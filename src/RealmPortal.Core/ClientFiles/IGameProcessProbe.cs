namespace RealmPortal.Core.ClientFiles;

/// <summary>Tells whether the game is running, so client files are never changed (or restored) underneath it.</summary>
public interface IGameProcessProbe
{
    /// <summary>True when a game process that could be using <paramref name="clientRoot"/> is running.</summary>
    bool IsGameRunning(string clientRoot);
}
