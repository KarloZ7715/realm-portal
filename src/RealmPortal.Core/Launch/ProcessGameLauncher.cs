namespace RealmPortal.Core.Launch;

/// <summary>
/// Launches the client as a detached child process. Implements the simple <see cref="IGameLauncher"/> (executable + optional wrapper)
/// on top of the full <see cref="IGameLaunchService"/>; both go through an <see cref="IProcessRunner"/>.
/// </summary>
public sealed class ProcessGameLauncher(IProcessRunner runner) : IGameLauncher, IGameLaunchService
{
    public ProcessGameLauncher()
        : this(new SystemProcessRunner())
    {
    }

    public void Launch(string executablePath, string workingDirectory, string? wrapper) =>
        Launch(new GameLaunchSpec(
            executablePath,
            workingDirectory,
            Prefix: string.IsNullOrWhiteSpace(wrapper) ? null : [wrapper]));

    public void Launch(GameLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        runner.Start(spec.Resolve());
    }
}
