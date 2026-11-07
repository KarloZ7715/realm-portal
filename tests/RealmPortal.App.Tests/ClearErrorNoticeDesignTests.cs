using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Controls;
using RealmPortal.App.Localization;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.App.Views;
using RealmPortal.Core.Service;

namespace RealmPortal.App.Tests;

public sealed class ClearErrorNoticeDesignTests
{
    public static TheoryData<ServiceErrorCode, Language> ClientErrors => new()
    {
        { ServiceErrorCode.DiskFull, Language.En },
        { ServiceErrorCode.DiskFull, Language.Es },
        { ServiceErrorCode.PermissionDenied, Language.En },
        { ServiceErrorCode.PermissionDenied, Language.Es },
        { ServiceErrorCode.GameRunning, Language.En },
        { ServiceErrorCode.GameRunning, Language.Es },
        { ServiceErrorCode.LaunchFailed, Language.En },
        { ServiceErrorCode.LaunchFailed, Language.Es },
        { ServiceErrorCode.StoreLocked, Language.En },
        { ServiceErrorCode.StoreLocked, Language.Es },
        { ServiceErrorCode.InconsistentState, Language.En },
        { ServiceErrorCode.InconsistentState, Language.Es },
        { ServiceErrorCode.UndoConflict, Language.En },
        { ServiceErrorCode.UndoConflict, Language.Es },
        { ServiceErrorCode.IncompatibleClient, Language.En },
        { ServiceErrorCode.IncompatibleClient, Language.Es },
        { ServiceErrorCode.PathNotFound, Language.En },
        { ServiceErrorCode.PathNotFound, Language.Es },
        { ServiceErrorCode.ClientNotFound, Language.En },
        { ServiceErrorCode.ClientNotFound, Language.Es },
    };

