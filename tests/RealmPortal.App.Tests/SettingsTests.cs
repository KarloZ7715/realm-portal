using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
using RealmPortal.App.Tests.Support;
using TempDirectory = RealmPortal.Tests.Support.TempDirectory;
using RealmPortal.App.Settings;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Tests;

public class LauncherTextTests
{
    [Theory]
    [InlineData("-windowed -nosound", new[] { "-windowed", "-nosound" })]
    [InlineData("  a   b ", new[] { "a", "b" })]
    [InlineData("--name \"My Realm\" 'x y'", new[] { "--name", "My Realm", "x y" })]
    [InlineData("a\\ b c", new[] { "a b", "c" })]
    [InlineData("", new string[0])]
    public void TokensSplitLikeAShellWithoutRunningOne(string text, string[] expected) =>
        Assert.Equal(expected, LauncherText.Tokens(text));

    [Fact]
    public void JoinThenTokensRoundTrips()
    {
        string[] tokens = ["-a", "b c", "d\"e", "", "f\\g"];
        Assert.Equal(tokens, LauncherText.Tokens(LauncherText.Join(tokens)));
    }

    [Fact]
    public void EnvironmentParsesLinesAndPointsAtTheBadOne()
    {
        Assert.Null(LauncherText.TryParseEnvironment("A=1\n\nB=x=y\r\nC=", out var env));
        Assert.Equal("1", env["A"]);
        Assert.Equal("x=y", env["B"]);
        Assert.Equal("", env["C"]);
        Assert.Equal(2, LauncherText.TryParseEnvironment("A=1\nnonsense", out _));
        Assert.Equal(1, LauncherText.TryParseEnvironment("=x", out _));
    }
}

public class SettingsScreenTests
{
    private static (Shell Shell, SettingsViewModel Settings) Open()
    {
        var shell = new Shell();
        shell.Model.SectionIndex = 2;
        Screenshots.Settle();
        return (shell, shell.Model.Settings);
    }

    [AvaloniaFact]
    public void ShowsTheSavedValuesAndCacheToggleOffByDefault()
    {
        var (shell, settings) = Open();
        var texts = shell.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("Ajustes", texts);
        Assert.Contains("Lanzador", texts);
        Assert.Contains(texts, t => t.Contains("Caché", StringComparison.OrdinalIgnoreCase) || t.Contains("Cache", StringComparison.OrdinalIgnoreCase));
        Assert.False(settings.ClearCacheOnSwitch);
        Assert.Equal("wine {exe}", settings.Template);
        Assert.Equal("gamemoderun", settings.Prefix);
        Assert.Equal("-windowed -nosound", settings.Arguments);
        Assert.Contains("WINEPREFIX=/home/demo/.wine", settings.Environment);
        Assert.False(settings.IsDirty);
        Assert.Equal("Ajustes", shell.Model.StatusText);
        shell.Snap("settings");
    }

