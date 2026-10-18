using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Demo;
using RealmPortal.App.Localization;
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
    public void ShowsTheSavedValuesAndHasNoCacheOption()
    {
        var (shell, settings) = Open();
        var texts = shell.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty).ToList();
        Assert.Contains("Ajustes", texts);
        Assert.Contains("Lanzador", texts);
        Assert.DoesNotContain(texts, t => t.Contains("caché", StringComparison.OrdinalIgnoreCase) || t.Contains("cache", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("wine {exe}", settings.Template);
        Assert.Equal("gamemoderun", settings.Prefix);
        Assert.Equal("-windowed -nosound", settings.Arguments);
        Assert.Contains("WINEPREFIX=/home/demo/.wine", settings.Environment);
        Assert.False(settings.IsDirty);
        Assert.Equal("Ajustes", shell.Model.StatusText);
        shell.Snap("settings");
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
    public async Task BrowseFillsTheBackupFolderAndBackReturnsToServers()
    {
        var (shell, settings) = Open();
        await settings.BrowseCommand.ExecuteAsync(null);
        Assert.EndsWith("backups-2", settings.BackupFolder);
        Assert.True(settings.IsDirty);
        settings.BackCommand.Execute(null);
        Assert.Equal(0, shell.Model.SectionIndex);
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
