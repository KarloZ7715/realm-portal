using RealmPortal.Core.Launch;

namespace RealmPortal.Cli;

internal sealed class GameLauncherAdapter(IGameLauncher inner) : IGameLaunchService
{
    public void Launch(GameLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        // The legacy launcher only accepts one wrapper. Other fields would otherwise be ignored.
        var unsupported = UnsupportedSetting(spec);
        if (unsupported is not null)
            throw new ArgumentException($"Unsupported launcher setting '{unsupported}'.", GameLaunchSpec.UnsupportedSettingParameter);

        var wrapper = spec.Prefix is { Count: > 0 } prefix ? prefix[0] : null;
        inner.Launch(spec.ExecutablePath, spec.WorkingDirectory, wrapper);
    }

    private static string? UnsupportedSetting(GameLaunchSpec spec)
    {
        if (!string.IsNullOrWhiteSpace(spec.CommandTemplate))
            return nameof(GameLaunchSpec.CommandTemplate);
        if (spec.Arguments is { Count: > 0 })
            return nameof(GameLaunchSpec.Arguments);
        if (spec.Environment is { Count: > 0 })
            return nameof(GameLaunchSpec.Environment);
        if (spec.Prefix is { Count: > 1 })
            return nameof(GameLaunchSpec.Prefix);
        return null;
    }
}
