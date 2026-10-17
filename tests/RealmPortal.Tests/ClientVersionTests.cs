using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Clients;

namespace RealmPortal.Tests;

public class ClientVersionTests
{
    [Theory]
    [InlineData("3.3.5", 3, 3, 5, null)]
    [InlineData("3.3.5a", 3, 3, 5, null)]
    [InlineData(" 3.3.5.12340 ", 3, 3, 5, 12340)]
    [InlineData("3.3.5a.12340", 3, 3, 5, 12340)]
    [InlineData("1.12.1", 1, 12, 1, null)]
    public void TryParse_AcceptsValidForms(string text, int major, int minor, int patch, int? build)
    {
        Assert.True(ClientVersion.TryParse(text, out var version));
        Assert.Equal(new ClientVersion(major, minor, patch, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("3.3")]
    [InlineData("3.3.5.")]
    [InlineData("3.3.5ab")]
    [InlineData("a.b.c")]
    [InlineData("3.3.-5")]
    [InlineData("3.3.5.1.2")]
    public void TryParse_RejectsInvalidForms(string? text) => Assert.False(ClientVersion.TryParse(text, out _));

    [Fact]
    public void Parse_ThrowsFormatException_OnInvalidText() =>
        Assert.Throws<FormatException>(() => ClientVersion.Parse("nope"));

    [Theory]
    [InlineData("3.3.5", "3.3.5", true)]
    [InlineData("3.3.5.12340", "3.3.5", true)]
    [InlineData("3.3.5", "3.3.5.12340", true)]
    [InlineData("3.3.5.12340", "3.3.5.12340", true)]
    [InlineData("3.3.5.12213", "3.3.5.12340", false)]
    [InlineData("3.3.4", "3.3.5", false)]
    [InlineData("4.3.5", "3.3.5", false)]
    [InlineData("3.2.5", "3.3.5", false)]
    public void Satisfies_ComparesPartsAndBuildWhenBothKnown(string client, string required, bool expected) =>
        Assert.Equal(expected, ClientVersion.Parse(client).Satisfies(ClientVersion.Parse(required)));

    private static Arbitrary<ClientVersion> Versions() =>
        Gen.Choose(0, 9999).Three()
            .SelectMany(t => Gen.OneOf(Gen.Constant<int?>(null), Gen.Choose(0, 999_999_999).Select(b => (int?)b)),
                (t, build) => new ClientVersion(t.Item1, t.Item2, t.Item3, build))
            .ToArbitrary();

    [Property]
    public Property ToString_ThenParse_RoundTrips() =>
        Prop.ForAll(Versions(), v => ClientVersion.TryParse(v.ToString(), out var parsed) && parsed == v);

    [Property]
    public Property Satisfies_IsReflexive() => Prop.ForAll(Versions(), v => v.Satisfies(v));
}
