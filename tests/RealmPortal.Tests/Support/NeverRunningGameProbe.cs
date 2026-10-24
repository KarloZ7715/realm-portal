using RealmPortal.Core.ClientFiles;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Reports no game running, so service and CLI tests don't depend on the processes of the machine they run on
/// (a real <see cref="GameProcessProbe"/> scans every process, including those of parallel test runs).
/// </summary>
public sealed class NeverRunningGameProbe : IGameProcessProbe
{
    public bool IsGameRunning(string clientRoot) => false;
}
