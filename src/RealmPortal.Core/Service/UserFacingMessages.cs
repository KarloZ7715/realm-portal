using System.Text.RegularExpressions;

namespace RealmPortal.Core.Service;

public static class UserFacingMessages
{
    private static readonly Regex AtomicWriteTempPath = new(
        @"\.(?<file>(?:activo|servers|state|clients|last-switch)\.json)\.[0-9a-f]{32}\.tmp",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        var text = AtomicWriteTempPath.Replace(message, static m => m.Groups["file"].Value);
        return StripParameterSuffix(text);
    }

    public static string StripParameterSuffix(string message)
    {
        const string marker = " (Parameter '";
        var index = message.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return message;
        var end = message.IndexOf("')", index + marker.Length, StringComparison.Ordinal);
        return end < 0 ? message : message[..index];
    }
}
