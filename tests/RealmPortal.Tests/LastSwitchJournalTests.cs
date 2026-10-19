using RealmPortal.Core.IO;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class LastSwitchJournalTests
{
    private static LastSwitchJournal Open(TempDirectory dir) => new(dir.Path, new AtomicFileWriter());

    [Fact]
    public void Save_MigratesLegacyEntry_SoItsClientJournalSurvivesANewerWrite()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(System.IO.Path.Combine(dir.Path, LastSwitchJournal.FileName), """
        {
          "version": 1,
          "client_id": "legacy",
          "backup_id": null,
          "files_undone": false,
          "manifest_sha256": null
        }
        """);
        var journal = Open(dir);

        journal.Save(new LastSwitchEntry("newer", null));

        // The legacy client's journal survives the write for a different client.
        Assert.Equal("legacy", journal.Load("legacy")!.ClientId);
        Assert.Equal("newer", journal.Load("newer")!.ClientId);
        Assert.Equal("newer", journal.Load()!.ClientId);
    }

    [Fact]
    public void Clear_GlobalEntry_KeepsPerClientJournals()
    {
        using var dir = new TempDirectory();
        var journal = Open(dir);
        journal.Save(new LastSwitchEntry("clienta", null));
        journal.Save(new LastSwitchEntry("clientb", null));

        journal.Clear();

        // Only the global/legacy view is cleared; each client keeps its own journal.
        Assert.Null(journal.Load()!.ClientId);
        Assert.Equal("clienta", journal.Load("clienta")!.ClientId);
        Assert.Equal("clientb", journal.Load("clientb")!.ClientId);
    }

    [Fact]
    public void Clear_LegacyOwner_PromotesSurvivingClientAndDropsOnlyThatClient()
    {
        using var dir = new TempDirectory();
        var journal = Open(dir);
        journal.Save(new LastSwitchEntry("clienta", null));
        journal.Save(new LastSwitchEntry("clientb", null));

        journal.Clear("clientb");

        Assert.Equal("clienta", journal.Load()!.ClientId);
        Assert.Null(journal.Load("clientb"));
        Assert.Equal("clienta", journal.Load("clienta")!.ClientId);
    }
}
