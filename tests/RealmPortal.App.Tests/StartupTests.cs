using System.Reflection;
using System.Reflection.Emit;
using RealmPortal.App.Platform;
using RealmPortal.Core;
using RealmPortal.Core.Instances;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>
/// The app's startup sequence (<see cref="Program.Run"/>): the crash log handlers (#178) must be installed before
/// anything else, including the single-instance claim (#197), and <see cref="Program.Main"/> must use the real steps.
/// </summary>
public class StartupTests
{
    [Fact]
    public void Run_InstallsTheCrashLogFirst_ThenClaims_ThenStartsTheUi()
    {
        var calls = new List<string>();
        var steps = new StartupSteps(
            _ => calls.Add("crash-log"),
            _ => { calls.Add("claim"); return InstanceClaim.None; },
            _ => { calls.Add("ui"); return 7; });

        var exitCode = Program.Run(["--x"], steps);

        Assert.Equal(["crash-log", "claim", "ui"], calls);
        Assert.Equal(7, exitCode);
    }

    [Fact]
    public void Run_ExitsWithoutStartingTheUi_WhenAnotherInstanceTookOver()
    {
        using var dir = ShortTempDirectory.Create();
        var endpoint = InstanceEndpoint.For(dir.Path);
        using var primary = SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok);
        var calls = new List<string>();
        var steps = new StartupSteps(
            _ => calls.Add("crash-log"),
            _ => SingleInstance.Claim(endpoint, InstanceMessage.Focus, _ => InstanceReply.Ok),
            _ => { calls.Add("ui"); return 1; });

        var exitCode = Program.Run([], steps);

        Assert.Equal(0, exitCode);
        Assert.Equal(["crash-log"], calls);
    }

    [Fact]
    public void DefaultSteps_InstallTheExceptionLogProcessHandlers()
    {
        var expected = typeof(ExceptionLog).GetMethod(nameof(ExceptionLog.InstallProcessHandlers), BindingFlags.Public | BindingFlags.Static);

        Assert.Equal(expected, StartupSteps.Default.InstallCrashLog.Method);
    }

    [Fact]
    public void Main_RunsTheDefaultSteps()
    {
        var main = typeof(Program).GetMethod(nameof(Program.Main), BindingFlags.Public | BindingFlags.Static)!;

        var called = CalledMethods(main);

        Assert.Contains(typeof(StartupSteps).GetProperty(nameof(StartupSteps.Default))!.GetMethod, called);
        Assert.Contains(typeof(Program).GetMethod(nameof(Program.Run), BindingFlags.NonPublic | BindingFlags.Static), called);
    }

    [Fact]
    public void Diagnostics_AreKeptOnOneLine() =>
        Assert.Equal("a b  c", AppInstance.OneLine("a\nb\r\nc"));

    /// <summary>Methods referenced by <c>call</c> / <c>callvirt</c> instructions in a small method body.</summary>
    private static List<MethodBase?> CalledMethods(MethodInfo method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        var result = new List<MethodBase?>();
        for (var i = 0; i < il.Length; i++)
        {
            if ((il[i] == OpCodes.Call.Value || il[i] == OpCodes.Callvirt.Value) && i + 4 < il.Length)
            {
                try
                {
                    result.Add(method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1)));
                    i += 4;
                }
                catch (ArgumentException)
                {
                    // Not a method token at this offset (a byte of another operand); keep scanning.
                }
            }
        }
        return result;
    }
}
