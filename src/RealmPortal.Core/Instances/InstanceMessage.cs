using System.Text;
using System.Text.Json;

namespace RealmPortal.Core.Instances;

/// <summary>
/// A request sent from a second launch to the primary instance: a verb (for example <c>focus</c>) and optional string
/// arguments. The channel is generic so later verbs (such as a quick <c>play</c>) can reuse it.
/// </summary>
public sealed record InstanceMessage
{
    /// <summary>Bring the primary instance's main window to the front.</summary>
    public const string FocusVerb = "focus";

    public InstanceMessage(string verb, IReadOnlyList<string>? args = null)
    {
        if (!InstanceProtocol.IsValidVerb(verb))
            throw new ArgumentException($"'{verb}' is not a valid verb (lowercase letters, digits and '-', up to {InstanceProtocol.MaxVerbLength} characters).", nameof(verb));
        args ??= [];
        if (args.Count > InstanceProtocol.MaxArgs)
            throw new ArgumentException($"At most {InstanceProtocol.MaxArgs} arguments are allowed.", nameof(args));
        if (args.Any(a => a is null))
            throw new ArgumentException("Arguments cannot be null.", nameof(args));
        Verb = verb;
        Args = [.. args];
    }

    public static InstanceMessage Focus { get; } = new(FocusVerb);

    public string Verb { get; }

    public IReadOnlyList<string> Args { get; }

    public bool Equals(InstanceMessage? other) =>
        other is not null && Verb == other.Verb && Args.SequenceEqual(other.Args, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(Verb, Args.Count);
}

/// <summary>What the primary instance answered.</summary>
public enum InstanceReply
{
    /// <summary>The message was accepted (for <c>focus</c>: queued for the UI thread).</summary>
    Ok,

    /// <summary>The message was well formed but the primary does not handle that verb.</summary>
    Unsupported,

    /// <summary>The request was malformed, too large or too slow; nothing was done.</summary>
    Invalid,
}

/// <summary>
/// Wire format of the instance channel: one UTF-8 JSON object per direction, terminated by <c>\n</c>, at most
/// <see cref="MaxLineBytes"/> bytes. Request: <c>{"protocol":1,"verb":"focus","args":[]}</c>. Reply:
/// <c>{"protocol":1,"reply":"ok"}</c> (or <c>unsupported</c> / <c>invalid</c>).
/// </summary>
internal static class InstanceProtocol
{
    public const int Version = 1;
    public const int MaxLineBytes = 4096;
    public const int MaxArgs = 16;
    public const int MaxVerbLength = 32;

    public static bool IsValidVerb(string? verb)
    {
        if (string.IsNullOrEmpty(verb) || verb.Length > MaxVerbLength || verb[0] is < 'a' or > 'z')
            return false;
        foreach (var c in verb)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                return false;
        }
        return true;
    }

    public static byte[] EncodeRequest(InstanceMessage message)
    {
        var bytes = Encode(writer =>
        {
            writer.WriteNumber("protocol", Version);
            writer.WriteString("verb", message.Verb);
            writer.WriteStartArray("args");
            foreach (var arg in message.Args)
                writer.WriteStringValue(arg);
            writer.WriteEndArray();
        });
        if (bytes.Length > MaxLineBytes)
            throw new ArgumentException($"The message is larger than {MaxLineBytes} bytes.", nameof(message));
        return bytes;
    }

    public static byte[] EncodeReply(InstanceReply reply) =>
        Encode(writer =>
        {
            writer.WriteNumber("protocol", Version);
            writer.WriteString("reply", ReplyName(reply));
        });

    /// <summary>Parses one request line (without the trailing newline). Never throws.</summary>
    public static InstanceMessage? TryDecodeRequest(ReadOnlySpan<byte> line)
    {
        if (line.Length is 0 or > MaxLineBytes)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("protocol", out var protocol)
                || protocol.ValueKind != JsonValueKind.Number
                || !protocol.TryGetInt32(out var version)
                || version != Version
                || !root.TryGetProperty("verb", out var verbElement)
                || verbElement.ValueKind != JsonValueKind.String)
                return null;

            var verb = verbElement.GetString();
            if (!IsValidVerb(verb))
                return null;

            var args = new List<string>();
            if (root.TryGetProperty("args", out var argsElement))
            {
                if (argsElement.ValueKind != JsonValueKind.Array || argsElement.GetArrayLength() > MaxArgs)
                    return null;
                foreach (var arg in argsElement.EnumerateArray())
                {
                    if (arg.ValueKind != JsonValueKind.String)
                        return null;
                    args.Add(arg.GetString()!);
                }
            }

            return new InstanceMessage(verb!, args);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>Parses one reply line. Never throws; anything unexpected is <see cref="InstanceReply.Invalid"/>.</summary>
    public static InstanceReply DecodeReply(ReadOnlySpan<byte> line)
    {
        if (line.Length is 0 or > MaxLineBytes)
            return InstanceReply.Invalid;
        try
        {
            using var doc = JsonDocument.Parse(line.ToArray());
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("reply", out var reply)
                && reply.ValueKind == JsonValueKind.String)
            {
                return reply.GetString() switch
                {
                    "ok" => InstanceReply.Ok,
                    "unsupported" => InstanceReply.Unsupported,
                    _ => InstanceReply.Invalid,
                };
            }
        }
        catch (JsonException)
        {
        }
        return InstanceReply.Invalid;
    }

    /// <summary>
    /// Reads bytes up to the first <c>\n</c>. Returns null when the peer closes first or sends more than
    /// <see cref="MaxLineBytes"/> bytes without a newline. Cancellation (the per-connection deadline) propagates.
    /// </summary>
    public static async Task<byte[]?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxLineBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return null;
            var newline = Array.IndexOf(buffer, (byte)'\n', length, read);
            length += read;
            if (newline >= 0)
                return buffer[..newline];
        }
        return null;
    }

    private static string ReplyName(InstanceReply reply) => reply switch
    {
        InstanceReply.Ok => "ok",
        InstanceReply.Unsupported => "unsupported",
        _ => "invalid",
    };

    private static byte[] Encode(Action<Utf8JsonWriter> body)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        output.WriteByte((byte)'\n');
        return output.ToArray();
    }
}
