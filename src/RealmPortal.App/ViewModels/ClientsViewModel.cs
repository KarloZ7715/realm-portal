using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;

namespace RealmPortal.App.ViewModels;

/// <summary>
/// The clients screen: the clients Realm Portal found and which of them are in use. Scanning and picking a folder are simulated:
/// the list comes from <see cref="DemoData"/> and no folder is read.
/// </summary>
public sealed partial class ClientsViewModel : ViewModelBase
{
    private readonly DemoBackend _backend;
    private int _picked;

    public ClientsViewModel(DemoBackend backend)
    {
        _backend = backend;
        foreach (var seed in DemoData.Clients)
        {
            Add(seed);
        }
    }

    public ObservableCollection<ClientItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand), nameof(PickFolderCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _notice;

    public int InUseCount => Items.Count(i => i.IsInUse);

    /// <summary>"3 clientes encontrados · 2 en uso. Solo leemos las rutas."</summary>
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

    /// <summary>Simulated search: the same clients come back after a short wait.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task RescanAsync()
    {
        IsScanning = true;
        Notice = null;
        await _backend.DelayAsync(TimeSpan.FromMilliseconds(900)).ConfigureAwait(true);
        IsScanning = false;
    }

    /// <summary>Simulated folder picker: adds the next made-up folder (nothing is read from disk).</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private void PickFolder()
    {
        if (_picked >= DemoData.PickableClients.Count)
        {
            Notice = Loc.Get("status.nomorefolders");
            return;
        }

        Add(DemoData.PickableClients[_picked++]);
        Notice = Loc.Get("status.folderadded");
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StatusText));
    }

    private bool CanScan() => !IsScanning;

    private void Add(ClientSeed seed)
    {
        var item = new ClientItemViewModel(seed);
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientItemViewModel.IsInUse))
            {
                OnPropertyChanged(nameof(Summary));
            }
        };
        Items.Add(item);
    }
}
