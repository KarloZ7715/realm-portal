using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core.Service;

namespace RealmPortal.App.ViewModels;

/// <summary>
/// The clients screen. Demo mode uses <see cref="DemoData"/>; portal mode uses <see cref="IPortalService"/> and optional folder picking.
/// </summary>
public sealed partial class ClientsViewModel : ViewModelBase
{
    private readonly DemoBackend? _backend;
    private readonly PortalSession? _session;
    private readonly Func<Task<string?>>? _pickFolder;
    private int _picked;

    public ClientsViewModel(DemoBackend backend)
    {
        _backend = backend;
        foreach (var seed in DemoData.Clients)
        {
            AddDemo(seed);
        }
    }

    public ClientsViewModel(PortalSession session, Func<Task<string?>>? pickFolder = null)
    {
        _session = session;
        _pickFolder = pickFolder;
        ReloadFromService();
        if (!session.UiPreferences.Load().FirstScanCompleted)
            _ = RunScanAsync(markFirstScanCompleted: true, reportFailure: false);
    }

    public event Action? ClientsChanged;

    public ObservableCollection<ClientItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand), nameof(PickFolderCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _notice;

    public int InUseCount => Items.Count(i => i.IsInUse);

    public string Summary => Loc.Format(
        "clients.summary",
        Items.Count == 1 ? Loc.Get("clients.found.one") : Loc.Format("clients.found.many", Items.Count),
        InUseCount);

    public override string StatusText =>
        IsScanning ? Loc.Get("status.scanning") : Notice ?? Loc.Format("status.clients", Items.Count);

    public override void RefreshText()
    {
        foreach (var item in Items)
        {
            item.RefreshText();
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StatusText));
    }

    public void ReloadFromService()
    {
        if (_session is null)
        {
            return;
        }

        var prefs = _session.UiPreferences.Load();
        var inUse = prefs.InUseSet;
        var list = _session.Service.ListClients();
        Items.Clear();
        if (!list.Succeeded)
        {
            Notice = ErrorText.Body(list.Error!);
            return;
        }

        foreach (var entry in list.Value!)
        {
            AddPortal(entry, inUse.Contains(entry.Id));
        }

        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StatusText));
        ClientsChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task RescanAsync()
    {
        if (_session is not null)
        {
            IsScanning = true;
            Notice = null;
            await RunScanAsync(markFirstScanCompleted: true, reportFailure: true).ConfigureAwait(true);
            return;
        }

        IsScanning = true;
        Notice = null;
        await _backend!.DelayAsync(TimeSpan.FromMilliseconds(900)).ConfigureAwait(true);
        IsScanning = false;
    }

    private async Task RunScanAsync(bool markFirstScanCompleted, bool reportFailure)
    {
        if (_session is null)
            return;

        IsScanning = true;
        try
        {
            var outcome = await Task.Run(() => _session.Service.Scan()).ConfigureAwait(true);
            if (!outcome.Succeeded)
            {
                if (reportFailure)
                    Notice = ErrorText.Body(outcome.Error!);
                return;
            }

            var preferences = _session.UiPreferences.Load();
            var inUse = preferences.InUseSet;
            foreach (var client in outcome.Value!.AddedClients)
                inUse.Add(client.Id);
            _session.UiPreferences.Save(preferences with
            {
                ClientsInUse = inUse.ToList(),
                FirstScanCompleted = markFirstScanCompleted || preferences.FirstScanCompleted,
            });
            if (outcome.Value.AddedClients.Count > 0)
                ReloadFromService();
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task PickFolderAsync()
    {
        if (_session is not null)
        {
            if (_pickFolder is null)
            {
                Notice = Loc.Get("clients.pick.unavailable");
                return;
            }

            var folder = await _pickFolder().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var result = await Task.Run(() => _session.Service.AddClient(folder)).ConfigureAwait(true);
            if (!result.Succeeded)
            {
                Notice = ErrorText.Body(result.Error!);
                OnPropertyChanged(nameof(StatusText));
                return;
            }

            var prefs = _session.UiPreferences.Load();
            var ids = prefs.InUseSet;
            ids.Add(result.Value!.Id);
            _session.UiPreferences.Save(prefs with { ClientsInUse = ids.ToList() });
            Notice = Loc.Get("status.folderadded.real");
            ReloadFromService();
            return;
        }

        if (_picked >= DemoData.PickableClients.Count)
        {
            Notice = Loc.Get("status.nomorefolders");
            return;
        }

        AddDemo(DemoData.PickableClients[_picked++]);
        Notice = Loc.Get("status.folderadded");
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StatusText));
    }

    private bool CanScan() => !IsScanning;

    private void AddDemo(ClientSeed seed)
    {
        var item = new ClientItemViewModel(seed);
        WireInUse(item);
        Items.Add(item);
    }

    private void AddPortal(Core.Service.ClientEntry entry, bool inUse)
    {
        var item = ClientItemViewModel.FromEntry(entry, inUse);
        WireInUse(item);
        Items.Add(item);
    }

    private void WireInUse(ClientItemViewModel item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientItemViewModel.IsInUse))
            {
                if (_session is not null)
                {
                    PersistInUse();
                }

                OnPropertyChanged(nameof(Summary));
                ClientsChanged?.Invoke();
            }
        };
    }

    private void PersistInUse()
    {
        var ids = Items.Where(i => i.IsInUse).Select(i => i.Id).Where(id => id is not null).Cast<string>().ToList();
        _session!.UiPreferences.Save(_session.UiPreferences.Load() with { ClientsInUse = ids });
    }
}
