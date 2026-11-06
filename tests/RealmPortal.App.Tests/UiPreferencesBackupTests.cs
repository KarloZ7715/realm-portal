using RealmPortal.App.Portal;
using RealmPortal.Core.IO;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>Issue #196: ui-preferences.json keeps a .bak of its previous valid version.</summary>
public sealed class UiPreferencesBackupTests
{
    [Fact]
    public void Save_KeepsThePreviousValidVersion_AndADamagedFileNeverReplacesIt()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        var path = Path.Combine(temp.Path, "ui-preferences.json");
        store.Save(new UiPreferences("en"));
        Assert.Equal(ConfigFileState.Missing, store.BackupState);

        store.Save(new UiPreferences("es"));
        Assert.Equal(ConfigFileState.Valid, store.BackupState);
        Assert.Equal("en", BackupLanguage(path));

        // #141: a damaged file loads as defaults; saving over it must not replace the good .bak.
        File.WriteAllText(path, "{ damaged");
        store.Save(store.Load() with { LanguageCode = "en" });
        Assert.Equal("en", BackupLanguage(path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path + ".bak"));
    }

    [Fact]
    public void RestoreBackup_RecoversADamagedFile()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        var path = Path.Combine(temp.Path, "ui-preferences.json");
        store.Save(new UiPreferences("es"));
        store.Save(new UiPreferences("en"));
        File.WriteAllText(path, "{ damaged");

        store.RestoreBackup();

        Assert.Equal("es", store.Load().LanguageCode);
        Assert.Equal("{ damaged", File.ReadAllText(path + ".before-restore"));
    }

    [Fact]
    public void UnchangedSave_WritesNothing_AndKeepsTheRealPreviousBak()
    {
        using var temp = new TempDirectory();
        var writer = new CountingWriter();
        var store = new UiPreferencesStore(temp.Path, writer);
        var path = Path.Combine(temp.Path, "ui-preferences.json");
        store.Save(new UiPreferences("en"));
        store.Save(new UiPreferences("es"));
        var writes = writer.Count;

        // The window placement is saved on every close, usually with the same values.
        store.Save(new UiPreferences("es"));

        Assert.Equal(writes, writer.Count);
        Assert.Equal("en", BackupLanguage(path));
    }

    [Fact]
    public void RestoreBackup_OfADamagedBak_ThrowsStoreException_AndChangesNothing()
    {
        using var temp = new TempDirectory();
        var store = new UiPreferencesStore(temp.Path);
        var path = Path.Combine(temp.Path, "ui-preferences.json");
        store.Save(new UiPreferences("es"));
        File.WriteAllText(path + ".bak", "{ damaged");

        Assert.Throws<StoreException>(store.RestoreBackup);

        Assert.Equal("es", store.Load().LanguageCode);
        Assert.False(File.Exists(path + ".before-restore"));
    }

    private sealed class CountingWriter : IAtomicFileWriter
    {
        private readonly AtomicFileWriter _inner = new();

        public int Count { get; private set; }

        public void WriteAllBytes(string path, byte[] content) => WriteAllBytes(path, content, null);

        public void WriteAllBytes(string path, byte[] content, FileReplacementPermissions? permissions)
        {
            Count++;
            _inner.WriteAllBytes(path, content, permissions);
        }
    }

    /// <summary>Loads <c>&lt;path&gt;.bak</c> through a store in a scratch folder and returns its language.</summary>
    private static string? BackupLanguage(string path)
    {
        using var scratch = new TempDirectory();
        File.Copy(path + ".bak", Path.Combine(scratch.Path, "ui-preferences.json"));
        return new UiPreferencesStore(scratch.Path).Load().LanguageCode;
    }
}
