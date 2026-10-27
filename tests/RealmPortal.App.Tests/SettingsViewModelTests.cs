using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Settings;
using RealmPortal.App.ViewModels;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

public class SettingsViewModelTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        private SettingsModel _model = new(
            Language.En,
            "wine {exe}",
            [],
            [],
            new Dictionary<string, string?>(),
            "/tmp/backups");

        public SettingsModel Load() => _model;

        public SettingsError? Save(SettingsModel settings)
        {
            if (string.IsNullOrWhiteSpace(settings.CommandTemplate)
                || !settings.CommandTemplate.Contains("{exe}", StringComparison.Ordinal))
            {
                return new SettingsError("InvalidLauncherSettings");
            }

            if (string.IsNullOrWhiteSpace(settings.BackupFolder))
            {
                return new SettingsError("PathNotFound");
            }

            _model = settings;
            return null;
        }
    }

    [AvaloniaFact]
    public void ApplySavedLanguage_RestoresPersistedLanguage()
    {
        Loc.Instance.SetLanguage(Language.En);
        var store = new MemoryStore();
        store.Save(new SettingsModel(Language.Es, "wine {exe}", [], [], new Dictionary<string, string?>(), "/tmp/backups"));
        var vm = new SettingsViewModel(store);
        Loc.Instance.SetLanguage(Language.En);
        vm.ApplySavedLanguage();
        Assert.Equal(Language.Es, Loc.Instance.Current);
    }

    [AvaloniaFact]
    public async Task Browse_AwaitsTheFolderPicker()
    {
        var vm = new SettingsViewModel(new MemoryStore(), () => Task.FromResult<string?>("/tmp/picked-backups"));
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("/tmp/picked-backups", vm.BackupFolder);
    }

    [AvaloniaFact]
    public async Task Save_RejectsEmptyOrMissingExeTemplate()
    {
        var vm = new SettingsViewModel(new MemoryStore());
        vm.Template = "   ";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.TemplateError);

        vm.Template = "wine";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.NotNull(vm.TemplateError);
    }

    [AvaloniaFact]
    public void Cancel_DiscardsDirtyFieldsAndLanguagePreview()
    {
        var vm = new SettingsViewModel(new MemoryStore());
        vm.Template = "wine {exe} -windowed";
        vm.IsEnglish = false;
        Assert.True(vm.IsDirty);

        vm.CancelCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.Equal(Language.En, Loc.Instance.Current);
        Assert.Equal("wine {exe}", vm.Template);
    }

    [AvaloniaFact]
    public void ResolveLanguage_NullKeepsCurrent_ExplicitCodesStay()
    {
        using var temp = new TempDirectory();
        var dir = temp.Combine("config");
        Directory.CreateDirectory(dir);
        var store = new UiPreferencesStore(dir);
        var original = Loc.Instance.Current;
        try
        {
            Loc.Instance.SetLanguage(Language.En);
            Assert.Equal(Language.En, store.ResolveLanguage());
            Loc.Instance.SetLanguage(Language.Es);
            Assert.Equal(Language.Es, store.ResolveLanguage());

            store.SetLanguage(Language.En);
            Loc.Instance.SetLanguage(Language.Es);
            Assert.Equal(Language.En, store.ResolveLanguage());

            store.SetLanguage(Language.Es);
            Loc.Instance.SetLanguage(Language.En);
            Assert.Equal(Language.Es, store.ResolveLanguage());

            store.Save(store.Load() with { LanguageCode = null });
            Loc.Instance.SetLanguage(Language.En);
            Assert.Equal(Language.En, store.ResolveLanguage());
        }
        finally
        {
            Loc.Instance.SetLanguage(original);
        }
    }

    [AvaloniaFact]
    public void TemplateWatermarkKey_IsDefinedInBothLanguages()
    {
        Assert.Equal("e.g. wine {exe}", Loc.Load(Language.En)["settings.template.watermark"]);
        Assert.Equal("ej.: wine {exe}", Loc.Load(Language.Es)["settings.template.watermark"]);
    }
}
