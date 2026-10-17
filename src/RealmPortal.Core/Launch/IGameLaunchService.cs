namespace RealmPortal.Core.Launch;

/// <summary>Starts a game client from a full <see cref="GameLaunchSpec"/> (template, arguments, environment, prefix).</summary>
public interface IGameLaunchService
{
    /// <summary>Starts the game and returns immediately.</summary>
    /// <exception cref="ArgumentException">The spec cannot be resolved (see <see cref="GameLaunchSpec.Resolve"/>).</exception>
    void Launch(GameLaunchSpec spec);
}
