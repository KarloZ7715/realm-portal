using RealmPortal.App.Portal;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests.Support;

/// <summary>
/// Shared factory for App tests that need a real <see cref="PortalSession"/>.
/// Pins first-scan state and scan roots so tests never race a first-run scan or walk the
/// developer's home / Steam / Wine folders.
/// </summary>
public static class TestSessions
{
    /// <summary>
    /// Scan options that never inspect the real home or system game folders.
    /// <paramref name="homeDirectory"/> is still set so a regression that re-enables default
    /// roots only sees the synthetic home.
    /// </summary>
    public static ClientScanOptions IsolatedScan(string homeDirectory, IReadOnlyList<string>? additionalRoots = null) =>
        new()
        {
            IncludeDefaultRoots = false,
            IncludeSystemRoots = false,
            HomeDirectory = homeDirectory,
            AdditionalRoots = additionalRoots ?? [],
        };

    /// <summary>
    /// Builds an isolated session on the given paths. By default seeds
    /// <see cref="UiPreferences.FirstScanCompleted"/> so <see cref="MainViewModel"/> does not start a background scan.
    /// </summary>
    public static PortalSession Create(
        string configDir,
        string projectionDir,
        string backupDir,
        string homeDirectory,
        IGameLaunchService? launch = null,
        IGameProcessProbe? probe = null,
        bool firstScanCompleted = true,
        IReadOnlyList<string>? clientsInUse = null,
        string? languageCode = "en",
        IReadOnlyList<string>? additionalScanRoots = null,
        bool seedPreferences = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);

        AssertNotUserBackupPath(backupDir);

        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(projectionDir);
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(homeDirectory);

        var session = PortalSession.CreateForPaths(
            configDir,
            projectionDir,
            backupDir,
            launch,
            probe ?? new NeverRunningGameProbe(),
            IsolatedScan(homeDirectory, additionalScanRoots));

        if (seedPreferences)
        {
            session.UiPreferences.Save(new UiPreferences(
                languageCode,
                clientsInUse?.ToList() ?? [],
                FirstScanCompleted: firstScanCompleted));
        }

        return session;
    }

    /// <summary>Fails when a test would resolve or write the real user backup folder.</summary>
    public static void AssertNotUserBackupPath(string path)
    {
        var full = Path.GetFullPath(path);
        var userDefault = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "realm-portal",
            "backups"));
        if (string.Equals(full, userDefault, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(userDefault + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"App tests must not use the default user backup folder ('{userDefault}'). Use a temp folder.");
        }
    }
}

/// <summary>
/// Disposable temp dirs + isolated <see cref="PortalSession"/> for App/Settings tests.
/// </summary>
public sealed class IsolatedAppSession : IDisposable
{
    private readonly RealmPortal.Tests.Support.TempDirectory _temp = new();

    public IsolatedAppSession(
        IGameLaunchService? launch = null,
        IGameProcessProbe? probe = null,
        bool firstScanCompleted = true,
        IReadOnlyList<string>? clientsInUse = null,
        string? languageCode = "en",
        IReadOnlyList<string>? additionalScanRoots = null,
        bool seedPreferences = true)
    {
        Config = _temp.Combine("config");
        Projection = _temp.Combine("projection");
        Backups = _temp.Combine("backups");
        Home = _temp.Combine("home");
        Session = TestSessions.Create(
            Config,
            Projection,
            Backups,
            Home,
            launch,
            probe,
            firstScanCompleted,
            clientsInUse,
            languageCode,
            additionalScanRoots,
            seedPreferences);
    }

    public RealmPortal.Tests.Support.TempDirectory Temp => _temp;

    public string Config { get; }

    public string Projection { get; }

    public string Backups { get; }

    /// <summary>Empty synthetic home used as <see cref="ClientScanOptions.HomeDirectory"/>.</summary>
    public string Home { get; }

    public PortalSession Session { get; }

    public string CreateClientFolder(string name, Version? version = null)
    {
        var folder = _temp.Combine(name);
        Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
        File.WriteAllBytes(
            Path.Combine(folder, "Wow.exe"),
            SyntheticPe.WithVersion(version ?? new Version(3, 3, 5, 12340)));
        return folder;
    }

    public void Dispose() => _temp.Dispose();
}
