using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>The realmlist contract of the consuming launcher: ASCII only, starts with "set realmlist ".</summary>
public class ServerRealmlistContractTests
{
    [Property(MaxTest = 200)]
    public Property GeneratedServers_HaveAsciiRealmlistStartingWithSetRealmlist() =>
        Prop.ForAll(ServerGen.Server().ToArbitrary(), server =>
            server.Realmlist.StartsWith("set realmlist ", StringComparison.Ordinal)
            && server.Realmlist.StartsWith("set realmlist " + server.Host + "\r\n", StringComparison.Ordinal)
            && server.Realmlist.All(c => c is '\r' or '\n' || (c >= ' ' && c <= '~'))
            && RealmlistText.Validate(server.Realmlist) is null);

    [Property(MaxTest = 300)]
    public Property AnyServerCreateAccepts_SatisfiesTheContract() =>
        Prop.ForAll(
            Gen.Elements("h", "auth.gamefreedom.org", "ñ", "龍.org", "a b", "a\tb", "a\r\nb", "ok:3724", "😀", "")
                .ToArbitrary(),
            Gen.Elements("set patchlist x", "ñ", "龍", "a\nb", "", "  ", "~ok~", "tab\t")
                .ArrayOf().ToArbitrary(),
            (host, extra) =>
            {
                Server server;
                try
                {
                    server = Server.Create("n", host, extraRealmlistLines: extra);
                }
                catch (ArgumentException)
                {
                    return true;
                }

                return RealmlistText.Validate(server.Realmlist) is null
                       && server.Realmlist.StartsWith("set realmlist ", StringComparison.Ordinal);
            });
}
