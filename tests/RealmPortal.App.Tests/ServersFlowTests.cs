using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Threading;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.ViewModels;
using RealmPortal.Core;

namespace RealmPortal.App.Tests;

public class ServersFlowTests
{
    private static async Task Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Screenshots.Settle();
    }

    [AvaloniaFact]
    public async Task ListShowsTheActiveServerFirst()
    {
        var shell = new Shell();
        await Pump();
        Assert.Equal(["Mi Servidor", "Otro Servidor", "Servidor Clásico", "Otro Reino RP"], shell.Servers.Visible.Select(s => s.Name));
        Assert.Equal(ServerStatus.Active, shell.Servers.Visible[0].SealStatus);
        Assert.Equal(ServerStatus.Incompatible, shell.Servers.Visible[2].SealStatus);
        shell.Snap("servers-list");
    }

    [AvaloniaFact]
    public async Task WideWindowShowsSidebarLabelsAndTheNote()
    {
        var shell = new Shell(1000, 600);
        await Pump();
        var labels = shell.Window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("navlabel")).ToList();
        Assert.Equal(3, labels.Count);
        Assert.All(labels, l => Assert.True(l.IsVisible));
        Assert.Equal(["Servidores", "Clientes", "Ajustes"], labels.Select(l => l.Text));
        var note = shell.Window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "DisclaimerText");
        Assert.True(note.IsVisible);
        Assert.Equal("No afiliado ni respaldado por Blizzard Entertainment.", note.Text);
        shell.Snap("servers-wide", 1.0);

        shell.Window.Width = 720;
        await Pump();
        Assert.All(labels, l => Assert.False(l.IsVisible));
        Assert.False(note.IsVisible);
    }

    [AvaloniaFact]
    public async Task SearchFiltersByNameAddressAndClient()
    {
        var shell = new Shell();
        shell.Servers.SearchText = "otro";
        await Pump();
        Assert.Equal(["Otro Servidor", "Otro Reino RP"], shell.Servers.Visible.Select(s => s.Name));
        Assert.True(shell.Servers.ShowResults);
        Assert.False(shell.Servers.ShowEmpty);
        Assert.Equal("2 resultados para «otro»", shell.Servers.ResultsText);
        shell.Snap("servers-filtered");

        shell.Servers.SearchText = "classic.ejemplo";
        Assert.Equal(["Servidor Clásico"], shell.Servers.Visible.Select(s => s.Name));
        shell.Servers.SearchText = "1.12.1";
        Assert.Equal(["Servidor Clásico"], shell.Servers.Visible.Select(s => s.Name));
    }

    [AvaloniaFact]
    public async Task SearchWithoutMatchesShowsTheEmptyState()
    {
        var shell = new Shell();
        shell.Servers.SearchText = "zzz";
        await Pump();
        Assert.Empty(shell.Servers.Visible);
        Assert.True(shell.Servers.ShowEmpty);
        Assert.Equal("Sin resultados para «zzz»", shell.Servers.EmptyTitle);
        shell.Snap("servers-empty");

        shell.Servers.ClearSearchCommand.Execute(null);
        await Pump();
        Assert.Equal(4, shell.Servers.Visible.Count);
        Assert.False(shell.Servers.ShowEmpty);
    }

    [AvaloniaFact]
    public async Task SwitchingShowsStepsThenOffersUndo()
    {
        var shell = new Shell();
        var hold = new StepHold(shell.Backend, StepKind.Realmlist);
        var target = shell.Servers.All.First(s => s.Name == "Otro Servidor");
        var running = shell.Servers.SwitchOnlyAsync(target);
        await Pump();

        Assert.Equal(ServersPhase.Switching, shell.Servers.Phase);
        Assert.Equal(["Otro Servidor", "Mi Servidor", "Servidor Clásico", "Otro Reino RP"], shell.Servers.Visible.Select(s => s.Name));
        Assert.Equal([StepState.Done, StepState.Current, StepState.Pending], shell.Servers.Steps.Select(s => s.State));
        Assert.Equal("Cambiando a Otro Servidor…", shell.Servers.StatusText);
        Assert.Equal("Cambiando…", target.BadgeText);
        Assert.False(target.PlayCommand.CanExecute(null));
        Assert.False(shell.Servers.Visible[1].PlayCommand.CanExecute(null));
        shell.Snap("servers-switching");

        hold.ReleaseAll();
        await running;
        await Pump();

        Assert.Equal(ServersPhase.Idle, shell.Servers.Phase);
        Assert.True(target.IsActive);
        Assert.False(shell.Servers.All.First(s => s.Name == "Mi Servidor").IsActive);
        Assert.True(shell.Servers.ToastVisible);
        Assert.Equal("Cambiaste a Otro Servidor", shell.Servers.ToastText);
        Assert.Equal("Activo: Otro Servidor", shell.Servers.StatusText);
        shell.Servers.AdvanceToast(TimeSpan.FromSeconds(3.2));
        Assert.InRange(shell.Servers.ToastFraction, 0.59, 0.61);
        await Pump();
        shell.Snap("servers-undo");
    }

    [AvaloniaFact]
    public async Task UndoRestoresThePreviousServer()
    {
        var shell = new Shell();
        var previous = shell.Servers.Active!;
        var target = shell.Servers.All.First(s => s.Name == "Otro Servidor");
        await shell.Servers.SwitchOnlyAsync(target);
        shell.Servers.UndoCommand.Execute(null);
        await Pump();
        Assert.Same(previous, shell.Servers.Active);
        Assert.False(shell.Servers.ToastVisible);
        Assert.Equal("realmlist · logon.ejemplo.com", shell.Servers.StatusText);
    }

    [AvaloniaFact]
    public async Task UndoNoticeExpires()
    {
        var shell = new Shell();
        await shell.Servers.SwitchOnlyAsync(shell.Servers.All.First(s => s.Name == "Otro Servidor"));
        shell.Servers.AdvanceToast(ServersViewModel.ToastDuration / 2);
        Assert.True(shell.Servers.ToastVisible);
        shell.Servers.AdvanceToast(ServersViewModel.ToastDuration);
        Assert.False(shell.Servers.ToastVisible);
        Assert.Equal("Otro Servidor", shell.Servers.Active!.Name);
        Assert.Equal("realmlist · play.otro.net", shell.Servers.StatusText);
    }

    [AvaloniaFact]
    public async Task IncompatibleServerCannotBeStarted()
    {
        var shell = new Shell();
        var classic = shell.Servers.All.First(s => s.Name == "Servidor Clásico");
        Assert.False(classic.PlayCommand.CanExecute(null));
        await shell.Servers.PlayAsync(classic);
        Assert.Equal(ServersPhase.Idle, shell.Servers.Phase);
        Assert.False(classic.IsActive);
    }

    [AvaloniaFact]
    public async Task PlayOpensTheGameAndWaitsForItToClose()
    {
        var shell = new Shell();
        var hold = new StepHold(shell.Backend, StepKind.Launch);
        var target = shell.Servers.Active!;
        var running = shell.Servers.PlayAsync(target);
        await Pump();

        Assert.Equal(ServersPhase.Opening, shell.Servers.Phase);
        Assert.Equal("Abriendo el juego…", shell.Servers.BannerHeader);
        Assert.Equal("Mi Servidor · Cliente 3.3.5a (12340)", shell.Servers.BannerBody);
        Assert.Equal(100, shell.Servers.Progress);
        Assert.Equal([StepState.Done, StepState.Done, StepState.Current], shell.Servers.Steps.Select(s => s.State));
        Assert.Equal("Abriendo…", target.BadgeText);
        shell.Snap("servers-opening");

        hold.ReleaseAll();
        await running;
        await Pump();
        Assert.Equal(ServersPhase.Running, shell.Servers.Phase);
        Assert.Equal(BannerSeverity.Warning, shell.Servers.BannerSeverity);
        Assert.Equal("Juego abierto · Mi Servidor", shell.Servers.StatusText);
        Assert.Equal("En ejecución", target.BadgeText);
        Assert.False(shell.Servers.RetryCommand.CanExecute(null));
        shell.Snap("servers-running");

        shell.Backend.CloseGame();
        await Pump();
        Assert.True(shell.Servers.GameClosed);
        Assert.True(shell.Servers.RetryCommand.CanExecute(null));
        shell.Servers.RetryCommand.Execute(null);
        await Pump();
        Assert.Equal(ServersPhase.Idle, shell.Servers.Phase);
        Assert.Equal(ServerOverlay.None, target.Overlay);
        Assert.True(target.PlayCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task PlayingAnotherServerMakesItActive()
    {
        var shell = new Shell();
        var other = shell.Servers.All.First(s => s.Name == "Otro Servidor");
        await shell.Servers.PlayAsync(other);
        Assert.True(other.IsActive);
        Assert.Equal("Otro Servidor", shell.Servers.Visible[0].Name);
        Assert.False(shell.Servers.ToastVisible);
    }

    [AvaloniaFact]
    public async Task LanguageSwitchRetranslatesTheScreen()
    {
        var shell = new Shell();
        var loc = Loc.Instance;
        var original = loc.Current;
        try
        {
            loc.SetLanguage(Language.Es);
            Assert.Equal($"ES · v{AppInfo.Version}", shell.Model.LanguageLabel);
            Assert.Equal("Cliente 3.3.5a (12340) · Cuenta: demo", shell.Servers.Active!.DetailText);
            shell.Model.ToggleLanguageCommand.Execute(null);
            await Pump();
            Assert.Equal(Language.En, loc.Current);
            Assert.Equal($"EN · v{AppInfo.Version}", shell.Model.LanguageLabel);
            Assert.Equal("Client 3.3.5a (12340) · Account: demo", shell.Servers.Active!.DetailText);
            Assert.Equal("Active", shell.Servers.Active.BadgeText);
            shell.Servers.SearchText = "zzz";
            Assert.Equal("No results for “zzz”", shell.Servers.EmptyTitle);
            shell.Snap("servers-empty-en", 1.0);
        }
        finally
        {
            loc.SetLanguage(original);
        }
    }

    [AvaloniaFact]
    public void EveryStepHasATranslation()
    {
        foreach (var kind in Enum.GetValues<StepKind>())
        {
            Assert.DoesNotContain("!", new StepViewModel(1, kind, false).Label);
        }
    }

    [AvaloniaFact]
    public void ClickingACardCannotChangeTheActiveServer()
    {
        var shell = new Shell();
        var active = shell.Servers.Active;
        shell.Servers.SelectedCard = shell.Servers.All[1];
        Assert.Same(active, shell.Servers.SelectedCard);
    }
}

public class ServersMenuTests
{
    [AvaloniaFact]
    public async Task SwitchOnlyMenuItemStartsASwitchWithoutLaunching()
    {
        var shell = new Shell();
        var hold = new StepHold(shell.Backend, StepKind.Copy);
        var card = shell.Window.GetVisualDescendants().OfType<SplitButton>().ElementAt(1);
        Assert.Equal("Otro Servidor", ((ServerItemViewModel)card.DataContext!).Name);

        card.Flyout!.ShowAt(card);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Screenshots.Settle();
        shell.Snap("servers-menu");
        var items = card.Flyout is MenuFlyout menu ? menu.Items.OfType<MenuItem>().ToList() : throw new InvalidOperationException();
        Assert.Equal(["MenuPlay", "MenuSwitchOnly", "MenuEdit", "MenuDelete", "MenuDisabledHint"], items.Select(i => i.Name));
        // The disabled-reason row is hidden while nothing is locked (#201).
        Assert.False(items[4].IsVisible);
        // Demo cards have no CRUD handlers, so the real-only actions stay hidden.
        Assert.False(items[2].IsVisible);
        Assert.False(items[3].IsVisible);
        var item = items[1];
        Assert.Equal("Solo cambiar", HeaderTexts(item)[0]);
        Assert.Equal("Solo cambiar", AutomationProperties.GetName(item));
        Assert.Equal("Cambia el realmlist sin abrir el juego.", HeaderTexts(item)[1]);
        Assert.True(item.Command!.CanExecute(null));
        item.Command.Execute(null);
        Screenshots.Settle();

        Assert.Equal(ServersPhase.Switching, shell.Servers.Phase);
        Assert.Equal("Otro Servidor", shell.Servers.Target!.Name);
        hold.ReleaseAll();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ServersPhase.Idle, shell.Servers.Phase);
        Assert.False(shell.Backend.GameRunning);
    }

    [AvaloniaFact]
    public async Task SwitchOnlyMenuItem_UsesTheRepairTitleWhenTheCardNeedsRepair()
    {
        var shell = new Shell();
        var cardVm = shell.Servers.All.First(s => !s.IsActive);
        cardVm.SetNeedsRepair(true);
        var card = shell.Window.GetVisualDescendants().OfType<SplitButton>().First(b => ReferenceEquals(b.DataContext, cardVm));

        card.Flyout!.ShowAt(card);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        var item = ((MenuFlyout)card.Flyout).Items.OfType<MenuItem>().First(i => i.Name == "MenuSwitchOnly");
        Assert.Equal("Reparar", cardVm.SwitchMenuText);
        Assert.Equal(cardVm.SwitchMenuText, AutomationProperties.GetName(item));
    }

    [AvaloniaFact]
    public async Task ActiveServerCannotBeSwitchedToAgainButCanPlay()
    {
        var shell = new Shell();
        var card = shell.Window.GetVisualDescendants().OfType<SplitButton>().First();
        Assert.Equal("Mi Servidor", ((ServerItemViewModel)card.DataContext!).Name);

        card.Flyout!.ShowAt(card);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Screenshots.Settle();
        shell.Snap("servers-menu-disabled");
        var items = ((MenuFlyout)card.Flyout).Items.OfType<MenuItem>().ToList();
        Assert.True(items[0].Command!.CanExecute(null));
        Assert.False(items[1].Command!.CanExecute(null));
        Assert.Equal("Ya es el servidor activo.", HeaderTexts(items[1])[1]);
        Assert.Equal("Cambia de servidor y abre el juego.", HeaderTexts(items[0])[1]);
    }

    private static List<string?> HeaderTexts(MenuItem item) =>
        ((Avalonia.Controls.Panel)item.Header!).Children.OfType<TextBlock>().Select(t => t.Text).ToList();
}
