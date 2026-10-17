namespace RealmPortal.Core.Clients;

/// <summary>Reads the version of a game client installed in a folder.</summary>
public interface IClientVersionReader
{
    /// <summary>
    /// Reads the version of the client whose executable (<c>Wow.exe</c>, matched case-insensitively)
    /// lives directly in <paramref name="clientDirectory"/>. The executable is only read, never run.
    /// </summary>
    /// <exception cref="ClientVersionException">
    /// The version cannot be determined; <see cref="ClientVersionException.Kind"/> tells why.
    /// </exception>
    ClientVersion Read(string clientDirectory);
}
