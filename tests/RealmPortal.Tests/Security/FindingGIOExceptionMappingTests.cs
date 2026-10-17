using RealmPortal.Core.Service;

namespace RealmPortal.Tests.Security;

public class FindingGIOExceptionMappingTests
{
    [Fact]
    public void FromException_DistinguishesDiskFullSharingAndReadOnly()
    {
        Assert.Equal(ServiceErrorCode.DiskFull, ServiceErrors.FromException(new HResultIOException(unchecked((int)0x80070070))).Code);
        Assert.Equal(ServiceErrorCode.FileSharingViolation, ServiceErrors.FromException(new HResultIOException(unchecked((int)0x80070020))).Code);
        Assert.Equal(ServiceErrorCode.ReadOnlyFs, ServiceErrors.FromException(new HResultIOException(30)).Code);
        Assert.Equal(ServiceErrorCode.PermissionDenied, ServiceErrors.FromException(new HResultIOException(5)).Code);
    }

    private sealed class HResultIOException : IOException
    {
        public HResultIOException(int hr) : base("io failure") => HResult = hr;
    }
}
