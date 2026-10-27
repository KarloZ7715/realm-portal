namespace RealmPortal.Tests.Support;

/// <summary>xUnit 2 has no Assert.Skip; these Fact/Theory subclasses set Skip so OS- or privilege-specific tests report as skipped instead of silently returning as passed.</summary>
public sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test.";
    }
}

public sealed class LinuxTheoryAttribute : TheoryAttribute
{
    public LinuxTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test.";
    }
}

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
    }
}

public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
    }
}

/// <summary>Linux or macOS (POSIX symlink / Unix file-mode tests).</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "Unix-only test (Linux or macOS).";
    }
}

public sealed class UnixTheoryAttribute : TheoryAttribute
{
    public UnixTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "Unix-only test (Linux or macOS).";
    }
}

/// <summary>Any non-Windows OS (folder names with control characters, Unix permissions, etc.).</summary>
public sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Skipped on Windows.";
    }
}

public sealed class NonWindowsTheoryAttribute : TheoryAttribute
{
    public NonWindowsTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Skipped on Windows.";
    }
}

/// <summary>Linux and not a privileged process (directory mode bits are enforced).</summary>
public sealed class NonPrivilegedLinuxFactAttribute : FactAttribute
{
    public NonPrivilegedLinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test.";
        else if (Environment.IsPrivilegedProcess)
            Skip = "Skipped when running as a privileged process (directory mode bits are ignored).";
    }
}

public sealed class NonPrivilegedLinuxTheoryAttribute : TheoryAttribute
{
    public NonPrivilegedLinuxTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test.";
        else if (Environment.IsPrivilegedProcess)
            Skip = "Skipped when running as a privileged process (directory mode bits are ignored).";
    }
}

/// <summary>Non-Windows and not privileged (Unix file modes must be enforceable).</summary>
public sealed class NonPrivilegedUnixFactAttribute : FactAttribute
{
    public NonPrivilegedUnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Skipped on Windows.";
        else if (Environment.IsPrivilegedProcess)
            Skip = "Skipped when running as a privileged process (directory mode bits are ignored).";
    }
}

public sealed class NonPrivilegedUnixTheoryAttribute : TheoryAttribute
{
    public NonPrivilegedUnixTheoryAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Skipped on Windows.";
        else if (Environment.IsPrivilegedProcess)
            Skip = "Skipped when running as a privileged process (directory mode bits are ignored).";
    }
}

/// <summary>Linux and not root (EACCES / unreadable-stat behavior).</summary>
public sealed class NonRootLinuxFactAttribute : FactAttribute
{
    public NonRootLinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test.";
        else if (Environment.IsPrivilegedProcess || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            Skip = "Skipped when running as root (cannot observe EACCES on owned files).";
    }
}

public sealed class NonRootLinuxTheoryAttribute : TheoryAttribute
{
    public NonRootLinuxTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only test.";
        else if (Environment.IsPrivilegedProcess || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            Skip = "Skipped when running as root (cannot observe EACCES on owned files).";
    }
}

/// <summary>Unix (Linux or macOS) and not root/privileged (unreadable-file defaults).</summary>
public sealed class NonRootUnixFactAttribute : FactAttribute
{
    public NonRootUnixFactAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "Unix-only test (Linux or macOS).";
        else if (Environment.IsPrivilegedProcess || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            Skip = "Skipped when running as root (cannot observe EACCES on owned files).";
    }
}

public sealed class NonRootUnixTheoryAttribute : TheoryAttribute
{
    public NonRootUnixTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            Skip = "Unix-only test (Linux or macOS).";
        else if (Environment.IsPrivilegedProcess || string.Equals(Environment.UserName, "root", StringComparison.Ordinal))
            Skip = "Skipped when running as root (cannot observe EACCES on owned files).";
    }
}
