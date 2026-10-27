using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;
using RealmPortal.Core;

namespace RealmPortal.App.ViewModels;

public sealed partial class BackupsViewModel : ViewModelBase
{
    private readonly PortalSession _session;
    private readonly Action _backRequested;

    public BackupsViewModel(PortalSession session, Action backRequested)
    {
        _session = session;
        _backRequested = backRequested;
        LoadClients();
    }

    public ObservableCollection<BackupClientOption> Clients { get; } = [];
    public ObservableCollection<BackupItemViewModel> Backups { get; } = [];
    public Func<BackupEntry, Task<bool>>? ConfirmDeleteBackupAsync { get; set; }
    public Func<BackupEntry, Task<ConfirmDialogChoice>>? ResolveRestoreConflictAsync { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackups), nameof(CountText), nameof(StatusText))]
    private BackupClientOption? _selectedClient;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    private bool _isGameRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    private bool _isGameStateUnknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _notice;

    public bool HasBackups => Backups.Count > 0;
    public string CountText => Loc.Format("backups.count", Backups.Count, ClientFilesApplier.DefaultKeepBackups);
    public override string StatusText => Notice ?? (SelectedClient is null
        ? Loc.Get("backups.empty")
        : IsGameRunning ? Loc.Get("clients.state.running") : Loc.Format("backups.count", Backups.Count, ClientFilesApplier.DefaultKeepBackups));

    public override void RefreshText()
    {
        foreach (var backup in Backups)
            backup.RefreshText();
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(StatusText));
    }

    [RelayCommand]
    private void Back() => _backRequested();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadBackupsAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanCreateBackup))]
    private async Task CreateBackupAsync()
    {
        if (SelectedClient is not { } selected)
            return;
        IsBusy = true;
        Notice = null;
        try
        {
            RefreshRunningState();
            if (IsGameRunning || IsGameStateUnknown)
                return;
            var result = await Task.Run(() => _session.Service.CreateBackup(selected.Entry.Id)).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                Notice = ErrorText.Body(result.Error!);
                return;
            }

            var backup = result.Value!;
            Notice = Loc.Format(backup.WasDeduplicated ? "backups.status.deduplicated" : "backups.status.created", backup.BackupId);
            await LoadBackupsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            CreateBackupCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCreateBackup() => SelectedClient is not null && !IsGameRunning && !IsGameStateUnknown && !IsBusy;

    private async Task RestoreAsync(BackupItemViewModel item)
    {
        if (SelectedClient is not { } selected || item.Entry.Error is not null || IsBusy)
            return;
        RefreshRunningState();
        if (IsGameRunning || IsGameStateUnknown)
            return;

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _session.Service.RestoreBackup(selected.Entry.Id, item.Entry.BackupId)).ConfigureAwait(true);
            if (!result.Succeeded && result.Error!.Code == ServiceErrorCode.UndoConflict && ResolveRestoreConflictAsync is not null)
            {
                var choice = await ResolveRestoreConflictAsync(item.Entry).ConfigureAwait(true);
                if (choice == ConfirmDialogChoice.Confirm)
                {
                    result = await Task.Run(() => _session.Service.RestoreBackup(selected.Entry.Id, item.Entry.BackupId, force: true)).ConfigureAwait(true);
                }
                else if (choice == ConfirmDialogChoice.Secondary)
                {
                    var discarded = await Task.Run(() => _session.Service.DiscardUndo(selected.Entry.Id)).ConfigureAwait(true);
                    Notice = discarded.Succeeded ? Loc.Get("backups.status.undo.discarded") : ErrorText.Body(discarded.Error!);
                    await LoadBackupsAsync().ConfigureAwait(true);
                    return;
                }
                else
                {
                    return;
                }
            }

            if (!result.Succeeded)
            {
                Notice = ErrorText.Body(result.Error!);
                return;
            }

            Notice = Loc.Format("backups.status.restored", result.Value!.RestoredBackupId);
            await LoadBackupsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteAsync(BackupItemViewModel item)
    {
        if (SelectedClient is not { } selected || IsBusy || ConfirmDeleteBackupAsync is null)
            return;
        RefreshRunningState();
        if (IsGameRunning || IsGameStateUnknown || !await ConfirmDeleteBackupAsync(item.Entry).ConfigureAwait(true))
            return;

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _session.Service.DeleteBackup(selected.Entry.Id, item.Entry.BackupId)).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                Notice = ErrorText.Body(result.Error!);
                return;
            }

            Notice = Loc.Get("backups.status.deleted");
            await LoadBackupsAsync().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadClients()
    {
        var result = _session.Service.ListClients();
        if (!result.Succeeded)
        {
            Notice = ErrorText.Body(result.Error!);
            return;
        }

        foreach (var client in result.Value!)
        {
            // The version is shown once: "Client 3.3.5a (12340)" without a name, "Name · 3.3.5a (12340)" with one.
            var version = VersionText.Of(client.Version);
            var label = string.IsNullOrWhiteSpace(client.Name) ? VersionText.ClientTitle(client.Version) : VersionText.WithName(client.Name, version);
            Clients.Add(new BackupClientOption(client, label));
        }
        SelectedClient = Clients.FirstOrDefault();
    }

    private async Task LoadBackupsAsync()
    {
        if (SelectedClient is not { } selected)
        {
            Backups.Clear();
            Changed();
            return;
        }

        RefreshRunningState();
        var result = await Task.Run(() => _session.Service.ListBackups(selected.Entry.Id)).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            Notice = ErrorText.Body(result.Error!);
            return;
        }

        Backups.Clear();
        foreach (var entry in result.Value!)
            Backups.Add(new BackupItemViewModel(entry, () => IsGameRunning || IsGameStateUnknown, RestoreAsync, DeleteAsync));
        Changed();
    }

    private void RefreshRunningState()
    {
        if (SelectedClient is not { } selected)
        {
            IsGameRunning = false;
            IsGameStateUnknown = true;
            return;
        }
        var running = _session.Service.IsGameRunning(selected.Entry.Id);
        IsGameRunning = running.Succeeded && running.Value;
        IsGameStateUnknown = !running.Succeeded;
        foreach (var backup in Backups)
            backup.RefreshAvailability();
    }

    partial void OnSelectedClientChanged(BackupClientOption? value)
    {
        ExceptionLog.Observe(LoadBackupsAsync());
        CreateBackupCommand.NotifyCanExecuteChanged();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(StatusText));
    }
}

