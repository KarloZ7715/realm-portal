using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Clients;

namespace RealmPortal.Tests;

public class ClientFilesContractTests
{
    private static readonly ClientVersion V335 = new(new Version(3, 3, 5, 12340));

    [Theory]
    [InlineData("install-1", true)]
    [InlineData("A.b_c-9", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("a b", false)]
    [InlineData("é", false)]
    public void IsValidId_AcceptsOnlySafeFolderNames(string id, bool expected) =>
        Assert.Equal(expected, ClientFilesRequest.IsValidId(id));

    [Fact]
    public void ValidateIds_RejectsUnsafeIds()
    {
        Assert.Throws<ArgumentException>(() => new ClientFilesRequest("../x", "p", "/c", V335, "set realmlist h\r\n", "").ValidateIds());
        Assert.Throws<ArgumentException>(() => new ClientFilesRequest("i", "bad id", "/c", V335, "set realmlist h\r\n", "").ValidateIds());
        new ClientFilesRequest("i", "p", "/c", V335, "set realmlist h\r\n", "").ValidateIds();
    }

    [Fact]
    public void Plan_IsEmptyWithoutEdits()
    {
        var empty = new ClientFilesPlan("i", "p", "/c", V335, "set realmlist h\r\n", "", []);
        var edit = new ClientFileEdit("realmlist.wtf", ClientEditKind.Create, [1], null, "ab");

        Assert.True(empty.IsEmpty);
        Assert.False(empty with { Edits = [edit] } is { IsEmpty: true });
    }

    [Fact]
    public void UndoResult_ReportsConflicts()
    {
        var ok = new UndoResult("b", [new UndoFileResult("a", UndoFileStatus.Restored)]);
        var bad = new UndoResult("b", [new UndoFileResult("a", UndoFileStatus.Restored), new UndoFileResult("c", UndoFileStatus.Conflict)]);

        Assert.False(ok.HasConflicts);
        Assert.True(bad.HasConflicts);
    }

    [Fact]
    public void Exception_ExposesCodeAndPath()
    {
        var ex = new ClientFilesException(ClientFilesErrorCode.HashMismatch, "WTF/Config.wtf", "changed");

        Assert.Equal(ClientFilesErrorCode.HashMismatch, ex.Code);
        Assert.Equal("WTF/Config.wtf", ex.Path);
    }
}
