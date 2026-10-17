using FsCheck;
using FsCheck.Fluent;
using RealmPortal.Core.Servers;

namespace RealmPortal.Tests.Support;

/// <summary>FsCheck generators for valid servers (unicode names and lines included).</summary>
public static class ServerGen
{
    private static Gen<string> Text(int min, int max) =>
        Gen.Elements("a", "Z", "7", "é", "ñ", "龍", "-", "_", ".", "'", "\"", "\\", "&", "<", ">", "+", "😀", " ")
            .ArrayOf()
            .Where(chars => chars.Length >= min && chars.Length <= max)
            .Select(chars => string.Concat(chars));

    private static readonly Gen<string> NameGen = Text(1, 20).Where(s => s.Trim().Length > 0);

    private static readonly Gen<string> HostGen =
        Gen.Elements("logon.example.org", "10.0.0.5:3724", "localhost", "wow.example.net:8085", "ünï.example.org");

    private static readonly Gen<string[]> ExtraLinesGen =
        Gen.Elements("set patchlist patch.example.org", "set realmname \"My & Realm\"", "-- ñ 龍", "set x y")
            .ArrayOf();

    private static readonly Gen<int?> BuildGen =
        Gen.OneOf(Gen.Constant<int?>(null), Gen.Elements<int?>(5875, 8606, 12340, 15595));

    private static readonly Gen<string?> AccountGen =
        Gen.OneOf(Gen.Constant<string?>(null), Gen.Elements<string?>("carlos", "Ünï", "a b"));

    public static Gen<Server> Server() =>
        from name in NameGen
        from host in HostGen
        from realm in Text(0, 12).Where(s => !s.Any(char.IsControl))
        from extra in ExtraLinesGen
        from build in BuildGen
        from account in AccountGen
        select Core.Servers.Server.Create(name, host, realm, extra, build, account);

    /// <summary>A list of servers with distinct (case-insensitive) names.</summary>
    public static Gen<IReadOnlyList<Server>> DistinctServers() =>
        Server().ListOf().Select(list =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (IReadOnlyList<Server>)list.Where(s => seen.Add(s.Name)).ToList();
        });
}
