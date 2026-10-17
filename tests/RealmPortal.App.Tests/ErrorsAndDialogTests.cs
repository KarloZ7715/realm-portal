using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;

namespace RealmPortal.App.Tests;

public sealed class ErrorsAndDialogTests
{
    /// <summary>Codes the core announced for 0.1 that are not in the enum yet; their texts are ready in both languages.</summary>
    private static readonly string[] AnnouncedCodes = ["ProcessCheckFailed"];

    public static TheoryData<string> AllCodes()
    {
        var data = new TheoryData<string>();
        foreach (var code in Enum.GetNames<ClientFilesErrorCode>().Concat(AnnouncedCodes).Distinct())
        {
            data.Add(code);
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(AllCodes))]
    public void EveryErrorCodeHasTitleAndBodyInBothLanguages(string code)
    {
        foreach (var language in new[] { Language.Es, Language.En })
        {
            Loc.Instance.SetLanguage(language);
            Assert.DoesNotContain("!error.", ErrorText.Title(code));
            Assert.DoesNotContain("!error.", ErrorText.Body(code, "/some/path"));
            Assert.NotEqual(ErrorText.Title("NoSuchCode"), ErrorText.Title(code));
        }

        Loc.Instance.SetLanguage(Language.Es);
    }

    [AvaloniaFact]
    public void PathAndLanguageGoIntoTheBody()
    {
        Loc.Instance.SetLanguage(Language.En);
        Assert.Contains("/games/wow", ErrorText.Body(ClientFilesErrorCode.PermissionDenied, "/games/wow"));
        Loc.Instance.SetLanguage(Language.Es);
        Assert.Contains("/games/wow", ErrorText.Body(ClientFilesErrorCode.PermissionDenied, "/games/wow"));
        Assert.Equal("El juego está abierto", ErrorText.Title(ClientFilesErrorCode.GameRunning));
    }

    [AvaloniaFact]
    public void UnknownCodeFallsBackToTheGenericMessage()
    {
        Loc.Instance.SetLanguage(Language.En);
        Assert.Equal("Something went wrong", ErrorText.Title("FutureCode"));
        Loc.Instance.SetLanguage(Language.Es);
    }

    [AvaloniaFact]
    public async Task DiscardDialogCancelIsDefaultAndKeepsFocus()
    {
        var shell = new Shell();
        var dialog = ConfirmDialogViewModel.DiscardBackup();
        var answer = shell.Model.ConfirmAsync(dialog);
        Screenshots.Settle();

        var host = shell.Window.FindControl<ContentControl>("DialogHost")!;
        Assert.True(host.IsVisible);
        var cancel = host.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        Assert.True(cancel.IsCancel);
        Assert.True(cancel.IsFocused);
        Assert.Contains("Se conservarán los archivos actuales", host.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DialogBody").Text);
        shell.Snap("dialog-discard", 1.0, 2.0);

        // Enter on the focused button means Cancel.
        cancel.Command!.Execute(null);
        Assert.False(await answer);
        Screenshots.Settle();
        Assert.False(host.IsVisible);
        Assert.Null(shell.Model.Dialog);
    }

    [AvaloniaFact]
    public async Task DiscardDialogConfirmAnswersTrueAndFollowsTheLanguage()
    {
        var shell = new Shell();
        var answer = shell.Model.ConfirmAsync(ConfirmDialogViewModel.DiscardBackup());
        Screenshots.Settle();
        Loc.Instance.SetLanguage(Language.En);
        Assert.Equal("Discard", shell.Model.Dialog!.ConfirmText);
        Assert.Contains("you will no longer be able to undo", shell.Model.Dialog.Body);
        shell.Model.Dialog.ConfirmCommand.Execute(null);
        Assert.True(await answer);
        Loc.Instance.SetLanguage(Language.Es);
    }

    [AvaloniaFact]
    public async Task OnlyOneDialogAtATime()
    {
        var shell = new Shell();
        var first = shell.Model.ConfirmAsync(ConfirmDialogViewModel.DiscardBackup());
        Assert.False(await shell.Model.ConfirmAsync(ConfirmDialogViewModel.DiscardBackup()));
        shell.Model.Dialog!.CancelCommand.Execute(null);
        Assert.False(await first);
    }

    [AvaloniaFact]
    public void FlatPopupHoverShowsTheWaxBar()
    {
        var shell = new Shell();
        var combo = new ComboBox { ItemsSource = new[] { "ES", "EN" }, SelectedIndex = 0, Width = 200 };
        shell.Window.Content = new StackPanel { Children = { combo } };
        Screenshots.Settle();
        combo.IsDropDownOpen = true;
        Screenshots.Settle();
        var popup = combo.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First();
        var item = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().First();
        var bar = item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Bar");
        Assert.Equal(3, bar.Width);
        Assert.Equal(0, bar.Opacity); // selected: fill only
        ((Avalonia.Controls.IPseudoClasses)item.Classes).Set(":pointerover", true);
        Screenshots.Settle();
        Assert.Equal(1, bar.Opacity); // hovered: fill + bar
        combo.IsDropDownOpen = false;
    }
}