public sealed record BackupClientOption(ClientEntry Entry, string Label);

public sealed partial class BackupItemViewModel : ObservableObject
{
    private readonly Func<bool> _isBlocked;
    private readonly Func<BackupItemViewModel, Task> _restore;
    private readonly Func<BackupItemViewModel, Task> _delete;

    public BackupItemViewModel(BackupEntry entry, Func<bool> isBlocked,
        Func<BackupItemViewModel, Task> restore, Func<BackupItemViewModel, Task> delete)
    {
        Entry = entry;
        _isBlocked = isBlocked;
        _restore = restore;
        _delete = delete;
    }

    public BackupEntry Entry { get; }
    public string Timestamp => Entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Label => Entry.ServerName is { Length: > 0 } server
        ? Loc.Format("backups.item.server", server)
        : Entry.Label ?? Loc.Get("backups.item.manual");
    public string FilesText => string.Join(", ", Entry.Files.Select(Path.GetFileName));
    public string SizeText => Entry.TotalBytes < 1024 ? $"{Entry.TotalBytes} B" : $"{Entry.TotalBytes / 1024d:0.#} KB";
    public string Meta => Loc.Format("backups.item.meta", Entry.Files.Count, FilesText, SizeText);
    public bool IsDamaged => Entry.Error is not null;
    public string Badge => IsDamaged ? Loc.Get("backups.badge.damaged")
        : Entry.IsPendingUndo ? Loc.Get("backups.badge.pendingundo")
        : Entry.UndoneAt is not null ? Loc.Get("backups.badge.restored") : string.Empty;
    public bool HasBadge => Badge.Length > 0;
    public string ErrorText => IsDamaged ? Loc.Get("backups.item.corrupt") : string.Empty;
    public bool CanRestore => !IsDamaged && !_isBlocked();
    public bool CanDelete => !_isBlocked();

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private Task RestoreAsync() => _restore(this);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => _delete(this);

    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanRestore));
        OnPropertyChanged(nameof(CanDelete));
        RestoreCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    public void RefreshText()
    {
        OnPropertyChanged(string.Empty);
        RefreshAvailability();
    }
}
