using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>The UI shows Core's ClientVersion.DisplayVersion ("3.3.5a (12340)") once per line (#99).</summary>
public sealed class ClientVersionDisplayTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly PortalSession _session;

    public ClientVersionDisplayTests()
    {
        var config = _temp.Combine("config");
        Directory.CreateDirectory(config);
        _session = PortalSession.CreateForPaths(config, _temp.Combine("projection"), _temp.Combine("backups"));
        Loc.Instance.SetLanguage(Language.En);
    }

    public void Dispose()
    {
        Loc.Instance.SetLanguage(Language.Es);
        _temp.Dispose();
    }

    private string AddClient(string folderName, Version version, string? name = null)
    {
        var folder = _temp.Combine(Path.Combine("games", folderName));
        Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
        File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(version));
        var id = _session.Service.AddClient(folder, name).Value!.Id;
        var prefs = _session.UiPreferences.Load();
        _session.UiPreferences.Save(prefs with { ClientsInUse = [.. prefs.ClientsInUse ?? [], id], FirstScanCompleted = true });
        return id;
    }

    [AvaloniaFact]
    public async Task KnownBuild_ShowsPatchAndBuildOnCardsClientsAndBackups()
    {
        var id = AddClient("World of Warcraft", new Version(3, 3, 5, 12340));
        _session.Service.AddServer(new NewServer("My Server", "logon.example.com", Account: "demo", ClientId: id));
        var main = new MainViewModel(_session, autoTick: false);

        var card = Assert.Single(main.Servers.All);
        Assert.Equal("3.3.5a (12340)", card.ClientVersion);
        Assert.Equal("Client 3.3.5a (12340) · Account: demo", card.DetailText);

        var client = Assert.Single(main.Clients.Items);
        Assert.Equal("World of Warcraft", client.Title);
        Assert.Equal("3.3.5a (12340)", client.Version);

        var backups = new BackupsViewModel(_session, () => { });
        await backups.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("World of Warcraft · 3.3.5a (12340)", Assert.Single(backups.Clients).Label);
    }

    [AvaloniaFact]
    public void UnknownBuild_FallsBackToTheFileVersion()
    {
        var id = AddClient("Custom", new Version(3, 3, 5, 12345));
        _session.Service.AddServer(new NewServer("Test", "test.example.org", ClientId: id));
        var main = new MainViewModel(_session, autoTick: false);

        Assert.Equal("Client 3.3.5 (12345) · No account", Assert.Single(main.Servers.All).DetailText);
        Assert.Equal("3.3.5 (12345)", Assert.Single(main.Clients.Items).Version);
    }

    [AvaloniaFact]
    public void ClientFilter_ShowsEachVersionOnce()
    {
        AddClient("Client-335", new Version(3, 3, 5, 12340), name: "WotLK");
        AddClient("Vanilla", new Version(1, 12, 1, 5875));
        var main = new MainViewModel(_session, autoTick: false);

        var labels = main.Servers.ClientFilterOptions.Skip(1).Select(o => o.Label).ToList();
        Assert.Equal(["WotLK · 3.3.5a (12340)", "Vanilla · 1.12.1 (5875)"], labels);
    }

    /// <summary>A saved client whose Wow.exe was later replaced by something that is not a Windows executable.</summary>
    private string AddBrokenClient(string folderName)
    {
        var id = AddClient(folderName, new Version(3, 3, 5, 12340));
        File.WriteAllBytes(_temp.Combine(Path.Combine("games", folderName, "Wow.exe")), [0x6E, 0x6F, 0x74, 0x20, 0x61, 0x20, 0x50, 0x45]);
        return id;
    }

    [AvaloniaFact]
    public async Task UnreadableVersion_IsLocalizedTheSameWayOnEveryScreen()
    {
        var id = AddBrokenClient("Broken");
        _session.Service.AddServer(new NewServer("Test", "test.example.org", ClientId: id));
        var main = new MainViewModel(_session, autoTick: false);
        var card = Assert.Single(main.Servers.All);
        var client = Assert.Single(main.Clients.Items);
        var backups = new BackupsViewModel(_session, () => { });
        await backups.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("Unknown version", card.ClientVersion);
        Assert.Equal("Unknown client version · No account", card.DetailText);
        Assert.Equal("Not a Windows executable", client.Version);
        Assert.Equal("Broken · Unknown version", Assert.Single(backups.Clients).Label);
        Assert.Equal("Broken · Unknown version", main.Servers.ClientFilterOptions[1].Label);
        Assert.DoesNotContain("?", card.DetailText + client.Version + client.Label, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientNotAPortableExecutable", client.Label, StringComparison.Ordinal);

        var window = new MainWindow(main) { Width = 1000, Height = 600 };
        window.Show();
        Screenshots.Settle();
        Screenshots.Save(window, 1.0, "unknown-version-servers-en-1.0x.png");
        main.SectionIndex = 1;
        Screenshots.Settle();
        Screenshots.Save(window, 1.0, "unknown-version-clients-en-1.0x.png");

        Loc.Instance.SetLanguage(Language.Es);
        main.Servers.RefreshText();
        main.Clients.RefreshText();
        Assert.Equal("Versión desconocida", card.ClientVersion);
        Assert.Equal("Versión de cliente desconocida · Sin cuenta", card.DetailText);
        Assert.Equal("No es un ejecutable de Windows", client.Version);
        Screenshots.Settle();
        Screenshots.Save(window, 1.0, "unknown-version-clients-es-1.0x.png");
        window.Close();
    }
}
