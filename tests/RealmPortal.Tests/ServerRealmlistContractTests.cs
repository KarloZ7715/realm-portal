using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

/// <summary>The realmlist contract of the consuming launcher, seen from <see cref="Server"/>.</summary>
public class ServerRealmlistContractTests
{
    [Property(MaxTest = 200)]
    public Property GeneratedServers_HaveAValidRealmlistStartingWithSetRealmlist() =>
        Prop.ForAll(ServerGen.Server().ToArbitrary(), server =>
        {
            RealmlistText.Validate(server.Realmlist);
            return server.Realmlist.StartsWith("set realmlist " + server.Host + "\r\n", StringComparison.Ordinal)
                   && server.Realmlist.All(c => c is '\r' or '\n' || (c >= ' ' && c <= '~'));
        });

    [Property(MaxTest = 500)]
    public Property AnyServerCreateAccepts_SatisfiesTheContract() =>
        Prop.ForAll(
            Gen.Elements("h", "auth.gamefreedom.org", "ñ", "龍.org", "a b", "a\tb", "a\"b", "a\r\nb", "ok:3724", "😀", "")
                .ToArbitrary(),
            Gen.Elements("set patchlist x", "set portal x", "set realmname y", "ñ", "a\nb", "", "  ", "tab\t", "set patchlist 'q'")
                .ArrayOf().ToArbitrary(),
            (host, extra) =>
            {
                Server server;
                try
                {
                    server = Server.Create("n", host, extraRealmlistLines: extra);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidRealmlistException)
                {
                    return true;
                }

                RealmlistText.Validate(server.Realmlist);
                return server.Realmlist.StartsWith("set realmlist ", StringComparison.Ordinal);
            });
}
