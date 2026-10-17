using RealmPortal.Core.Clients;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class PeClientVersionReaderTests
{
    private readonly PeClientVersionReader _reader = new();

    [Theory]
    [InlineData("3.3.5.12340", 12340)]
    [InlineData("2.4.3.8606", 8606)]
    [InlineData("1.12.1.5875", 5875)]
    public void Read_ReturnsVersionFromResource(string version, int build)
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.Combine("Wow.exe"), SyntheticPe.WithVersion(Version.Parse(version)));

        var result = _reader.Read(dir.Path);

        Assert.Equal(Version.Parse(version), result.FileVersion);
        Assert.Equal(build, result.Build);
        Assert.Equal($"{build} ({version})", result.ToString());
    }

    [Fact]
    public void Read_FindsExecutableCaseInsensitively()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.Combine("WOW.EXE"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));

        Assert.Equal(12340, _reader.Read(dir.Path).Build);
    }

    [Fact]
    public void Read_DoesNotModifyTheExecutable()
    {
        using var dir = new TempDirectory();
        var bytes = SyntheticPe.WithVersion(new Version(3, 3, 5, 12340));
        File.WriteAllBytes(dir.Combine("Wow.exe"), bytes);

        _reader.Read(dir.Path);

        Assert.Equal(bytes, File.ReadAllBytes(dir.Combine("Wow.exe")));
    }

    [Fact]
    public void Read_PeWithoutVersionResource_ThrowsNoVersionResource()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.Combine("Wow.exe"), SyntheticPe.WithoutVersionResource());

        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(dir.Path));

        Assert.Equal(ClientVersionErrorKind.NoVersionResource, ex.Kind);
        Assert.EndsWith("Wow.exe", ex.Path);
    }

    [Fact]
    public void Read_ZeroVersion_ThrowsNoVersionResource()
    {
        using var dir = new TempDirectory();
        File.WriteAllBytes(dir.Combine("Wow.exe"), SyntheticPe.WithVersion(new Version(0, 0, 0, 0)));

        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(dir.Path));

        Assert.Equal(ClientVersionErrorKind.NoVersionResource, ex.Kind);
    }

    [Theory]
    [InlineData("hello\n")]
    [InlineData("")]
    public void Read_NotAPeFile_ThrowsNotAPortableExecutable(string content)
    {
        using var dir = new TempDirectory();
        File.WriteAllText(dir.Combine("Wow.exe"), content);

        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(dir.Path));

        Assert.Equal(ClientVersionErrorKind.NotAPortableExecutable, ex.Kind);
    }

    [Fact]
    public void Read_TruncatedPe_ThrowsTypedError()
    {
        using var dir = new TempDirectory();
        var bytes = SyntheticPe.WithVersion(new Version(3, 3, 5, 12340));
        File.WriteAllBytes(dir.Combine("Wow.exe"), bytes.AsSpan(0, 0x100).ToArray());

        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(dir.Path));

        Assert.True(ex.Kind is ClientVersionErrorKind.NotAPortableExecutable or ClientVersionErrorKind.NoVersionResource);
    }

    [Fact]
    public void Read_MissingExecutable_ThrowsExecutableNotFound()
    {
        using var dir = new TempDirectory();

        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(dir.Path));

        Assert.Equal(ClientVersionErrorKind.ExecutableNotFound, ex.Kind);
    }

    [Fact]
    public void Read_MissingDirectory_ThrowsExecutableNotFound()
    {
        using var dir = new TempDirectory();

        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(dir.Combine("does-not-exist")));

        Assert.Equal(ClientVersionErrorKind.ExecutableNotFound, ex.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_EmptyPath_ThrowsExecutableNotFound(string path)
    {
        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read(path));

        Assert.Equal(ClientVersionErrorKind.ExecutableNotFound, ex.Kind);
    }

    [Fact]
    public void Read_PathWithNulCharacter_ThrowsTypedError()
    {
        var ex = Assert.Throws<ClientVersionException>(() => _reader.Read("bad\0path"));

        Assert.True(ex.Kind is ClientVersionErrorKind.Unreadable or ClientVersionErrorKind.ExecutableNotFound);
    }

    /// <summary>
    /// Optional: set REALM_PORTAL_FIXTURES to a folder with sub-folders such as wow-12340/Wow.exe to run the
    /// reader against real (or externally generated) clients. Returns immediately when the variable is unset; fails if it is set but a folder is missing.
    /// The files are only read; nothing is copied into the repository.
    /// </summary>
    [Theory]
    [InlineData("wow-12340", 12340)]
    [InlineData("wow-8606", 8606)]
    [InlineData("wow-5875", 5875)]
    public void Read_OptionalExternalFixtures(string folder, int build)
    {
        var root = Environment.GetEnvironmentVariable("REALM_PORTAL_FIXTURES");
        if (string.IsNullOrEmpty(root))
            return;

        // The variable is set: a missing folder is a configuration error, not a silent pass.
        var dir = Path.Combine(root, folder);
        Assert.True(Directory.Exists(dir), $"REALM_PORTAL_FIXTURES is set but '{dir}' does not exist.");

        Assert.Equal(build, _reader.Read(dir).Build);
    }
}
