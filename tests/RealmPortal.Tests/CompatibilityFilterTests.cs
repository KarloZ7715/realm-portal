using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Clients;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class CompatibilityFilterTests
{
    private static ClientVersion Client(int build) => new(new Version(3, 3, 5, build));

    private static readonly Server Any = Server.Create("Any", "a");
    private static readonly Server Wotlk = Server.Create("Wotlk", "w", requiredClientBuild: 12340);
    private static readonly Server Tbc = Server.Create("Tbc", "t", requiredClientBuild: 8606);

    [Fact]
    public void ServerWithoutRequirement_AcceptsAnyClient_EvenUnknown()
    {
        Assert.True(CompatibilityFilter.IsCompatible(Any, null));
        Assert.True(CompatibilityFilter.IsCompatible(Any, Client(12340)));
    }

    [Fact]
    public void ServerWithRequirement_NeedsMatchingBuild()
    {
        Assert.True(CompatibilityFilter.IsCompatible(Wotlk, Client(12340)));
        Assert.False(CompatibilityFilter.IsCompatible(Wotlk, Client(8606)));
        Assert.False(CompatibilityFilter.IsCompatible(Wotlk, null));
    }

    [Fact]
    public void Filter_KeepsOrder_AndDropsIncompatible()
    {
        var result = CompatibilityFilter.Filter([Tbc, Any, Wotlk], Client(12340));

        Assert.Equal([Any, Wotlk], result);
    }

    [Fact]
    public void Filter_WithUnknownClient_ReturnsOnlyServersWithoutRequirement() =>
        Assert.Equal([Any], CompatibilityFilter.Filter([Tbc, Any, Wotlk], null));

    [Property]
    public Property Filter_ReturnsExactlyTheCompatibleSubset() =>
        Prop.ForAll(
            ServerGen.DistinctServers().ToArbitrary(),
            Gen.OneOf(Gen.Constant<int?>(null), Gen.Elements<int?>(5875, 8606, 12340, 15595)).ToArbitrary(),
            (servers, build) =>
            {
                var client = build is { } b ? Client(b) : null;
                var filtered = CompatibilityFilter.Filter(servers, client);
                return filtered.SequenceEqual(servers.Where(s => s.RequiredClientBuild is null || s.RequiredClientBuild == build))
                       && filtered.All(s => CompatibilityFilter.IsCompatible(s, client));
            });
}
