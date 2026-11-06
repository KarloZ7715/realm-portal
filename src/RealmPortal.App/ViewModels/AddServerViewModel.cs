using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.Core.Network;
using RealmPortal.Core.Clients;
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
    private readonly string? _editingServerId;
    private readonly ServerBuildOption? _initialBuild;
    private readonly IReadOnlyList<string>? _extraRealmlistLines;
    private readonly bool _isDuplicating;
    private readonly string? _sourceServerId;

    public AddServerViewModel(ClientsViewModel clients, DemoBackend backend, Action<ServerSeed> save, Action cancel)
    {
        _backend = backend;
        _saveDemo = save;
        _cancel = cancel;
        Clients = clients;
        SelectedClient = clients.Items.FirstOrDefault(c => c.IsInUse) ?? clients.Items.FirstOrDefault();
        _initialInput = CurrentInput();
    }

    public AddServerViewModel(ClientsViewModel clients, PortalSession session, Action save, Action cancel, IConnectionProbe? probe = null)
    {
        _session = session;
        _probe = probe ?? new TcpConnectionProbe();
        _savePortal = save;
        _cancel = cancel;
        Clients = clients;
        SelectedClient = clients.Items.FirstOrDefault(c => c.IsInUse) ?? clients.Items.FirstOrDefault();
        _initialInput = CurrentInput();
    }

    /// <summary>Edit mode: prefills the form from <paramref name="entry"/> and saves with <see cref="IPortalService.UpdateServer"/>.</summary>
    public AddServerViewModel(ClientsViewModel clients, PortalSession session, ServerEntry entry, Action save, Action cancel, IConnectionProbe? probe = null)
        : this(clients, session, save, cancel, probe)
    {
        _editingServerId = entry.Id;
        Name = entry.Name;
        Address = entry.Host;
        Realm = entry.Realm;
        Account = entry.Account ?? string.Empty;
        SelectedClient = clients.Items.FirstOrDefault(c => c.Id == entry.ClientId)
            ?? clients.Items.FirstOrDefault(c => c.IsInUse)
            ?? clients.Items.FirstOrDefault();

        // "Any client" plus the stored build and the builds of the saved clients; a server without a build preselects "Any client".
        var listed = session.Service.ListClients();
        var builds = (listed.Succeeded ? listed.Value! : [])
            .Where(c => c.Version is { HasBuild: true })
            .Select(c => c.Version!.Build)
            .Append(entry.RequiredClientBuild ?? 0)
            .Where(b => b > 0)
            .Distinct()
            .Order();
        BuildOptions = [new ServerBuildOption(null), .. builds.Select(b => new ServerBuildOption(b))];
        _initialBuild = BuildOptions.First(o => o.Build == entry.RequiredClientBuild);
        SelectedBuild = _initialBuild;
        ClearCacheOnSwitch = entry.ClearCacheOnSwitch;
        _initialInput = CurrentInput();
    }

    /// <summary>
    /// Duplicate mode (#191): prefilled from <paramref name="entry"/>; saving adds a new server (new id) and keeps the
    /// source's extra realmlist lines (patchlist, ...). Not edit mode: <see cref="IsEditing"/> stays false.
    /// </summary>
    public AddServerViewModel(ClientsViewModel clients, PortalSession session, ServerEntry entry, Action save, Action cancel, bool duplicate, IConnectionProbe? probe = null)
        : this(clients, session, save, cancel, probe)
    {
        if (!duplicate)
            throw new ArgumentException("Use the edit constructor when duplicate is false.", nameof(duplicate));
        _isDuplicating = true;
        _sourceServerId = entry.Id;
        _extraRealmlistLines = entry.ExtraRealmlistLines;
        Address = entry.Host;
        Realm = entry.Realm;
        Account = entry.Account ?? string.Empty;
        SelectedClient = clients.Items.FirstOrDefault(c => c.Id == entry.ClientId)
            ?? clients.Items.FirstOrDefault(c => c.IsInUse)
            ?? clients.Items.FirstOrDefault();

        var listed = session.Service.ListClients();
        var builds = (listed.Succeeded ? listed.Value! : [])
            .Where(c => c.Version is { HasBuild: true })
            .Select(c => c.Version!.Build)
            .Append(entry.RequiredClientBuild ?? 0)
            .Where(b => b > 0)
            .Distinct()
            .Order();
        BuildOptions = [new ServerBuildOption(null), .. builds.Select(b => new ServerBuildOption(b))];
        _initialBuild = BuildOptions.First(o => o.Build == entry.RequiredClientBuild);
        SelectedBuild = _initialBuild;
        ClearCacheOnSwitch = entry.ClearCacheOnSwitch;

        // Localized unique "<name> (copy)" / " (copy 2)" …; cut by grapheme so a trailing emoji is not split (#219 F2).
        var names = session.Service.ListServers(compatibleOnly: false) is { Succeeded: true, Value: { } servers }
            ? servers.Select(s => s.Name).ToList()
            : [];
        Name = CreateLocalizedDuplicateName(entry.Name, names);
        _initialInput = CurrentInput();
    }

    private (string Name, string Address, string Realm, string Account, ClientItemViewModel? Client, ServerBuildOption? Build, bool? ClearCache) _initialInput;

    private (string, string, string, string, ClientItemViewModel?, ServerBuildOption?, bool?) CurrentInput() =>
        (Name, Address, Realm, Account, SelectedClient, SelectedBuild, ClearCacheOnSwitch);

    /// <summary>True while every field still holds the value the form opened with (nothing typed or picked).</summary>
    public bool IsPristine => CurrentInput() == _initialInput;

    /// <summary>Edit mode only: which client builds the server accepts ("Any client" or one build).</summary>
    public IReadOnlyList<ServerBuildOption> BuildOptions { get; } = [];

    [ObservableProperty]
    private ServerBuildOption? _selectedBuild;

    public ClientsViewModel Clients { get; }

    /// <summary>True when the form edits an existing server instead of adding one.</summary>
    public bool IsEditing => _editingServerId is not null;

    public bool IsDuplicating => _isDuplicating;

    /// <summary>Build picker and Cache/WDB override: edit and duplicate (the copy keeps the source's values until changed).</summary>
    public bool ShowBuildAndCacheFields => IsEditing || IsDuplicating;

    public string FormTitle => Loc.Get(IsEditing ? "edit.server.title" : IsDuplicating ? "servers.duplicate.title" : "add.title");

    public string FormSubtitle => IsEditing
        ? Loc.Get("edit.server.subtitle")
        : IsDuplicating ? Loc.Get("servers.duplicate.subtitle") : string.Empty;

    public string SaveLabel => Loc.Get(IsEditing ? "edit.server.save" : IsDuplicating ? "servers.duplicate.save" : "add.save");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddressError), nameof(HasAddressError), nameof(AddressInterpretation), nameof(HasAddressInterpretation))]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _realm = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientError), nameof(HasClientError))]
    private ClientItemViewModel? _selectedClient;

    [ObservableProperty]
    private string _account = string.Empty;

    /// <summary>Per-server Cache/WDB clear override; <c>null</c> uses the global Settings toggle. Edit form only.</summary>
    [ObservableProperty]
    private bool? _clearCacheOnSwitch;

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

    /// <summary>Localization key of a client-side save failure (for example a duplicate address).</summary>
    private string? _saveMessageKey;

    /// <summary>Localized service failure from the last save attempt. Not a connection-test result.</summary>
    public string? SaveError => _saveServiceError is not null
        ? ErrorText.Body(_saveServiceError)
        : _saveMessageKey is not null ? Loc.Get(_saveMessageKey)
        : _saveFailedUnknown ? Loc.Get("error.state.unknown")
        : null;

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

    /// <summary>Shown under the address field when paste normalization produced a host (and optionally dropped other lines).</summary>
    public string? AddressInterpretation
    {
        get
        {
            if (!TryParseAddress(Address, out var host, out var port))
                return null;

            var pasted = RealmlistAddress.NormalizePastedAddress(Address);
            var display = port == DefaultPort ? host : $"{host}:{port}";
            var raw = Address.Trim();
            var changed = !string.Equals(raw, pasted.Token, StringComparison.Ordinal)
                || !string.Equals(raw, display, StringComparison.OrdinalIgnoreCase);
            if (!changed && !pasted.IgnoredOtherLines)
                return null;

            return pasted.IgnoredOtherLines
                ? Loc.Format("add.address.willuse.ignored", display)
                : Loc.Format("add.address.willuse", display);
        }
    }

    public bool HasAddressInterpretation => AddressInterpretation is not null;

    public override string StatusText => TestState == ConnectionTest.Testing
        ? Loc.Get("status.testing")
        : Loc.Get(IsDuplicating ? "status.duplicate" : "status.add");

    private bool IsNameValid => Name.Trim().Length is > 0 and <= Server.MaxNameLength;

    /// <summary>
    /// Host or host:port after paste normalization (<c>set realmlist …</c>, quotes, trailing <c>;</c>).
    /// The port defaults to <see cref="DefaultPort"/>. Same rules as a realmlist host.
    /// </summary>
    public static bool TryParseAddress(string? text, out string host, out int port) =>
        RealmlistAddress.TryParsePastedHostAddress(text, out host, out port, out _, DefaultPort);

    /// <summary>Drops the brackets around an IPv6 literal so a connect/DNS call sees <c>::1</c>, not <c>[::1]</c>.</summary>
    internal static string HostForProbe(string host)
    {
        if (host.Length >= 2 && host[0] == '[' && host[^1] == ']')
        {
            return host[1..^1];
        }

        return host;
    }

    /// <summary>
    /// What to send for the required build: nothing when the choice is untouched (Core keeps the stored build),
    /// the build when one is picked, or the clear flag for "Any client". Never a build and the flag together.
    /// </summary>
    internal (int? Build, bool Clear) BuildChange()
    {
        if (SelectedBuild is null || ReferenceEquals(SelectedBuild, _initialBuild))
        {
            return (null, false);
        }

        return SelectedBuild.Build is { } build ? (build, false) : (null, true);
    }

    public override void RefreshText()
    {
        foreach (var option in BuildOptions)
        {
            option.Refresh();
        }

        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(AddressError));
        OnPropertyChanged(nameof(AddressInterpretation));
        OnPropertyChanged(nameof(HasAddressInterpretation));
        OnPropertyChanged(nameof(ClientError));
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(FormSubtitle));
        OnPropertyChanged(nameof(SaveLabel));
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
            ConnectionProbeResult result;
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

            TestState = result.Outcome switch
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
        var realm = Realm.Trim();
        if (_session is not null)
        {
            var clientId = SelectedClient.Id;
            if (clientId is null)
            {
                return;
            }

            var hostForServer = port == DefaultPort ? host : $"{host}:{port}";
            // Duplicate mode may keep the source host+realm (and match earlier copies); Core DuplicateServer allows that.
            if (!IsDuplicating && IsDuplicateAddress(hostForServer, realm))
            {
                SetSaveMessage("add.error.duplicate");
                return;
            }

            var realmValue = realm.Length == 0 ? null : realm;
            var accountValue = account.Length == 0 ? null : account;
            var (build, clearBuild) = BuildChange();
            ServiceResult<ServerEntry> result;
            if (_editingServerId is { } serverId)
            {
                result = _session.Service.UpdateServer(new UpdateServerRequest(serverId, Name.Trim(), hostForServer, realmValue,
                    RequiredClientBuild: build, Account: accountValue, ClientId: clientId, ClearRequiredClientBuild: clearBuild,
                    ClearCacheOnSwitch: ClearCacheOnSwitch));
            }
            else if (IsDuplicating && _sourceServerId is { } sourceId)
            {
                // Core DuplicateServer (#219) allows the same host+realm as the source; then apply form fields it does not take.
                var created = _session.Service.DuplicateServer(sourceId, Name.Trim(), realmValue, accountValue);
                if (!created.Succeeded)
                {
                    SetSaveError(created.Error);
                    return;
                }

                result = _session.Service.UpdateServer(new UpdateServerRequest(created.Value!.Id, Name.Trim(), hostForServer, realmValue,
                    RequiredClientBuild: build, Account: accountValue, ClientId: clientId, ClearRequiredClientBuild: clearBuild,
                    ClearCacheOnSwitch: ClearCacheOnSwitch));
            }
            else
            {
                result = _session.Service.AddServer(new NewServer(Name.Trim(), hostForServer, realmValue, Account: accountValue, ClientId: clientId,
                    RequiredClientBuild: BuildOptions.Count > 0 ? SelectedBuild?.Build : null,
                    ExtraRealmlistLines: _extraRealmlistLines,
                    ClearCacheOnSwitch: ClearCacheOnSwitch));
            }

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

    /// <summary>
    /// Client-side guard for an existing host and realm: the service has no host-duplicate rule, so the
    /// form rejects it here (<c>add.error.duplicate</c>). The server being edited is not a match for itself.
    /// </summary>
    private bool IsDuplicateAddress(string hostForServer, string realm)
    {
        var servers = _session!.Service.ListServers(compatibleOnly: false);
        if (!servers.Succeeded)
        {
            return false;
        }

        return servers.Value!.Any(server =>
            server.Id != _editingServerId
            && server.Id != _sourceServerId
            && string.Equals(server.Host, hostForServer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(server.Realm.Trim(), realm, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Localized unique duplicate name: <c>"Name (copy)"</c> / <c>"Name (copia)"</c>, then <c>" (copy 2)"</c> / <c>" (copia 2)"</c>, …
    /// Prefixes are cut by whole text elements so a trailing emoji is not split (same rule as Core #219).
    /// </summary>
    internal static string CreateLocalizedDuplicateName(string name, IReadOnlyList<string> existingNames)
    {
        var basis = name.Trim();
        for (var number = 1; ; number++)
        {
            var tail = number == 1
                ? Loc.Get("servers.duplicate.suffix")
                : Loc.Format("servers.duplicate.suffix.n", number);
            var prefix = TruncateToTextElements(basis, Server.MaxNameLength - tail.Length).TrimEnd();
            var candidate = (prefix + tail).Trim();
            if (!existingNames.Any(n => string.Equals(n, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    /// <summary>Longest prefix of whole grapheme clusters within <paramref name="maxLength"/> UTF-16 chars.</summary>
    internal static string TruncateToTextElements(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext())
        {
            var next = elements.ElementIndex + ((string)elements.Current).Length;
            if (next > maxLength)
            {
                break;
            }

            end = next;
        }

        return value[..end];
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

    partial void OnRealmChanged(string value) => ClearSaveError();

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
        _saveMessageKey = null;
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
    }

    /// <summary>Shows a client-side save failure whose copy is a localization key.</summary>
    private void SetSaveMessage(string messageKey)
    {
        _saveServiceError = null;
        _saveFailedUnknown = false;
        _saveMessageKey = messageKey;
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
    }

    private void ClearSaveError()
    {
        if (_saveServiceError is null && !_saveFailedUnknown && _saveMessageKey is null)
        {
            return;
        }

        _saveServiceError = null;
        _saveFailedUnknown = false;
        _saveMessageKey = null;
        OnPropertyChanged(nameof(SaveError));
        OnPropertyChanged(nameof(HasSaveError));
    }
}

/// <summary>A choice in the edit form's "Compatible clients" box: <see cref="Build"/> null means any client.</summary>
public sealed partial class ServerBuildOption(int? build) : ObservableObject
{
    public int? Build { get; } = build;

    public string Label => Build is not { } b
        ? Loc.Get("edit.build.any")
        : Loc.Format("edit.build.only", KnownClientBuilds.TryGetPatchLabel(b, out var patch) ? $"{patch} ({b})" : b.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public void Refresh() => OnPropertyChanged(nameof(Label));
}
