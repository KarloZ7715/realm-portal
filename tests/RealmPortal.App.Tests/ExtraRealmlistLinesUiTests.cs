using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Portal;
using RealmPortal.App.ViewModels;
using RealmPortal.App.Views;
using RealmPortal.Core.Service;
using RealmPortal.Core.Servers;
using RealmPortal.Tests.Support;

namespace RealmPortal.App.Tests;

/// <summary>Add/Edit Advanced section for extra realmlist lines (#192).</summary>
public sealed class ExtraRealmlistLinesUiTests
{
    private sealed class Env : IDisposable
    {
        private readonly TempDirectory _temp = new();

        public Env()
        {
            Config = _temp.Combine("config");
            Directory.CreateDirectory(Config);
            Session = PortalSession.CreateForPaths(Config, _temp.Combine("projection"), _temp.Combine("backups"));
            Loc.Instance.SetLanguage(Language.En);
            var folder = _temp.Combine("home", "demo", "Games", "WoW-335");
            Directory.CreateDirectory(Path.Combine(folder, "Data", "enUS"));
            File.WriteAllBytes(Path.Combine(folder, "Wow.exe"), SyntheticPe.WithVersion(new Version(3, 3, 5, 12340)));
            ClientId = Session.Service.AddClient(folder, "WotLK").Value!.Id;
            Session.UiPreferences.Save(new UiPreferences("en", [ClientId], FirstScanCompleted: true, WelcomeCompleted: true));
            Main = new MainViewModel(Session, autoTick: false);
            Main.Servers.ReloadFromService();
        }

        public string Config { get; }
        public string ClientId { get; }
        public PortalSession Session { get; }
        public MainViewModel Main { get; }

        public ServerEntry Add(string name, string host, IReadOnlyList<string>? extra = null) =>
            Session.Service.AddServer(new NewServer(name, host, ClientId: ClientId, ExtraRealmlistLines: extra)).Value!;

        public ServerEntry Reload(string id) =>
            Session.Service.ListServers(compatibleOnly: false).Value!.Single(s => s.Id == id);

        public void Dispose()
        {
            Loc.Instance.SetLanguage(Language.En);
            _temp.Dispose();
        }
    }

    [AvaloniaFact]
    public void AddForm_AdvancedIsCollapsedByDefault()
    {
        using var env = new Env();
        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        Assert.False(form.IsAdvancedExpanded);
        Assert.Equal(string.Empty, form.ExtraRealmlistLinesText);
        Assert.Equal(0, form.NonBlankExtraLineCount);
        Assert.Equal(Loc.Get("add.advanced"), form.AdvancedHeaderText);
    }

    [AvaloniaFact]
    public void EditForm_ExpandsAndShowsCountWhenServerHasLines()
    {
        using var env = new Env();
        var added = env.Add("WithPatch", "logon.example.com", ["set patchlist patch.example.com"]);
        var form = new AddServerViewModel(env.Main.Clients, env.Session, env.Reload(added.Id), () => { }, () => { });
        Assert.True(form.IsAdvancedExpanded);
        Assert.Equal("set patchlist patch.example.com", form.ExtraRealmlistLinesText);
        Assert.Equal(1, form.NonBlankExtraLineCount);
        Assert.Equal(Loc.Get("add.advanced.count.one"), form.AdvancedHeaderText);

        form.ExtraRealmlistLinesText = "set patchlist a.example.com\nset patchlist b.example.com";
        Assert.Equal(2, form.NonBlankExtraLineCount);
        Assert.Equal(Loc.Format("add.advanced.count", 2), form.AdvancedHeaderText);
    }

