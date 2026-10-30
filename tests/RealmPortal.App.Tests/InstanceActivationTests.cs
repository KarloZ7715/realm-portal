using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RealmPortal.App.Platform;
using RealmPortal.Core;
using RealmPortal.Core.Instances;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>
/// The app side of the single-instance guard (#197): focus requests reach the main window, and the startup glue only
/// ever uses the config directory it is given (a temp folder here). All tests share static state, so they live in one
/// (sequential) class and reset it.
/// </summary>
public class InstanceActivationTests
{
    [AvaloniaFact]
    public void AFocusRequest_RestoresAMinimizedWindowToMaximized_AndShowsIt()
    {
        InstanceActivation.Reset();
        var window = new Window { Width = 400, Height = 300 };
        try
        {
            window.Show();
            InstanceActivation.Attach(window);
            window.WindowState = WindowState.Maximized;
            window.WindowState = WindowState.Minimized;

            Assert.Equal(InstanceReply.Ok, InstanceActivation.Handle(InstanceMessage.Focus));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Maximized, window.WindowState);
            Assert.True(window.IsVisible);
        }
        finally
        {
            window.Close();
            InstanceActivation.Reset();
        }
    }

    [AvaloniaFact]
    public void AFocusRequest_RestoresAMinimizedNormalWindowToNormal()
    {
        InstanceActivation.Reset();
        var window = new Window { Width = 400, Height = 300 };
        try
        {
            window.Show();
            InstanceActivation.Attach(window);
            window.WindowState = WindowState.Minimized;

            InstanceActivation.Handle(InstanceMessage.Focus);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally
        {
            window.Close();
            InstanceActivation.Reset();
        }
    }

    [AvaloniaFact]
    public void AFocusRequestBeforeTheWindowExists_IsAppliedWhenItIsAttached()
    {
        InstanceActivation.Reset();
        Assert.Equal(InstanceReply.Ok, InstanceActivation.Handle(InstanceMessage.Focus));
        var window = new Window { Width = 400, Height = 300 };
        try
        {
            InstanceActivation.Attach(window); // not shown yet
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsVisible);
        }
        finally
        {
            window.Close();
            InstanceActivation.Reset();
        }
    }

    [AvaloniaFact]
    public void AFocusRequestAfterTheWindowClosed_IsIgnored()
    {
        InstanceActivation.Reset();
        var window = new Window { Width = 400, Height = 300 };
        window.Show();
        InstanceActivation.Attach(window);
        window.Close();

        Assert.Equal(InstanceReply.Ok, InstanceActivation.Handle(InstanceMessage.Focus));
        Dispatcher.UIThread.RunJobs();

        Assert.False(window.IsVisible);
        InstanceActivation.Reset();
    }

    [Fact]
    public void OtherVerbs_AreUnsupported() =>
        Assert.Equal(InstanceReply.Unsupported, InstanceActivation.Handle(new InstanceMessage("play", ["x"])));

    [Theory]
    [InlineData("--demo")]
    [InlineData("--gallery")]
    public void DeveloperModes_SkipTheGuard(string mode)
    {
        using var claim = AppInstance.Claim([mode], _ => throw new InvalidOperationException("must not read the environment"));

        Assert.Same(InstanceClaim.None, claim);
        Assert.False(claim.ShouldExit);
    }

    [Fact]
    public void ASecondLaunchOnTheSameConfigDirectory_IsToldToExit()
    {
        InstanceActivation.Reset();
        using var dir = ShortTempDirectory.Create();
        var config = dir.Combine("config");
        Func<string, string?> env = name => name == PortalPaths.ConfigDirVariable ? config : null;
        var log = new StringWriter();
        try
        {
            using var first = AppInstance.Claim([], env, log);
            using var second = AppInstance.Claim([], env, log);

            Assert.Equal(ClaimOutcome.Primary, first.Outcome);
            Assert.True(first.IsListening);
            Assert.False(first.ShouldExit);
            Assert.Equal(ClaimOutcome.Forwarded, second.Outcome);
            Assert.True(second.ShouldExit);
            Assert.True(File.Exists(Path.Combine(config, ".realm-portal-app.lock")));
            Assert.Equal(string.Empty, log.ToString());
        }
        finally
        {
            InstanceActivation.Reset();
        }
    }
}
