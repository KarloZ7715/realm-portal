using System.Text;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.Tests;

public class ActiveProjectionTests
{
    [Fact]
    public void Serialize_IsByteExact()
    {
        var server = Server.Create(
            "Mi Servidor Ñ",
            "logon.example.org",
            "Realm \"One\"",
            ["set patchlist patch.example.org?a=1&b=2"],
            12340,
            "carlos",
            id: "0123456789abcdef0123456789abcdef");

        var expected =
            "{\n" +
            "  \"server_id\": \"0123456789abcdef0123456789abcdef\",\n" +
            "  \"name\": \"Mi Servidor Ñ\",\n" +
            "  \"host\": \"logon.example.org\",\n" +
            "  \"realm\": \"Realm \\\"One\\\"\",\n" +
            "  \"realmlist\": \"set realmlist logon.example.org\\r\\nset patchlist patch.example.org?a=1&b=2\\r\\n\"\n" +
            "}\n";

        var bytes = ActiveProjection.Serialize(server);

        Assert.Equal(new UTF8Encoding(false).GetBytes(expected), bytes);
    }

    [Fact]
    public void Serialize_UsesEmptyStringForMissingRealm_AndNoBom()
    {
        var bytes = ActiveProjection.Serialize(Server.Create("n", "h"));

        Assert.Equal((byte)'{', bytes[0]);
        Assert.Contains("\"realm\": \"\",", Encoding.UTF8.GetString(bytes));
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^2]);
    }

    [Property(MaxTest = 200)]
    public Property Serialize_ParsesBackToTheSameFiveFieldsInOrder() =>
        Prop.ForAll(ServerGen.Server().ToArbitrary(), server =>
        {
            var bytes = ActiveProjection.Serialize(server);
            using var doc = JsonDocument.Parse(bytes);
            var props = doc.RootElement.EnumerateObject().ToList();
            return props.Select(p => p.Name).SequenceEqual(["server_id", "name", "host", "realm", "realmlist"])
                   && props.All(p => p.Value.ValueKind == JsonValueKind.String)
                   && props[0].Value.GetString() == server.Id
                   && props[1].Value.GetString() == server.Name
                   && props[2].Value.GetString() == server.Host
                   && props[3].Value.GetString() == server.Realm
                   && props[4].Value.GetString() == server.Realmlist
                   && bytes[^1] == (byte)'\n'
                   && !(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                   && !bytes.AsSpan().Contains((byte)'\r'); // CRs only appear escaped inside the JSON string
        });

    [Property(MaxTest = 100)]
    public Property Serialize_IsDeterministic() =>
        Prop.ForAll(ServerGen.Server().ToArbitrary(), server =>
            ActiveProjection.Serialize(server).SequenceEqual(ActiveProjection.Serialize(server)));
}
