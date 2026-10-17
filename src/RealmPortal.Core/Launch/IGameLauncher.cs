namespace RealmPortal.Core.Launch;

/// <summary>Starts the game client. Faked in tests so that no real client is ever launched.</summary>
public interface IGameLauncher
{
    /// <summary>
    /// Starts <paramref name="executablePath"/> with <paramref name="workingDirectory"/> as its working directory and
    /// returns immediately (it does not wait for the game to exit).
    /// </summary>
    /// <param name="wrapper">Optional command that runs the executable (for example <c>wine</c>); <c>null</c> runs it directly.</param>
    void Launch(string executablePath, string workingDirectory, string? wrapper);
}
