using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;

namespace RealmPortal.App.ViewModels;

public enum ConnectionTest
{
    None,
    Testing,
    Ok,
    Failed,
}

/// <summary>The add-server form (demo or portal).</summary>
public sealed partial class AddServerViewModel : ViewModelBase
{
    public const int DefaultPort = 3724;

    private readonly DemoBackend? _backend;
    private readonly PortalSession? _session;
    private readonly Action<ServerSeed>? _saveDemo;
    private readonly Action? _savePortal;
    private readonly Action _cancel;

    public AddServerViewModel(ClientsViewModel clients, DemoBackend backend, Action<ServerSeed> save, Action cancel)
    {
        _backend = backend;
        _saveDemo = save;
        _cancel = cancel;
        Clients = clients;
        SelectedClient = clients.Items.FirstOrDefault(c => c.IsInUse) ?? clients.Items.FirstOrDefault();
    }

    public AddServerViewModel(ClientsViewModel clients, PortalSession session, Action save, Action cancel)
    {
        _session = session;
        _savePortal = save;
        _cancel = cancel;
        Clients = clients;
        SelectedClient = clients.Items.FirstOrDefault(c => c.IsInUse) ?? clients.Items.FirstOrDefault();
    }

    public ClientsViewModel Clients { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddressError), nameof(HasAddressError))]
    private string _address = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientError), nameof(HasClientError))]
    private ClientItemViewModel? _selectedClient;

    [ObservableProperty]
    private string _account = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError), nameof(AddressError), nameof(HasAddressError), nameof(ClientError), nameof(HasClientError))]
    private bool _submitted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(ResultText), nameof(ResultStatus), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private ConnectionTest _testState;

    public bool HasResult => TestState != ConnectionTest.None;

    public ServerStatus ResultStatus => TestState switch
    {
        ConnectionTest.Ok => ServerStatus.Ready,
        ConnectionTest.Failed => ServerStatus.NoResponse,
        _ => ServerStatus.Inactive,
    };

    public string ResultText => TestState switch
    {
        ConnectionTest.Ok => Loc.Get("add.result.ok"),
        ConnectionTest.Failed => Loc.Get("add.result.fail"),
        ConnectionTest.Testing => Loc.Get("add.result.testing"),
        _ => string.Empty,
    };

    public string? NameError => Submitted && !IsNameValid ? Loc.Get("add.error.name") : null;

    public string? AddressError => Submitted && !TryParseAddress(Address, out _, out _) ? Loc.Get("add.error.address") : null;

    public string? ClientError => Submitted && SelectedClient is null ? Loc.Get("add.error.client") : null;

    public bool HasNameError => NameError is not null;

    public bool HasAddressError => AddressError is not null;

    public bool HasClientError => ClientError is not null;

    public override string StatusText => TestState == ConnectionTest.Testing ? Loc.Get("status.testing") : Loc.Get("status.add");

    private bool IsNameValid => Name.Trim().Length is > 0 and <= Server.MaxNameLength;

    public static bool TryParseAddress(string? text, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultPort;
        var value = text?.Trim() ?? string.Empty;
        var match = AddressPattern().Match(value);
        if (!match.Success)
        {
            return false;
        }

        var parsedPort = DefaultPort;
        if (match.Groups["port"].Success && (!int.TryParse(match.Groups["port"].Value, out parsedPort) || parsedPort is < 1 or > 65535))
        {
            return false;
        }

        host = match.Groups["host"].Value;
        port = parsedPort;
        return true;
    }

    public override void RefreshText()
    {
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(AddressError));
        OnPropertyChanged(nameof(ClientError));
        OnPropertyChanged(nameof(StatusText));
        foreach (var client in Clients.Items)
        {
            client.RefreshText();
        }
    }

    [RelayCommand(CanExecute = nameof(CanTest))]
    private async Task TestAsync()
    {
        Submitted = true;
        if (!TryParseAddress(Address, out var host, out _))
        {
            TestState = ConnectionTest.None;
            return;
        }

        if (_session is not null)
        {
            TestState = ConnectionTest.Ok;
            return;
        }

        TestState = ConnectionTest.Testing;
        await _backend!.DelayAsync(TimeSpan.FromMilliseconds(900)).ConfigureAwait(true);
        var answers = !host.Contains("offline", StringComparison.OrdinalIgnoreCase)
                      && !host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase);
        TestState = answers ? ConnectionTest.Ok : ConnectionTest.Failed;
    }

    private bool CanTest() => TestState != ConnectionTest.Testing;

    [RelayCommand]
    private void Save()
    {
        Submitted = true;
        if (!IsNameValid || SelectedClient is null || !TryParseAddress(Address, out var host, out var port))
        {
            return;
        }

        var account = Account.Trim();
        if (_session is not null)
        {
            var clientId = SelectedClient.Id;
            if (clientId is null)
            {
                return;
            }

            var result = _session.Service.AddServer(new NewServer(
                Name.Trim(),
                host,
                Account: account.Length == 0 ? null : account,
                ClientId: clientId));
            if (!result.Succeeded)
            {
                TestState = ConnectionTest.Failed;
                return;
            }

            _savePortal!();
            return;
        }

        _saveDemo!(new ServerSeed(Name.Trim(), host, port, SelectedClient.Version, account.Length == 0 ? null : account, IsActive: false, IsCompatible: true));
    }

    [RelayCommand]
    private void Cancel() => _cancel();

    partial void OnAddressChanged(string value)
    {
        if (TestState != ConnectionTest.Testing)
        {
            TestState = ConnectionTest.None;
        }
    }

    [GeneratedRegex(@"^(?<host>[A-Za-z0-9](?:[A-Za-z0-9.\-]*[A-Za-z0-9])?)(?::(?<port>\d{1,5}))?$")]
    private static partial Regex AddressPattern();
}