    [AvaloniaFact]
    public void Save_PersistsAndReplacesExtraLines_EmptyClearsThem()
    {
        using var env = new Env();
        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        form.Name = "Demo";
        form.Address = "logon.example.com";
        form.SelectedClient = env.Main.Clients.Items.First(c => c.Id == env.ClientId);
        form.ExtraRealmlistLinesText = "set patchlist patch.example.com\n\n  \n";
        form.SaveCommand.Execute(null);
        Assert.False(form.HasExtraLinesError);
        Assert.False(form.HasSaveError);

        var saved = env.Session.Service.ListServers(compatibleOnly: false).Value!.Single(s => s.Name == "Demo");
        Assert.Equal(["set patchlist patch.example.com"], saved.ExtraRealmlistLines);

        var edit = new AddServerViewModel(env.Main.Clients, env.Session, env.Reload(saved.Id), () => { }, () => { });
        edit.ExtraRealmlistLinesText = "set patchlist other.example.com";
        edit.SaveCommand.Execute(null);
        Assert.Equal(["set patchlist other.example.com"], env.Reload(saved.Id).ExtraRealmlistLines);

        var clear = new AddServerViewModel(env.Main.Clients, env.Session, env.Reload(saved.Id), () => { }, () => { });
        clear.ExtraRealmlistLinesText = "  \n\n";
        clear.SaveCommand.Execute(null);
        Assert.Empty(env.Reload(saved.Id).ExtraRealmlistLines);
    }

    [AvaloniaFact]
    public void Save_SetRealmlistLine_ShowsInlineErrorAndBlocksSave()
    {
        using var env = new Env();
        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        form.Name = "Bad";
        form.Address = "logon.example.com";
        form.SelectedClient = env.Main.Clients.Items.First(c => c.Id == env.ClientId);
        form.ExtraRealmlistLinesText = "SET realmlist other.example.com";
        form.SaveCommand.Execute(null);

        Assert.True(form.HasExtraLinesError);
        Assert.Equal(
            Loc.Format("add.advanced.error.line", 1, Loc.Get("add.advanced.error.realmlist")),
            form.ExtraLinesError);
        Assert.True(form.IsAdvancedExpanded);
        Assert.Empty(env.Session.Service.ListServers(compatibleOnly: false).Value!);
    }

    [AvaloniaTheory]
    [InlineData("set realmlist host.example.com", true)]
    [InlineData("SET REALMLIST host.example.com", true)]
    [InlineData("Set Realmlist host.example.com", true)]
    [InlineData("SET  REALMLIST host.example.com", true)]
    [InlineData("set\trealmlist host.example.com", true)]
    [InlineData("  set realmlist host.example.com", true)]
    [InlineData("\tSET\tREALMLIST\t host.example.com", true)]
    [InlineData("set realmlistfoo host.example.com", false)]
    [InlineData("setrealmlist host.example.com", false)]
    [InlineData("set patchlist host.example.com", false)]
    public void LooksLikeSetRealmlist_TokenizesOnAnyWhitespace(string line, bool expected) =>
        Assert.Equal(expected, AddServerViewModel.LooksLikeSetRealmlist(line));

        [AvaloniaFact]
    public void BlankAndWhitespaceOnlyLines_AreDroppedByViewModelBeforeCore()
    {
        using var env = new Env();
        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        form.Name = "Filtered";
        form.Address = "logon.example.com";
        form.SelectedClient = env.Main.Clients.Items.First(c => c.Id == env.ClientId);
        // Empty, spaces-only and tab-only rows must not reach Core or the count header.
        // One patchlist only: Core rejects duplicate keys.
        form.ExtraRealmlistLinesText = "set patchlist a.example.com\n\n   \n\t\n  ";

        Assert.Equal(
            ["set patchlist a.example.com"],
            AddServerViewModel.SplitNonBlankExtraLines(form.ExtraRealmlistLinesText));
        Assert.Equal(1, form.NonBlankExtraLineCount);
        Assert.Equal(Loc.Get("add.advanced.count.one"), form.AdvancedHeaderText);

        form.SaveCommand.Execute(null);
        Assert.False(form.HasExtraLinesError);
        Assert.False(form.HasSaveError);
        var saved = env.Session.Service.ListServers(compatibleOnly: false).Value!.Single(s => s.Name == "Filtered");
        Assert.Equal(["set patchlist a.example.com"], saved.ExtraRealmlistLines);
    }

