using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.Platform;
using RealmPortal.App.Tests.Support;
using RealmPortal.App.ViewModels;
using RealmPortal.Core;

namespace RealmPortal.App.Tests;

public class AboutTests
{
    private sealed class RecordingUriOpener : IUriOpener
    {
        public Uri? Last { get; private set; }
        public bool Succeed { get; set; } = true;

        public Task<bool> OpenAsync(Uri uri)
        {
            Last = uri;
            return Task.FromResult(Succeed);
        }
    }

    private static Stream LicensesStream(string text = "# Licenses\nTest") =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void AboutShowsAppInfoVersionAndName()
    {
        var about = new AboutViewModel(new RecordingUriOpener(), () => { }, openThirdPartyLicenses: () => LicensesStream());
        Assert.Equal(AppInfo.Name, about.AppName);
        Assert.Equal(AppInfo.Version, about.Version);
        Assert.Equal(AppInfo.RepositoryUrl, about.RepositoryUrl);
        Assert.True(about.HasThirdPartyLicenses);
        Assert.Contains("Licenses", about.ThirdPartyLicenses);
    }

    [AvaloniaFact]
    public async Task OpenRepositoryUsesTheUriOpener()
    {
        var opener = new RecordingUriOpener();
        var about = new AboutViewModel(opener, () => { }, openThirdPartyLicenses: () => LicensesStream("x"));
        await about.OpenRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(new Uri(AppInfo.RepositoryUrl), opener.Last);
        Assert.Null(about.OpenError);
        Assert.Contains(AppInfo.Version, about.VersionLabel);
    }

    [AvaloniaFact]
    public async Task OpenRepositorySurfacesALocalizedErrorWhenTheOpenerFails()
    {
        Loc.Instance.SetLanguage(Language.En);
        var opener = new RecordingUriOpener { Succeed = false };
        var about = new AboutViewModel(opener, () => { }, openThirdPartyLicenses: () => LicensesStream("x"));
        await about.OpenRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Get("about.open.failed"), about.OpenError);
    }

    [Fact]
    public async Task ShellUriOpenerRejectsNonHttpSchemes() =>
        Assert.False(await new ShellUriOpener().OpenAsync(new Uri("file:///tmp/x")));

    [AvaloniaFact]
    public async Task SettingsOpensTheAboutSheetWithMatchingVersion()
    {
        var opener = new RecordingUriOpener();
        var shell = new Shell(uriOpener: opener);
        shell.Model.SectionIndex = 2;
        Screenshots.Settle();

        Assert.True(shell.Model.Settings.OpenAboutCommand.CanExecute(null));
        shell.Model.Settings.OpenAboutCommand.Execute(null);
        // ShowAbout loads licenses off-thread before swapping Content.
        for (var i = 0; i < 50 && shell.Model.Content is not AboutViewModel; i++)
        {
            await Task.Delay(20);
            Screenshots.Settle();
        }

        Assert.IsType<AboutViewModel>(shell.Model.Content);
        var about = (AboutViewModel)shell.Model.Content;
        Assert.Equal(AppInfo.Version, about.Version);
        Assert.Equal($"ES · v{AppInfo.Version}", shell.Model.LanguageLabel);
        Assert.True(about.HasThirdPartyLicenses);

        var texts = shell.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(AppInfo.Name, texts);
        Assert.Contains(texts, t => t.Contains(AppInfo.Version, StringComparison.Ordinal));

        await about.OpenRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(new Uri(AppInfo.RepositoryUrl), opener.Last);

        about.BackCommand.Execute(null);
        Screenshots.Settle();
        Assert.Same(shell.Model.Settings, shell.Model.Content);
    }

    [AvaloniaFact]
    public void AboutStringsExistInBothLanguages()
    {
        foreach (var key in new[]
                 {
                     "settings.about", "settings.about.open", "settings.about.hint",
                     "about.title", "about.back", "about.version", "about.credits", "about.copyright",
                     "about.disclaimer", "about.mit", "about.github", "about.thirdparty", "about.open.failed",
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(Loc.Load(Language.En)[key]));
            Assert.False(string.IsNullOrWhiteSpace(Loc.Load(Language.Es)[key]));
        }
    }
}