    [AvaloniaFact]
    public void EmptyLauncherFieldsShowLocalizedExampleHints()
    {
        // Tall enough to show the whole launcher card in the screenshots.
        var shell = new Shell(720, 760);
        shell.Model.SectionIndex = 2;
        var settings = shell.Model.Settings;
        try
        {
            settings.Template = "";
            settings.Prefix = "";
            settings.Arguments = "";
            settings.Environment = "";
            Screenshots.Settle();

            TextBox Box(string name) => shell.Window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == name);
            string[] boxes = ["TemplateBox", "PrefixBox", "ArgumentsBox", "EnvironmentBox"];
            Assert.All(boxes, name => Assert.StartsWith("ej.: ", Box(name).Watermark));
            shell.Snap("settings-hints-es", 1.0, 2.0);

            Loc.Instance.SetLanguage(Language.En);
            Screenshots.Settle();
            Assert.All(boxes, name => Assert.StartsWith("e.g. ", Box(name).Watermark));
            Assert.Equal("e.g. gamemoderun", Box("PrefixBox").Watermark);

            // The hint uses the body face, the typed value the mono face, so the two never look alike.
            var prefix = Box("PrefixBox");
            var hint = prefix.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Watermark");
            Assert.True(hint.IsVisible);
            Assert.NotEqual(prefix.FontFamily, hint.FontFamily);
            shell.Snap("settings-hints-en", 1.0);
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.Es);
            shell.Window.Close();
        }
    }

    [AvaloniaFact]
    public void EditingMarksUnsavedAndSaveClearsIt()
    {
        var (shell, settings) = Open();
        settings.Arguments = "-windowed";
        Assert.True(settings.IsDirty);
        Assert.Equal("Ajustes · cambios sin guardar", shell.Model.StatusText);
        Assert.True(settings.SaveCommand.CanExecute(null));
        shell.Snap("settings-dirty", 1.0);

        settings.SaveCommand.Execute(null);
        Assert.False(settings.IsDirty);
        Assert.Equal("Ajustes · guardado", shell.Model.StatusText);
        Assert.False(settings.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CancelPutsTheSavedValuesBack()
    {
        var (_, settings) = Open();
        settings.Template = "other {exe}";
        settings.BackupFolder = "/elsewhere";
        settings.CancelCommand.Execute(null);
        Assert.Equal("wine {exe}", settings.Template);
        Assert.Equal("/home/demo/.local/share/RealmPortal/backups", settings.BackupFolder);
        Assert.False(settings.IsDirty);
    }

    [AvaloniaFact]
    public void TemplateWithoutExeIsRefusedByTheStoreAndShownOnTheField()
    {
        var (shell, settings) = Open();
        settings.Template = "wine";
        settings.SaveCommand.Execute(null);
        Assert.Equal("La plantilla debe incluir {exe}.", settings.TemplateError);
        Assert.True(settings.IsDirty);
        Screenshots.Settle();
        var error = shell.Window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "TemplateError");
        Assert.True(error.IsVisible);
        shell.Snap("settings-error", 1.0);
    }

    [AvaloniaFact]
    public void MalformedEnvironmentLineBlocksSaveWithItsNumber()
    {
        var (_, settings) = Open();
        settings.Environment = "A=1\noops";
        Assert.Equal("Línea 2: usa el formato CLAVE=valor.", settings.EnvironmentError);
        settings.SaveCommand.Execute(null);
        Assert.True(settings.IsDirty);
    }

    [AvaloniaFact]
    public void LanguageRadioSwitchesTheUiAndCancelRestoresIt()
    {
        var (shell, settings) = Open();
        var english = shell.Window.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Name == "EnglishOption");
        english.IsChecked = true;
        Screenshots.Settle();
        Assert.Equal(Language.En, Loc.Instance.Current);
        Assert.True(settings.IsDirty);
        Assert.Equal("Settings · unsaved changes", shell.Model.StatusText);
        Assert.Equal("Launcher", shell.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).First(t => t == "Launcher"));

        settings.CancelCommand.Execute(null);
        Screenshots.Settle();
        Assert.Equal(Language.Es, Loc.Instance.Current);
        Assert.True(shell.Window.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Name == "SpanishOption").IsChecked);
    }

    [AvaloniaFact]
    public void StatusBarToggleKeepsTheRadiosInSync()
    {
        var (shell, settings) = Open();
        shell.Model.ToggleLanguageCommand.Execute(null);
        Screenshots.Settle();
        Assert.True(settings.IsEnglish);
        Assert.False(settings.IsSpanish);
        Loc.Instance.SetLanguage(Language.Es);
    }

    [AvaloniaFact]
    public async Task BrowseFillsTheBackupFolder()
    {
        var (_, settings) = Open();
        await settings.BrowseCommand.ExecuteAsync(null);
        Assert.EndsWith("backups-2", settings.BackupFolder);
        Assert.True(settings.IsDirty);
    }

    [AvaloniaFact]
    public void HasNoTopBackButton()
    {
        // The sidebar and the Cancel/Save footer already leave the screen (#100).
        var (shell, _) = Open();
        var view = shell.Window.GetVisualDescendants().OfType<RealmPortal.App.Views.SettingsView>().Single();
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.Name == "BackButton");
    }

    [AvaloniaFact]
    public void LeavingWithoutChangesDoesNotAsk()
    {
        var (shell, _) = Open();
        shell.Model.SectionIndex = 0;
        Assert.Equal(0, shell.Model.SectionIndex);
        Assert.Null(shell.Model.Dialog);
    }

    [AvaloniaFact]
    public void LeavingWithUnsavedChanges_AsksAndCancelKeepsTheEdits()
    {
        var (shell, settings) = Open();
        settings.Prefix = "mangohud";
        var nav = shell.Window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "Nav");

        nav.SelectedIndex = 0;
        Screenshots.Settle();

        var dialog = Assert.IsType<ConfirmDialogViewModel>(shell.Model.Dialog);
        Assert.Equal("¿Descartar los cambios sin guardar?", dialog.Title);
        Assert.Equal("Seguir editando", dialog.CancelText);
        Assert.Equal("Descartar cambios", dialog.ConfirmText);
        Assert.Equal(2, shell.Model.SectionIndex);
        Assert.Equal(2, nav.SelectedIndex);
        Assert.Same(settings, shell.Model.Content);
        shell.Snap("settings-leave-confirm", 1.0, 2.0);

        dialog.CancelCommand.Execute(null);
        Screenshots.Settle();
        Assert.Null(shell.Model.Dialog);
        Assert.Equal(2, shell.Model.SectionIndex);
        Assert.Equal("mangohud", settings.Prefix);
        Assert.True(settings.IsDirty);
    }

    [AvaloniaFact]
    public void StatusBarToggle_IsSavedRightAwayAndIsNotAnUnsavedSettingsChange()
    {
        using var env = new IsolatedAppSession(languageCode: "es");
        env.Session.UiPreferences.SetLanguage(Language.Es);
        var main = new MainViewModel(env.Session, autoTick: false);
        try
        {
            // Without entering Settings: the toggle must not make Settings dirty (closing would ask otherwise).
            main.ToggleLanguageCommand.Execute(null);
            Assert.Equal("en", env.Session.UiPreferences.Load().LanguageCode);
            Assert.False(main.Settings.IsDirty);

            // Inside Settings with a pending edit: the edit stays pending, the language does not become one.
            main.SectionIndex = 2;
            main.Settings.Prefix = "mangohud";
            main.ToggleLanguageCommand.Execute(null);
            Assert.True(main.Settings.IsDirty);

            main.SectionIndex = 0;
            main.Dialog!.ConfirmCommand.Execute(null);
            Screenshots.Settle();

            Assert.Null(main.Dialog);
            Assert.Equal(0, main.SectionIndex);
            Assert.False(main.Settings.IsDirty);
            Assert.Equal(Language.Es, Loc.Instance.Current);
            Assert.Equal("es", env.Session.UiPreferences.Load().LanguageCode);
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.Es);
        }
    }

    [AvaloniaFact]
    public void ClosingAfterStatusBarToggleDoesNotAsk()
    {
        var shell = new Shell();
        try
        {
            shell.Model.ToggleLanguageCommand.Execute(null);
            Screenshots.Settle();
            var closed = false;
            shell.Window.Closed += (_, _) => closed = true;
            shell.Window.Close();
            Screenshots.Settle();
            Assert.Null(shell.Model.Dialog);
            Assert.True(closed);
        }
        finally
        {
            Loc.Instance.SetLanguage(Language.Es);
        }
    }

    [AvaloniaFact]
    public void ClosingWithoutChangesClosesRightAway()
    {
        var (shell, _) = Open();
        var closed = false;
        shell.Window.Closed += (_, _) => closed = true;
        shell.Window.Close();
        Screenshots.Settle();
        Assert.True(closed);
        Assert.Null(shell.Model.Dialog);
    }

    [AvaloniaFact]
    public void ClosingWithUnsavedChanges_AsksKeepEditingStaysOpenDiscardCloses()
    {
        var (shell, settings) = Open();
        var closed = false;
        shell.Window.Closed += (_, _) => closed = true;
        settings.Prefix = "mangohud";

        shell.Window.Close();
        Screenshots.Settle();
        var dialog = Assert.IsType<ConfirmDialogViewModel>(shell.Model.Dialog);
        Assert.Equal("Seguir editando", dialog.CancelText);
        Assert.False(closed);
        shell.Snap("settings-close-confirm", 1.0);

        dialog.CancelCommand.Execute(null);
        Screenshots.Settle();
        Assert.False(closed);
        Assert.Equal("mangohud", settings.Prefix);

        shell.Window.Close();
        Screenshots.Settle();
        shell.Model.Dialog!.ConfirmCommand.Execute(null);
        Screenshots.Settle();
        Assert.True(closed);
        Assert.False(settings.IsDirty);
    }

    [AvaloniaFact]
    public void LeavingWithUnsavedChanges_DiscardRevertsAndLeaves()
    {
        var (shell, settings) = Open();
        settings.Prefix = "mangohud";
        settings.IsEnglish = true;
        Assert.Equal(Language.En, Loc.Instance.Current);

        shell.Model.SectionIndex = 1;
        Screenshots.Settle();
        var dialog = Assert.IsType<ConfirmDialogViewModel>(shell.Model.Dialog);
        dialog.ConfirmCommand.Execute(null);
        Screenshots.Settle();

        Assert.Equal(1, shell.Model.SectionIndex);
        Assert.False(settings.IsDirty);
        Assert.Equal("gamemoderun", settings.Prefix);
        Assert.Equal(Language.Es, Loc.Instance.Current);
    }

    [AvaloniaFact]
    public void SavedSettingsSurviveLeavingTheScreen()
    {
        var (shell, settings) = Open();
        settings.Prefix = "gamemoderun mangohud";
        settings.SaveCommand.Execute(null);
        shell.Model.SectionIndex = 0;
        shell.Model.SectionIndex = 2;
        Assert.Equal("gamemoderun mangohud", shell.Model.Settings.Prefix);
    }
}