    [AvaloniaTheory]
    [MemberData(nameof(ClientErrors))]
    public void EveryClientErrorUsesOneAnnouncedNoticeAboveTheEmptyState(ServiceErrorCode code, Language language)
    {
        using var env = new IsolatedAppSession(languageCode: language == Language.Es ? "es" : "en");
        var main = new MainViewModel(env.Session, autoTick: false);
        main.SectionIndex = 1;
        main.Clients.Notice = "Must not be rendered as a second loose error";
        main.Clients.Recovery.Show(new ServiceError(code, Path: code == ServiceErrorCode.UndoConflict ? null : "/home/demo/client",
            ConflictingFiles: code == ServiceErrorCode.UndoConflict ? ["Data/enUS/realmlist.wtf", "WTF/Config.wtf"] : null,
            Consistency: code == ServiceErrorCode.UndoConflict ? Consistency.Pending : Consistency.Unchanged), () => Task.CompletedTask);
        var window = new MainWindow(main) { Width = 720, Height = 480 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var view = window.GetVisualDescendants().OfType<ClientsView>().Single();
            var recovery = view.GetVisualDescendants().OfType<ErrorRecoveryView>().Single();
            var notice = recovery.FindControl<Banner>("ErrorNotice");
            Assert.NotNull(notice);
            Assert.True(notice.IsEffectivelyVisible);
            Assert.Equal(Loc.Get("error.banner.title"), notice.Header);
            Assert.Equal("Assertive", AutomationProperties.GetLiveSetting(notice).ToString());
            var cause = recovery.FindControl<TextBlock>("RecoveryCause")!;
            Assert.Equal($"{notice.Header}. {cause.Text}", AutomationProperties.GetName(notice));
            Assert.True(cause.IsEffectivelyVisible);
            if (code == ServiceErrorCode.PathNotFound)
                Assert.StartsWith(language == Language.Es ? "La siguiente ruta" : "The following path", cause.Text);
            Assert.True(recovery.FindControl<TextBlock>("RecoveryHint")!.IsEffectivelyVisible);
            Assert.True(recovery.FindControl<Button>("RecoveryPrimary")!.IsEffectivelyVisible);
            Assert.Contains(notice, recovery.FindControl<TextBlock>("RecoveryHint")!.GetVisualAncestors());
            Assert.Contains(notice, recovery.FindControl<Button>("RecoveryPrimary")!.GetVisualAncestors());
            Assert.False(view.FindControl<TextBlock>("ScanNotice")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<ContentControl>("ClientsSheet")!.IsEffectivelyVisible);
            var empty = view.FindControl<StackPanel>("ClientsEmptyState")!;
            Assert.True(empty.IsEffectivelyVisible);
            Assert.Contains("primary", view.FindControl<Button>("EmptyClientScan")!.Classes);
            Assert.Contains(Loc.Get("clients.empty.title"), empty.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal(Loc.Format("status.error.section", Loc.Get("nav.clients")), main.StatusText);
            var add = view.FindControl<Button>("EmptyClientAdd")!;
            var addBottom = add.TranslatePoint(new Point(0, add.Bounds.Height), window)!.Value.Y;
            var statusTop = window.FindControl<TextBlock>("StatusText")!.TranslatePoint(default, window)!.Value.Y;
            Assert.True(addBottom < statusTop, $"{code}/{language}: empty actions end at {addBottom}, status begins at {statusTop}");
            var bar = notice.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Bar");
            Assert.Equal(3, bar.Width);
        }
        finally { window.Close(); main.DisposeNetwork(); Loc.Instance.SetLanguage(Language.En); }
    }

    [AvaloniaFact]
    public void ServerRecoveryIsInsideTheSameNoticeInsteadOfBesideThePhaseBanner()
    {
        using var env = new IsolatedAppSession();
        var main = new MainViewModel(env.Session, autoTick: false);
        File.WriteAllText(Path.Combine(env.Config, "servers.json"), "{ damaged");
        main.Servers.ReloadFromService();
        var window = new MainWindow(main) { Width = 720, Height = 480 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var view = window.GetVisualDescendants().OfType<ServersView>().Single();
            Assert.False(view.FindControl<Banner>("PhaseBanner")!.IsEffectivelyVisible);
            Assert.True(view.GetVisualDescendants().OfType<ErrorRecoveryView>().Single().FindControl<Banner>("ErrorNotice")!.IsEffectivelyVisible);
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaTheory]
    [InlineData(Language.En)]
    [InlineData(Language.Es)]
    public void EmptyFormKeepsItsFooterAndPlacesClientRequirementInsideParchment(Language language)
    {
        using var env = new IsolatedAppSession(languageCode: language == Language.Es ? "es" : "en");
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.RequestAddServerCommand.Execute(null);
        var window = new MainWindow(main) { Width = 720, Height = 480 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var view = window.GetVisualDescendants().OfType<AddServerView>().Single();
            var cancel = view.FindControl<Button>("CancelButton")!;
            var save = view.FindControl<Button>("SaveButton")!;
            var test = view.FindControl<Button>("TestButton")!;
            Assert.True(cancel.IsEffectivelyVisible);
            Assert.True(test.IsEffectivelyVisible);
            Assert.False(save.IsEffectivelyEnabled);
            Assert.False(save.Command!.CanExecute(null));
            Assert.Equal(Loc.Get("add.clients.empty"), AutomationProperties.GetHelpText(save));
            Assert.DoesNotContain(view.FindControl<ScrollViewer>("Scroller"), cancel.GetVisualAncestors());
            Assert.True(cancel.TranslatePoint(new Point(0, cancel.Bounds.Height), window)!.Value.Y < window.FindControl<TextBlock>("StatusText")!.TranslatePoint(default, window)!.Value.Y);
            var requirement = view.GetVisualDescendants().OfType<ErrorRecoveryView>().Single(n => n.IsEffectivelyVisible);
            Assert.Contains(requirement.GetVisualAncestors().OfType<ContentControl>(), c => c.Classes.Contains("card"));
            view.FindControl<ScrollViewer>("Scroller")!.ScrollToEnd();
            Screenshots.Settle();
            Assert.True(cancel.IsEffectivelyVisible);
            main.Adding!.CancelCommand.Execute(null);
            Assert.Null(main.Adding);
        }
        finally { window.Close(); main.DisposeNetwork(); Loc.Instance.SetLanguage(Language.En); }
    }

    [AvaloniaFact]
    public void UndoFilesAreSeparateMonoListItemsNotInlineCauseText()
    {
        var recovery = new ErrorRecoveryViewModel();
        recovery.Show(new ServiceError(ServiceErrorCode.UndoConflict, ConflictingFiles: ["Data/enUS/realmlist.wtf", "WTF/Config.wtf"]));
        var view = new ErrorRecoveryView { DataContext = recovery };
        var window = new Window { Content = view, Width = 560, Height = 320 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var cause = view.FindControl<TextBlock>("RecoveryCause")!;
            Assert.DoesNotContain("realmlist.wtf", cause.Text!);
            var files = view.FindControl<ItemsControl>("ConflictingFiles")!;
            Assert.Equal(2, files.ItemCount);
            var text = files.GetVisualDescendants().OfType<TextBlock>().ToList();
            Assert.Contains(text, t => t.Text == "Data/enUS/realmlist.wtf" && t.FontFamily.Name.Contains("JetBrains Mono"));
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(ServiceErrorCode.DiskFull, BannerSeverity.Warning)]
    [InlineData(ServiceErrorCode.GameRunning, BannerSeverity.Warning)]
    [InlineData(ServiceErrorCode.LaunchFailed, BannerSeverity.Error)]
    [InlineData(ServiceErrorCode.InconsistentState, BannerSeverity.Error)]
    public void SeverityIsNotInferredOnlyFromHavingAnAction(ServiceErrorCode code, BannerSeverity expected)
    {
        var recovery = new ErrorRecoveryViewModel();
        recovery.Show(new ServiceError(code));
        var view = new ErrorRecoveryView { DataContext = recovery };
        var window = new Window { Content = view, Width = 560, Height = 320 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var notice = view.FindControl<Banner>("ErrorNotice")!;
            Assert.Equal(expected, notice.Severity);
            var bar = notice.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Bar");
            Assert.Equal(Application.Current!.FindResource(expected == BannerSeverity.Error ? "WaxLightBrush" : "AmberLightBrush"), bar.Background);
            var icon = notice.GetVisualDescendants().OfType<Icon>().Single(i => i.Name == "Glyph");
            Assert.Equal(Application.Current!.FindResource("IconWarning"), icon.Data);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void LongPathUsesResponsiveMiddleEllipsisAndRetainsItsFullAccessibleValue()
    {
        var full = "/home/demo/" + string.Concat(Enumerable.Repeat("very-long-folder/", 12)) + "Wow.exe";
        var recovery = new ErrorRecoveryViewModel();
        recovery.Show(new ServiceError(ServiceErrorCode.PathNotFound, Path: full));
        var view = new ErrorRecoveryView { DataContext = recovery };
        var window = new Window { Content = view, Width = 420, Height = 320 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var path = view.FindControl<TextBlock>("RecoveryPath")!;
            Assert.Contains("…", path.Text!);
            Assert.StartsWith("/home/demo/", path.Text);
            Assert.EndsWith("Wow.exe", path.Text);
            Assert.Equal(full, ToolTip.GetTip(path));
            Assert.Equal(full, AutomationProperties.GetName(path));
            Assert.Contains("JetBrains Mono", path.FontFamily.Name);
            Assert.DoesNotContain(full, view.FindControl<TextBlock>("RecoveryCause")!.Text!);
            var shortLength = path.Text!.Length;
            window.Width = 900;
            Screenshots.Settle();
            Assert.True(path.Text!.Length > shortLength);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EmptyClientButtonsInvokeScanAndManualPickerAndAreKeyboardReachable()
    {
        using var env = new IsolatedAppSession();
        var picked = 0;
        var main = new MainViewModel(env.Session, () => { picked++; return Task.FromResult<string?>(null); }, autoTick: false);
        main.SectionIndex = 1;
        var window = new MainWindow(main) { Width = 720, Height = 480 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var view = window.GetVisualDescendants().OfType<ClientsView>().Single();
            var scan = view.FindControl<Button>("EmptyClientScan")!;
            var add = view.FindControl<Button>("EmptyClientAdd")!;
            Assert.Same(main.Clients.RescanCommand, scan.Command);
            Assert.Same(main.Clients.PickFolderCommand, add.Command);
            Assert.True(scan.Focus());
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)scan.Command!).ExecuteAsync(null);
            Assert.Equal(Loc.Format("scan.outcome.nochange.count", 0, 0), main.Clients.Notice);
            Assert.True(add.Focus());
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)add.Command!).ExecuteAsync(null);
            Assert.Equal(1, picked);
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaFact]
    public async Task FormRequirementPickerKeepsFormAndEnablesSaveOnlyAfterAClientIsSelected()
    {
        using var env = new IsolatedAppSession();
        var folder = env.CreateClientFolder("demo-client");
        var main = new MainViewModel(env.Session, () => Task.FromResult<string?>(folder), autoTick: false);
        main.Servers.RequestAddServerCommand.Execute(null);
        var form = main.Adding!;
        form.Name = "Demo";
        form.Address = "play.example.com";
        var window = new MainWindow(main) { Width = 720, Height = 480 };
        window.Show();
        try
        {
            Screenshots.Settle();
            var view = window.GetVisualDescendants().OfType<AddServerView>().Single();
            var notice = view.GetVisualDescendants().OfType<ErrorRecoveryView>().Single(n => n.IsEffectivelyVisible);
            var button = notice.FindControl<Button>("RecoverySecondary")!;
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)button.Command!).ExecuteAsync(null);
            Screenshots.Settle();
            Assert.Same(form, main.Adding);
            Assert.Equal("Demo", form.Name);
            form.SelectedClient = main.Clients.Items.Single();
            Screenshots.Settle();
            var save = view.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsEffectivelyEnabled);
            Assert.True(save.Command!.CanExecute(null));
            Assert.Equal(string.Empty, AutomationProperties.GetHelpText(save));
            Assert.False(notice.IsEffectivelyVisible);
            main.Clients.Items.Clear();
            Screenshots.Settle();
            Assert.False(save.IsEffectivelyEnabled);
            Assert.Equal(Loc.Get("add.clients.empty"), AutomationProperties.GetHelpText(save));
            form.CancelCommand.Execute(null);
        }
        finally { window.Close(); main.DisposeNetwork(); }
    }

    [AvaloniaFact]
    public void LanguageRefreshUpdatesTheWholeAssertiveAnnouncementAndBackupAction()
    {
        var recovery = new ErrorRecoveryViewModel { CanRestoreConfig = _ => true };
        recovery.Show(new ServiceError(ServiceErrorCode.StoreCorrupt, Path: "/home/demo/servers.json"));
        var view = new ErrorRecoveryView { DataContext = recovery };
        var window = new Window { Content = view, Width = 560, Height = 320 };
        window.Show();
        try
        {
            foreach (var language in new[] { Language.Es, Language.En })
            {
                Loc.Instance.SetLanguage(language);
                recovery.RefreshText();
                Screenshots.Settle();
                var notice = view.FindControl<Banner>("ErrorNotice")!;
                Assert.Equal(Loc.Get("error.banner.title"), notice.Header);
                Assert.Equal($"{notice.Header}. {recovery.Cause}", AutomationProperties.GetName(notice));
                Assert.Equal(language == Language.Es ? "Revisar copias de configuración" : "Review config backups", recovery.PrimaryText);
            }
        }
        finally { window.Close(); Loc.Instance.SetLanguage(Language.En); }
    }

}
