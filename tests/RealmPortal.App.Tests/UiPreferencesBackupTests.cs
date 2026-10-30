using RealmPortal.App.Portal;
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

    /// <summary>Loads <c>&lt;path&gt;.bak</c> through a store in a scratch folder and returns its language.</summary>
    private static string? BackupLanguage(string path)
    {
        using var scratch = new TempDirectory();
        File.Copy(path + ".bak", Path.Combine(scratch.Path, "ui-preferences.json"));
        return new UiPreferencesStore(scratch.Path).Load().LanguageCode;
    }
}
