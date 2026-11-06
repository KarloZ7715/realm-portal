using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.Launch;
using RealmPortal.Core.Servers;
using RealmPortal.Core.Service;
using RealmPortal.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;

namespace RealmPortal.App.Tests;

/// <summary>Duplicate… on a server card (#191): prefilled add form; the copy is independent of the original.</summary>
public sealed class DuplicateServerUiTests
{
    private sealed class NoLaunch : IGameLaunchService
    {
        public void Launch(GameLaunchSpec spec) => throw new InvalidOperationException("tests do not launch");
    }

    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Projection = _temp.Combine("projection");
            Backups = _temp.Combine("backups");
            Home = _temp.Combine("home");
            foreach (var d in new[] { Config, Projection, Backups, Home })
                Directory.CreateDirectory(d);
            Session = TestSessions.Create(Config, Projection, Backups, Home, new NoLaunch(), new NeverRunningGameProbe(), seedPreferences: false);
            Loc.Instance.SetLanguage(Language.En);
        }

        public string Config { get; }
        public string Projection { get; }
        public string Backups { get; }
        public string Home { get; }
        public PortalSession Session { get; }

        public string CreateClient(string name)
        {
            var folder = _temp.Combine(name);
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            return folder;
        }

        public void Dispose() => _temp.Dispose();
    }

    [AvaloniaFact]
    public void Duplicate_OpensFormPrefilled_AndSaveCreatesIndependentCopy()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        var original = env.Session.Service.AddServer(new NewServer(
            "Demo Realm",
            "logon.example.com",
            Realm: "Icecrown",
            Account: "demo",
            ClientId: cid,
            RequiredClientBuild: 12340,
            ExtraRealmlistLines: ["set patchlist patch.example.net"],
            ClearCacheOnSwitch: true)).Value!;
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));

        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var card = Assert.Single(main.Servers.Visible);
        Assert.True(card.DuplicateCommand.CanExecute(null));

        card.DuplicateCommand.Execute(null);

        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.True(form.IsDuplicating);
        Assert.False(form.IsEditing);
        Assert.Equal("Demo Realm (copy)", form.Name);
        Assert.Equal("logon.example.com", form.Address);
        Assert.Equal("Icecrown", form.Realm);
        Assert.Equal("demo", form.Account);
        Assert.Equal(12340, form.SelectedBuild?.Build);
        Assert.True(form.ClearCacheOnSwitch);
        Assert.Equal("Duplicate server", form.FormTitle);
        Assert.Equal("Duplicate server · not saved", form.StatusText);

        // Save with every field unchanged: Core DuplicateServer allows the same host+realm as the source.
        form.SaveCommand.Execute(null);

        Assert.Null(main.Adding);
        var listed = env.Session.Service.ListServers(compatibleOnly: false).Value!;
        Assert.Equal(2, listed.Count);
        var copy = Assert.Single(listed, s => s.Id != original.Id);
        Assert.Equal("Demo Realm (copy)", copy.Name);
        Assert.Equal("logon.example.com", copy.Host);
        Assert.Equal("Icecrown", copy.Realm);
        Assert.Equal("demo", copy.Account);
        Assert.Equal(12340, copy.RequiredClientBuild);
        Assert.True(copy.ClearCacheOnSwitch);
        Assert.Contains(copy.ExtraRealmlistLines, line => line.Contains("patch.example.net", StringComparison.Ordinal));
        Assert.NotEqual(original.Id, copy.Id);

        // Editing the copy does not change the original.
        env.Session.Service.UpdateServer(new UpdateServerRequest(copy.Id, "Renamed Copy", copy.Host, "Other", Account: "alt", ClientId: cid));
        var again = env.Session.Service.ListServers(compatibleOnly: false).Value!;
        var stillOriginal = Assert.Single(again, s => s.Id == original.Id);
        Assert.Equal("Demo Realm", stillOriginal.Name);
        Assert.Equal("Icecrown", stillOriginal.Realm);
        Assert.Equal("demo", stillOriginal.Account);
    }

    [AvaloniaFact]
    public void Duplicate_SpanishNameUsesCopiaSuffix()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Mi Servidor", "play.example.net", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("es", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        Loc.Instance.SetLanguage(Language.Es);

        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);

        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Equal("Mi Servidor (copia)", form.Name);
        Assert.Equal("Duplicar servidor", form.FormTitle);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void Duplicate_MenuItem_IsReachableWithAccessibleName()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var window = new MainWindow(main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        var split = window.GetVisualDescendants().OfType<SplitButton>().First();
        split.Flyout!.ShowAt(split);
        Screenshots.Settle();
        var menu = ((MenuFlyout)split.Flyout).Items.OfType<MenuItem>().First(m => m.Name == "MenuDuplicate");
        Assert.Equal("Duplicate server…", Avalonia.Automation.AutomationProperties.GetName(menu));
        Assert.True(menu.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Duplicate_StatusBarShowsDuplicateNotSaved()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);
        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Equal("Duplicate server · not saved", form.StatusText);
        Loc.Instance.SetLanguage(Language.Es);
        form.RefreshText();
        Assert.Equal("Duplicar servidor · sin guardar", form.StatusText);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void Duplicate_CopiesNullClearCacheAsIndeterminateInherit()
    {
        // null ClearCacheOnSwitch means "inherit Settings" (#106); the checkbox stays three-state.
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid, ClearCacheOnSwitch: null));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);
        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Null(form.ClearCacheOnSwitch);
        Assert.Contains("indeterminate", Loc.Get("edit.cache.hint"), StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task CardMenu_StaysInsideTheWindowAt720Spanish()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        var a = env.Session.Service.AddServer(new NewServer("My Server", "logon.example.com", Account: "demo", ClientId: cid)).Value!;
        env.Session.Service.AddServer(new NewServer("Another Server", "play.example.net", ClientId: cid));
        env.Session.Service.SwitchServer(new SwitchRequest(cid, a.Id));
        env.Session.Service.DiscardUndo(cid);
        env.Session.UiPreferences.Save(new UiPreferences("es", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        Loc.Instance.SetLanguage(Language.Es);
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        Screenshots.Settle();
        // Open the menu on the lower card so a downward flyout would overflow without FlipY.
        var card = window.GetVisualDescendants().OfType<SplitButton>().ElementAt(1);
        card.Flyout!.ShowAt(card);
        for (var i = 0; i < 10; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Screenshots.Settle();
        var presenter = window.GetVisualDescendants().OfType<MenuFlyoutPresenter>().FirstOrDefault()
            ?? TopLevel.GetTopLevel(card)!.GetVisualDescendants().OfType<MenuFlyoutPresenter>().Single();
        var topLeft = presenter.TranslatePoint(default, window)!.Value;
        var bottomRight = presenter.TranslatePoint(new Point(presenter.Bounds.Width, presenter.Bounds.Height), window)!.Value;
        Assert.True(topLeft.Y >= 0, $"menu top {topLeft.Y} is above the window");
        Assert.True(bottomRight.Y <= window.Bounds.Height + 0.5, $"menu bottom {bottomRight.Y} exceeds window height {window.Bounds.Height}");
        Assert.True(topLeft.X >= 0);
        Assert.True(bottomRight.X <= window.Bounds.Width + 0.5);
        var delete = ((MenuFlyout)card.Flyout!).Items.OfType<MenuItem>().Single(m => m.Name == "MenuDelete");
        Assert.True(delete.IsEffectivelyVisible);
        Assert.Equal(["MenuPlay", "MenuSwitchOnly", "MenuEdit", "MenuDuplicate", "MenuDelete", "MenuDisabledHint"],
            ((MenuFlyout)card.Flyout!).Items.OfType<MenuItem>().Select(m => m.Name));
        Screenshots.Save(window, 1.0, "duplicate-menu-720-es-fit-1.0x.png");
        Screenshots.Save(window, 2.0, "duplicate-menu-720-es-fit-2.0x.png");
        card.Flyout.Hide();
        window.Close();
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public async Task Duplicate_FocusesNameWithFullTextSelected()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo Realm", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        Screenshots.Settle();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);
        for (var i = 0; i < 20; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Screenshots.Settle();
        var name = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NameBox");
        Assert.True(name.IsKeyboardFocusWithin || name.IsFocused);
        Assert.Equal(0, name.SelectionStart);
        Assert.Equal(name.Text!.Length, name.SelectionEnd);
        Assert.EndsWith(" (copy)", name.Text);
        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(s => s.Name == "Scroller")
                   ?? window.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.ScrollToEnd();
        Screenshots.Settle();
        Screenshots.Save(window, 1.0, "duplicate-form-footer-720-en-1.0x.png");
        Screenshots.Save(window, 2.0, "duplicate-form-footer-720-en-2.0x.png");
        window.Close();
    }

    [AvaloniaFact]
    public async Task ClientCombo_ShowsEllipsisAndTooltipAtNarrowWidth()
    {
        using var env = new Env();
        var folder = env.CreateClient("very-long-client-folder-name-for-ellipsis");
        var cid = env.Session.Service.AddClient(folder, "World of Warcraft · Very Long Display Name For Truncation").Value!.Id;
        env.Session.Service.AddServer(new NewServer("Demo", "logon.example.com", ClientId: cid));
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        var window = new MainWindow(main) { Width = 720, Height = 440 };
        window.Show();
        Screenshots.Settle();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);
        for (var i = 0; i < 20; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Screenshots.Settle();
        var box = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ClientBox");
        TextBlock? label = null;
        for (var i = 0; i < 30 && label is null; i++)
        {
            label = box.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(tb => tb.Text == main.Adding!.SelectedClient!.Label);
            if (label is null)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(20);
            }
        }

        Assert.NotNull(label);
        Assert.Equal(TextTrimming.CharacterEllipsis, label!.TextTrimming);
        Assert.Equal(main.Adding!.SelectedClient!.Label, ToolTip.GetTip(label));
        window.Close();
    }

    [AvaloniaFact]
    public void Duplicate_SaveWithoutEdits_KeepsSameHostAndRealm()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        var original = env.Session.Service.AddServer(new NewServer(
            "Same Host", "logon.example.com", Realm: "Icecrown", Account: "demo", ClientId: cid)).Value!;
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();
        Assert.Single(main.Servers.Visible).DuplicateCommand.Execute(null);
        var form = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.True(form.IsPristine);
        form.SaveCommand.Execute(null);
        Assert.Null(form.SaveError);
        Assert.Null(main.Adding);
        var copy = Assert.Single(env.Session.Service.ListServers(compatibleOnly: false).Value!, s => s.Id != original.Id);
        Assert.Equal(original.Host, copy.Host);
        Assert.Equal(original.Realm, copy.Realm);
        Assert.Equal("Same Host (copy)", copy.Name);
    }

    [AvaloniaFact]
    public void Duplicate_NameTruncatesByGrapheme_NotUtf16()
    {
        using var env = new Env();
        Loc.Instance.SetLanguage(Language.En);
        // 64 UTF-16 units ending in an emoji (one grapheme, two code units): a naive[..] would split it.
        var emoji = "😀";
        Assert.Equal(2, emoji.Length);
        var name = new string('n', Server.MaxNameLength - emoji.Length) + emoji;
        Assert.Equal(Server.MaxNameLength, name.Length);
        var existing = new[] { "other" };
        var copyName = AddServerViewModel.CreateLocalizedDuplicateName(name, existing);
        Assert.EndsWith(" (copy)", copyName);
        Assert.True(copyName.Length <= Server.MaxNameLength);
        Assert.False(copyName.Contains('\uD83D') && !copyName.Contains(emoji), "high surrogate left without the emoji");
        // The emoji must be dropped as a whole when the suffix does not fit beside it.
        Assert.DoesNotContain(emoji, copyName);
        Assert.DoesNotContain("\uD83D", copyName);
        Assert.DoesNotContain("\uDE00", copyName);
    }

    [AvaloniaFact]
    public void Duplicate_Twice_GetsCopyThenCopy2()
    {
        using var env = new Env();
        var client = env.CreateClient("wotlk");
        var cid = env.Session.Service.AddClient(client, "WotLK").Value!.Id;
        var original = env.Session.Service.AddServer(new NewServer("Example", "logon.example.com", ClientId: cid)).Value!;
        env.Session.UiPreferences.Save(new UiPreferences("en", [cid], FirstScanCompleted: true, WelcomeCompleted: true));
        var main = new MainViewModel(env.Session, autoTick: false);
        main.Servers.ReloadFromService();

        main.Servers.Visible.Single(s => s.ServerId == original.Id).DuplicateCommand.Execute(null);
        var first = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Equal("Example (copy)", first.Name);
        first.SaveCommand.Execute(null);
        Assert.Null(main.Adding);
        main.Servers.ReloadFromService();

        main.Servers.Visible.Single(s => s.ServerId == original.Id).DuplicateCommand.Execute(null);
        var second = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Equal("Example (copy 2)", second.Name);
        second.SaveCommand.Execute(null);
        var names = env.Session.Service.ListServers(compatibleOnly: false).Value!.Select(s => s.Name).ToList();
        Assert.Equal(new HashSet<string> { "Example", "Example (copy)", "Example (copy 2)" }, names.ToHashSet());

        // Spanish suffixes are a different string from English ones, so the first free ES name is "(copia)".
        Loc.Instance.SetLanguage(Language.Es);
        main.Servers.Visible.Single(s => s.ServerId == original.Id).DuplicateCommand.Execute(null);
        var third = Assert.IsType<AddServerViewModel>(main.Content);
        Assert.Equal("Example (copia)", third.Name);
        third.SaveCommand.Execute(null);
        main.Servers.ReloadFromService();
        main.Servers.Visible.Single(s => s.ServerId == original.Id).DuplicateCommand.Execute(null);
        Assert.Equal("Example (copia 2)", Assert.IsType<AddServerViewModel>(main.Content).Name);
        Loc.Instance.SetLanguage(Language.En);
    }
}
