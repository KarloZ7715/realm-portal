using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Settings;

namespace RealmPortal.App.ViewModels;

/// <summary>
/// The Settings screen: language, launcher (command template, prefix, arguments, environment) and the backup folder.
/// The language applies as soon as it is picked (a live preview, like the status bar toggle); everything is only persisted by Save,
/// and Cancel puts the saved values (and language) back. Clear Cache/WDB on switch is off by default (#106).
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private readonly Func<Task<string?>>? _pickFolder;
    private SettingsModel _saved;
    private bool _justSaved;

    public SettingsViewModel(ISettingsStore store, Func<Task<string?>>? pickFolder = null)
    {
        _store = store;
        _pickFolder = pickFolder;
        _saved = store.Load();
        Reset();
    }

    /// <summary>Switches the UI to the saved language; the app calls it once at startup.</summary>
    public void ApplySavedLanguage()
    {
        Loc.Instance.SetLanguage(_saved.Language);
        Changed();
    }

    /// <summary>
    /// Records <paramref name="language"/> as the saved language. The status-bar toggle saves the language right away,
    /// so it is not an unsaved Settings change; the other fields keep their pending edits.
    /// </summary>
    public void AcceptSavedLanguage(Language language)
    {
        _saved = _saved with { Language = language };
        Changed();
    }

    public event Action? OpenBackupsManagerRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBackupsManagerCommand))]
    private bool _canManageBackups;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(StatusText), nameof(TemplateError))]
    private string _template = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(StatusText))]
    private string _prefix = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(StatusText))]
    private string _arguments = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(StatusText), nameof(EnvironmentError))]
    private string _environment = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(StatusText), nameof(BackupError))]
    private string _backupFolder = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(StatusText))]
    private bool _clearCacheOnSwitch;

    /// <summary>Which field the last failed save blamed: "template", "backup" or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplateError), nameof(BackupError))]
    private string? _serviceErrorField;

    public bool IsSpanish
    {
        get => Loc.Instance.Current == Language.Es;
        set
        {
            if (value)
            {
                SetLanguage(Language.Es);
            }
        }
    }

    public bool IsEnglish
    {
        get => Loc.Instance.Current == Language.En;
        set
        {
            if (value)
            {
                SetLanguage(Language.En);
            }
        }
    }

    public bool IsDirty =>
        Loc.Instance.Current != _saved.Language
        || Template != (_saved.CommandTemplate ?? string.Empty)
        || Prefix != LauncherText.Join(_saved.Prefix)
        || Arguments != LauncherText.Join(_saved.Arguments)
        || Environment != LauncherText.JoinEnvironment(_saved.Environment)
        || BackupFolder != _saved.BackupFolder
        || ClearCacheOnSwitch != _saved.ClearCacheOnSwitch;

    public bool CanSave => IsDirty;

    /// <summary>The first malformed environment line, as text; null when the box is fine.</summary>
    public string? EnvironmentError =>
        LauncherText.TryParseEnvironment(Environment, out _) is { } line ? Loc.Format("settings.error.env", line) : null;

    public string? TemplateError => ServiceErrorField == "template" ? Loc.Get("settings.error.launcher") : null;

    public string? BackupError => ServiceErrorField == "backup" ? Loc.Get("settings.error.backup") : null;

    public override string StatusText => IsDirty
        ? Loc.Get("settings.status.dirty")
        : Loc.Get(_justSaved ? "settings.status.saved" : "nav.settings");

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        ServiceErrorField = null;
        if (EnvironmentError is not null)
        {
            return;
        }

        if (!IsValidTemplate(Template))
        {
            ServiceErrorField = "template";
            Changed();
            return;
        }

        IReadOnlyDictionary<string, string?> environment;
        if (Environment == LauncherText.JoinEnvironment(_saved.Environment))
        {
            environment = _saved.Environment;
        }
        else
        {
            LauncherText.TryParseEnvironment(Environment, out environment);
        }

        var model = new SettingsModel(
            Loc.Instance.Current,
            Template.Trim(),
            LauncherText.Tokens(Prefix),
            LauncherText.Tokens(Arguments),
            environment,
            BackupFolder.Trim(),
            ClearCacheOnSwitch);

        var error = _store.Save(model);
        if (error is not null)
        {
            ServiceErrorField = error.Code == "InvalidLauncherSettings" ? "template" : "backup";
            return;
        }

        _saved = model;
        Reset();
        _justSaved = true;
        Changed();
    }

    [RelayCommand]
    private void Cancel()
    {
        Reset();
        Loc.Instance.SetLanguage(_saved.Language);
        Changed();
    }

    [RelayCommand(CanExecute = nameof(CanOpenBackupsManager))]
    private void OpenBackupsManager() => OpenBackupsManagerRequested?.Invoke();

    private bool CanOpenBackupsManager() => CanManageBackups;

    internal static bool IsValidTemplate(string? template) =>
        !string.IsNullOrWhiteSpace(template)
        && template.Contains("{exe}", StringComparison.Ordinal);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (_pickFolder is null)
        {
            return;
        }

        var folder = await _pickFolder().ConfigureAwait(true);
        if (folder is { Length: > 0 })
        {
            BackupFolder = folder;
        }
    }

    public override void RefreshText() => Changed();

    private void SetLanguage(Language language)
    {
        if (Loc.Instance.Current != language)
        {
            Loc.Instance.SetLanguage(language);
        }
    }

    private void Reset()
    {
        Template = _saved.CommandTemplate ?? string.Empty;
        Prefix = LauncherText.Join(_saved.Prefix);
        Arguments = LauncherText.Join(_saved.Arguments);
        Environment = LauncherText.JoinEnvironment(_saved.Environment);
        BackupFolder = _saved.BackupFolder;
        ClearCacheOnSwitch = _saved.ClearCacheOnSwitch;
        ServiceErrorField = null;
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsSpanish));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(EnvironmentError));
        OnPropertyChanged(nameof(TemplateError));
        OnPropertyChanged(nameof(BackupError));
        SaveCommand.NotifyCanExecuteChanged();
    }

    partial void OnTemplateChanged(string value)
    {
        if (ServiceErrorField == "template")
        {
            ServiceErrorField = null;
        }

        EditedField();
    }

    partial void OnPrefixChanged(string value) => EditedField();

    partial void OnArgumentsChanged(string value) => EditedField();

    partial void OnEnvironmentChanged(string value) => EditedField();

    partial void OnBackupFolderChanged(string value)
    {
        if (ServiceErrorField == "backup")
        {
            ServiceErrorField = null;
        }

        EditedField();
    }

    partial void OnClearCacheOnSwitchChanged(bool value) => EditedField();

    private void EditedField()
    {
        _justSaved = false;
        SaveCommand.NotifyCanExecuteChanged();
    }
}
