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
    TimedOut,
}

/// <summary>The add-server form (demo or portal).</summary>
public sealed partial class AddServerViewModel : ViewModelBase
{
    public const int DefaultPort = 3724;

    /// <summary>How long a portal connection check waits before it reports a timeout.</summary>
    public static readonly TimeSpan ConnectionProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly DemoBackend? _backend;
    private readonly PortalSession? _session;
    private readonly IConnectionProbe? _probe;
    private readonly Action<ServerSeed>? _saveDemo;
    private readonly Action? _savePortal;
    private readonly Action _cancel;
    private CancellationTokenSource? _probeCts;
    private int _probeGeneration;
    private bool _formClosed;

    public AddServerViewModel(ClientsViewModel clients, DemoBackend backend, Action<ServerSeed> save, Action cancel)
    {
        _backend = backend;
        _saveDemo = save;
        _cancel = cancel;
        Clients = clients;
        SelectedClient = clients.Items.FirstOrDefault(c => c.IsInUse) ?? clients.Items.FirstOrDefault();
    }

    public AddServerViewModel(ClientsViewModel clients, PortalSession session, Action save, Action cancel, IConnectionProbe? probe = null)
    {
        _session = session;
        _probe = probe ?? new TcpConnectionProbe();
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

    /// <summary>Service failure from the last save attempt. Text is derived so a language change re-reads it.</summary>
    private ServiceError? _saveServiceError;

    /// <summary>The last save failed without a <see cref="ServiceError"/> (unknown body).</summary>
    private bool _saveFailedUnknown;

    /// <summary>Localized service failure from the last save attempt. Not a connection-test result.</summary>
    public string? SaveError => _saveServiceError is null
        ? (_saveFailedUnknown ? Loc.Get("error.unknown.body") : null)
        : ErrorText.Body(_saveServiceError);

    public bool HasSaveError => SaveError is not null;

    public bool HasResult => TestState != ConnectionTest.None;

    public ServerStatus ResultStatus => TestState switch
    {
        ConnectionTest.Ok => ServerStatus.Ready,
        ConnectionTest.Failed => ServerStatus.NoResponse,
        ConnectionTest.TimedOut => ServerStatus.NoResponse,
        _ => ServerStatus.Inactive,
    };

    public string ResultText => TestState switch
    {
        ConnectionTest.Ok => Loc.Get("add.result.ok"),
        ConnectionTest.Failed => Loc.Get("add.result.fail"),
        ConnectionTest.TimedOut => Loc.Get("add.result.timeout"),
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

    /// <summary>host or host:port; the port defaults to <see cref="DefaultPort"/>. Same rules as a realmlist host.</summary>
    public static bool TryParseAddress(string? text, out string host, out int port) =>
        RealmlistAddress.TryParseHostAddress(text, out host, out port, DefaultPort);

    /// <summary>Drops the brackets around an IPv6 literal so a connect/DNS call sees <c>::1</c>, not <c>[::1]</c>.</summary>
    internal static string HostForProbe(string host)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            return host[1..^1];
        }

        return host;
    }

    public override void RefreshText()
    {
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(AddressError));
        OnPropertyChanged(nameof(ClientError));
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
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
        ClearSaveError();
        if (!TryParseAddress(Address, out var host, out var port))
        {
            TestState = ConnectionTest.None;
            return;
        }

        if (_session is not null)
        {
            if (_formClosed)
            {
                return;
            }

            // A new test replaces any in-flight one. The old completion must not write TestState.
            CancelInFlightProbe();
            var generation = ++_probeGeneration;
            var cts = new CancellationTokenSource();
            _probeCts = cts;
            TestState = ConnectionTest.Testing;
            var testedAddress = Address;
            ProbeResult result;
            try
            {
                // TryParseHostAddress keeps the brackets on an IPv6 literal ("[::1]"). The probe resolves a host, not that token.
                result = await _probe!.ProbeAsync(HostForProbe(host), port, ConnectionProbeTimeout, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(_probeCts, cts))
                {
                    _probeCts = null;
                    cts.Dispose();
                }
            }

            // Closed, cancelled, or superseded while the probe was in flight: leave TestState as it is.
            if (_formClosed || cts.IsCancellationRequested || generation != _probeGeneration)
            {
                return;
            }

            // The field may have moved on while the probe was in flight. Do not paint the old host's result on the new address.
            if (Address != testedAddress)
            {
                TestState = ConnectionTest.None;
                return;
            }

            TestState = result switch
            {
                ProbeResult.Ok => ConnectionTest.Ok,
                ProbeResult.TimedOut => ConnectionTest.TimedOut,
                _ => ConnectionTest.Failed,
            };
            return;
        }

        TestState = ConnectionTest.Testing;
        await _backend!.DelayAsync(TimeSpan.FromMilliseconds(900)).ConfigureAwait(true);
        if (_formClosed)
        {
            return;
        }

        var answers = !host.Contains("offline", StringComparison.OrdinalIgnoreCase)
                      && !host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase);
        TestState = answers ? ConnectionTest.Ok : ConnectionTest.Failed;
    }

    private bool CanTest() => TestState != ConnectionTest.Testing;

    [RelayCommand]
    private void Save()
    {
        Submitted = true;
        ClearSaveError();
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

            var hostForServer = port == DefaultPort ? host : $"{host}:{port}";
            var result = _session.Service.AddServer(new NewServer(
                Name.Trim(),
                hostForServer,
                Account: account.Length == 0 ? null : account,
                ClientId: clientId));
            if (!result.Succeeded)
            {
                SetSaveError(result.Error);
                return;
            }

            OnFormClosed();
            _savePortal!();
            return;
        }

        OnFormClosed();
        _saveDemo!(new ServerSeed(Name.Trim(), host, port, SelectedClient.Version, account.Length == 0 ? null : account, IsActive: false, IsCompatible: true));
    }

    [RelayCommand]
    private void Cancel()
    {
        OnFormClosed();
        _cancel();
    }

    /// <summary>
    /// The add-server form was dismissed (Cancel, a successful save, or the shell leaving the form).
    /// Cancels the in-flight probe so a late result does not change <see cref="TestState"/>.
    /// </summary>
    public void OnFormClosed()
    {
        _formClosed = true;
        CancelInFlightProbe();
    }

    private void CancelInFlightProbe()
    {
        var cts = _probeCts;
        _probeCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    partial void OnNameChanged(string value) => ClearSaveError();

    partial void OnAccountChanged(string value) => ClearSaveError();

    partial void OnSelectedClientChanged(ClientItemViewModel? value) => ClearSaveError();

    partial void OnAddressChanged(string value)
    {
        ClearSaveError();
        if (TestState != ConnectionTest.Testing)
        {
            TestState = ConnectionTest.None;
        }
    }

    private void SetSaveError(ServiceError? error)
    {
        _saveServiceError = error;
        _saveFailedUnknown = error is null;
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
    }

    private void ClearSaveError()
    {
        if (_saveServiceError is null && !_saveFailedUnknown)
        {
            return;
        }

        _saveServiceError = null;
        _saveFailedUnknown = false;
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
    }
}
