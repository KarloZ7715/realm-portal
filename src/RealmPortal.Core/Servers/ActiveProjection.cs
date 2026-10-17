using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RealmPortal.Core.Servers;

/// <summary>
/// The <c>activo.json</c> projection of the active server, consumed by external tools.
/// </summary>
/// <remarks>
/// Exact schema: <c>{ "server_id", "name", "host", "realm", "realmlist" }</c>, always in that order, all strings.
/// <c>realmlist</c> is the full realmlist text with <c>\r\n</c> line endings (JSON-escaped as <c>\r\n</c>),
/// including extra lines such as <c>set patchlist</c>. <c>realm</c> is <c>""</c> when the server has no realm name.
/// Bytes: UTF-8 without BOM, 2-space indent, <c>\n</c> newlines between JSON lines, one trailing <c>\n</c>.
/// Non-ASCII text is written as-is (not <c>\uXXXX</c>-escaped); only <c>"</c>, <c>\</c> and control characters are escaped.
/// </remarks>
public static class ActiveProjection
{
    public const string FileName = "activo.json";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static byte[] Serialize(Server server)
    {
        ArgumentNullException.ThrowIfNull(server);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("server_id", server.Id);
            json.WriteString("name", server.Name);
            json.WriteString("host", server.Host);
            json.WriteString("realm", server.Realm);
            json.WriteString("realmlist", server.Realmlist);
            json.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    public static string FilePath(string projectionDirectory) => Path.Combine(projectionDirectory, FileName);

    internal static readonly UTF8Encoding Utf8NoBom = new(false);
}
