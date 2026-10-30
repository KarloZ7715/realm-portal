using RealmPortal.Core.Instances;

namespace RealmPortal.Tests.Support;

/// <summary>
/// Entry point of the test assembly when it runs as a helper child process (<c>dotnet exec RealmPortal.Tests.dll
/// instance-probe …</c>), so tests can use real processes. The test runner itself never calls it.
/// Only touches the directory it is given (always a test temp folder).
/// </summary>
public static class ProbeProgram
{
    public static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "instance-probe")
            return InstanceProbe(args[1], args[2], args[3..]);
        Console.Error.WriteLine("Usage: instance-probe primary|forward|primary-with-child <dir> [args]");
        return 2;
    }

    private static int InstanceProbe(string mode, string directory, string[] rest)
    {
        var endpoint = InstanceEndpoint.For(directory);
        var output = Console.Out;
        var sync = new object();

        void Say(string line)
        {
            lock (sync)
            {
                output.WriteLine(line);
                output.Flush();
            }
        }

        switch (mode)
        {
            case "forward":
            {
                var verb = rest.Length > 0 ? rest[0] : InstanceMessage.FocusVerb;
                using var claim = SingleInstance.Claim(endpoint, new InstanceMessage(verb, rest.Skip(1).ToArray()), _ => InstanceReply.Ok);
                Say($"outcome {claim.Outcome} {claim.Reply}");
                return 0;
            }

            case "primary":
            case "primary-with-child":
            {
                using var claim = SingleInstance.Claim(endpoint, InstanceMessage.Focus, message =>
                {
                    Say($"message {message.Verb} {string.Join(' ', message.Args)}".TrimEnd());
                    return message.Verb == "unknown" ? InstanceReply.Unsupported : InstanceReply.Ok;
                });
                if (mode == "primary-with-child" && claim.Outcome == ClaimOutcome.Primary)
                {
                    // Stands in for a game started from the app: it must not inherit the lock or the socket.
                    var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(rest[0], "60") { UseShellExecute = false })!;
                    Say($"child {child.Id}");
                }
                Say($"outcome {claim.Outcome} listening={claim.IsListening}");
                // Hold the claim until stdin closes or says "exit".
                string? line;
                while ((line = Console.In.ReadLine()) is not null && line != "exit")
                {
                }
                return 0;
            }

            default:
                return 2;
        }
    }
}
