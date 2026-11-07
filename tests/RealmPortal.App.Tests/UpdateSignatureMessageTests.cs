using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Network;
using RealmPortal.Core.Updates;

namespace RealmPortal.App.Tests;

/// <summary>Install failures caused by the release signature (#116) get their own ES/EN text.</summary>
public class UpdateSignatureMessageTests
{
    [Theory]
    [InlineData(UpdateErrorCode.SignatureMissing, "update.install.unsigned.body")]
    [InlineData(UpdateErrorCode.SignatureInvalid, "update.install.badsignature.body")]
    [InlineData(UpdateErrorCode.ChecksumMismatch, "update.install.failed.body")]
    [InlineData(UpdateErrorCode.UpdateCheckFailed, "update.install.failed.body")]
    public void FailureBodyKey_FollowsTheErrorCode(UpdateErrorCode code, string key)
    {
        Assert.Equal(key, UpdateInstallPresentation.FailureBodyKey(new UpdateException(code, "x")));
        Assert.Equal(key, UpdateInstallPresentation.FailureBodyKey(new InvalidOperationException("wrapped", new UpdateException(code, "x"))));
    }

    [Fact]
    public void FailureBodyKey_FallsBackForOtherErrors()
    {
        Assert.Equal("update.install.failed.body", UpdateInstallPresentation.FailureBodyKey(new IOException()));
        Assert.Equal("update.install.failed.body", UpdateInstallPresentation.FailureBodyKey(null));
    }

    [AvaloniaTheory]
    [InlineData(Language.En)]
    [InlineData(Language.Es)]
    public void SignatureMessages_ExistAndDiffer(Language language)
    {
        var strings = Loc.Load(language);
        var unsigned = strings["update.install.unsigned.body"];
        var invalid = strings["update.install.badsignature.body"];
        Assert.False(string.IsNullOrWhiteSpace(unsigned));
        Assert.False(string.IsNullOrWhiteSpace(invalid));
        Assert.NotEqual(unsigned, invalid);
        Assert.NotEqual(strings["update.install.failed.body"], unsigned);
    }
}