    [AvaloniaFact]
    public void LocParity_AdvancedKeysExistInBothLanguages()
    {
        var keys = new[]
        {
            "add.advanced", "add.advanced.count", "add.advanced.count.one", "add.advanced.lines",
            "add.advanced.placeholder", "add.advanced.help", "add.advanced.error.realmlist", "add.advanced.error",
        };
        var en = Loc.Load(Language.En);
        var es = Loc.Load(Language.Es);
        foreach (var key in keys)
        {
            Assert.True(en.ContainsKey(key), key);
            Assert.True(es.ContainsKey(key), key);
            Assert.False(string.IsNullOrWhiteSpace(en[key]));
            Assert.False(string.IsNullOrWhiteSpace(es[key]));
        }

        Loc.Instance.SetLanguage(Language.En);
        Assert.Equal("Advanced · 1 line", Loc.Get("add.advanced.count.one"));
        Assert.Equal("Advanced · 2 lines", Loc.Format("add.advanced.count", 2));
        Loc.Instance.SetLanguage(Language.Es);
        Assert.Equal("Avanzado · 1 línea", Loc.Get("add.advanced.count.one"));
        Assert.Equal("Avanzado · 2 líneas", Loc.Format("add.advanced.count", 2));
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void View_TabOrder_CacheThenExpanderThenLinesThenFooter()
    {
        using var env = new Env();
        var added = env.Add("Tab", "tab.example.com", ["set patchlist patch.example.com"]);
        env.Main.Servers.ReloadFromService();
        var window = new MainWindow(env.Main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        env.Main.Servers.All.Single(s => s.ServerId == added.Id).EditCommand.Execute(null);
        Screenshots.Settle();

        var view = window.GetVisualDescendants().OfType<AddServerView>().Single();
        var cache = view.FindControl<CheckBox>("ClearCacheBox")!;
        var expander = view.FindControl<Expander>("AdvancedExpander")!;
        var lines = view.FindControl<TextBox>("ExtraLinesBox")!;
        var save = view.FindControl<Button>("SaveButton")!;

        Assert.True(expander.IsExpanded);
        // Document order inside the sheet: cache (edit) → expander → lines (when expanded) → footer.
        Assert.True(cache.IsEffectivelyVisible);
        Assert.True(lines.IsEffectivelyVisible);
        var cacheY = cache.TranslatePoint(default, view)!.Value.Y;
        var expanderY = expander.TranslatePoint(default, view)!.Value.Y;
        var linesY = lines.TranslatePoint(default, view)!.Value.Y;
        var saveY = save.TranslatePoint(default, view)!.Value.Y;
        Assert.True(cacheY < expanderY, $"cache {cacheY} vs expander {expanderY}");
        Assert.True(expanderY < linesY, $"expander {expanderY} vs lines {linesY}");
        // The footer now stays outside the scroller. Bring the last field into its viewport first.
        lines.BringIntoView();
        Screenshots.Settle();
        linesY = lines.TranslatePoint(default, view)!.Value.Y;
        saveY = save.TranslatePoint(default, view)!.Value.Y;
        Assert.True(linesY < saveY, $"scrolled lines {linesY} vs fixed save {saveY}");
        window.Close();
    }

    [AvaloniaFact]
    public void View_EnterDoesNotSave_CtrlEnterSaves()
    {
        using var env = new Env();
        env.Main.Servers.RequestAddServerCommand.Execute(null);
        var addForm = Assert.IsType<AddServerViewModel>(env.Main.Content);
        addForm.Name = "Keys";
        addForm.Address = "keys.example.com";
        addForm.SelectedClient = env.Main.Clients.Items.First(c => c.Id == env.ClientId);
        addForm.IsAdvancedExpanded = true;
        addForm.ExtraRealmlistLinesText = "set patchlist patch.example.com";

        var window = new MainWindow(env.Main) { Width = 960, Height = 600 };
        window.Show();
        Screenshots.Settle();
        var view = window.GetVisualDescendants().OfType<AddServerView>().Single();
        var box = view.FindControl<TextBox>("ExtraLinesBox")!;
        box.Focus();
        Screenshots.Settle();

        // Plain Enter must not save (AcceptsReturn + no IsDefault on Save).
        box.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.None,
            Source = box,
        });
        Screenshots.Settle();
        Assert.Empty(env.Session.Service.ListServers(compatibleOnly: false).Value!);

        box.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.Control,
            Source = box,
        });
        Screenshots.Settle();
        Assert.Contains(env.Session.Service.ListServers(compatibleOnly: false).Value!, s => s.Name == "Keys");
        window.Close();
    }

    [AvaloniaFact]
    public void EditForm_StatusTextSaysEditServerNotSaved()
    {
        using var env = new Env();
        var added = env.Add("Status", "status.example.com");
        var form = new AddServerViewModel(env.Main.Clients, env.Session, env.Reload(added.Id), () => { }, () => { });
        Assert.Equal(Loc.Get("status.edit"), form.StatusText);
        Loc.Instance.SetLanguage(Language.Es);
        form.RefreshText();
        Assert.Equal(Loc.Get("status.edit"), form.StatusText);
        Assert.Equal("Editar servidor · sin guardar", form.StatusText);
        Loc.Instance.SetLanguage(Language.En);
        form.RefreshText();
        Assert.Equal("Edit server · not saved", form.StatusText);
    }

    [AvaloniaFact]
    public void AddForm_StatusTextSaysAddServerNotSaved()
    {
        using var env = new Env();
        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        Assert.Equal(Loc.Get("status.add"), form.StatusText);
    }

    [AvaloniaFact]
    public void FormatExtraLineErrors_PrefixesLineAndSummarizesMore()
    {
        Loc.Instance.SetLanguage(Language.En);
        var one = AddServerViewModel.FormatExtraLineErrors([(2, AddServerViewModel.FormatRealmlistKind(InvalidRealmlistKind.MalformedLine))]);
        Assert.StartsWith("Line 2:", one);
        Assert.DoesNotContain("more", one, StringComparison.OrdinalIgnoreCase);

        var many = AddServerViewModel.FormatExtraLineErrors([
            (1, AddServerViewModel.FormatRealmlistKind(InvalidRealmlistKind.ForbiddenKey)),
            (2, AddServerViewModel.FormatRealmlistKind(InvalidRealmlistKind.DuplicateKey)),
            (3, AddServerViewModel.FormatRealmlistKind(InvalidRealmlistKind.InvalidValue)),
        ]);
        Assert.StartsWith("Line 1:", many);
        Assert.EndsWith(" and 2 more", many);

        Loc.Instance.SetLanguage(Language.Es);
        var es = AddServerViewModel.FormatExtraLineErrors([
            (1, AddServerViewModel.FormatRealmlistKind(InvalidRealmlistKind.MalformedLine)),
            (2, AddServerViewModel.FormatRealmlistKind(InvalidRealmlistKind.DuplicateKey)),
        ]);
        Assert.StartsWith("Línea 1:", es);
        Assert.EndsWith(" y 1 más", es);
        Assert.DoesNotContain("must have the form", es, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("appears more than once", es, StringComparison.OrdinalIgnoreCase);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void Save_CoreValidation_UsesLocalizedLinePrefix_NotRawEnglishReason()
    {
        using var env = new Env();
        Loc.Instance.SetLanguage(Language.Es);
        var form = new AddServerViewModel(env.Main.Clients, env.Session, () => { }, () => { });
        form.Name = "BadCore";
        form.Address = "logon.example.com";
        form.SelectedClient = env.Main.Clients.Items.First(c => c.Id == env.ClientId);
        form.ExtraRealmlistLinesText = "not a set line";
        form.SaveCommand.Execute(null);
        Assert.True(form.HasExtraLinesError);
        Assert.StartsWith("Línea 1:", form.ExtraLinesError);
        Assert.DoesNotContain("must have the form", form.ExtraLinesError!, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid extra realmlist line:", form.ExtraLinesError!, StringComparison.Ordinal);
        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaFact]
    public void UntouchedExtraLines_AreKeptOnEdit()
    {
        using var env = new Env();
        var added = env.Add("Keep", "keep.example.com", ["set patchlist patch.example.com"]);
        var form = new AddServerViewModel(env.Main.Clients, env.Session, env.Reload(added.Id), () => { }, () => { });
        form.Name = "Keep renamed";
        form.SaveCommand.Execute(null);
        Assert.False(form.HasSaveError);
        Assert.Equal(["set patchlist patch.example.com"], env.Reload(added.Id).ExtraRealmlistLines);
    }
}
