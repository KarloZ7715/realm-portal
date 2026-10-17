using RealmPortal.Core.Launch;

namespace RealmPortal.Cli;

internal sealed class GameLauncherAdapter(IGameLauncher inner) : IGameLaunchService
{
    public void Launch(GameLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var wrapper = spec.Prefix is { Count: > 0 } prefix ? prefix[0] : null;
        inner.Launch(spec.ExecutablePath, spec.WorkingDirectory, wrapper);
    }
}
