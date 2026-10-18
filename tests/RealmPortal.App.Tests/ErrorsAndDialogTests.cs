using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RealmPortal.App.Localization;
using RealmPortal.App.ViewModels;
using RealmPortal.Core.ClientFiles;
using RealmPortal.Core.Service;

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
        var titleKey = $"error.{code.ToLowerInvariant()}.title";
        var bodyKey = $"error.{code.ToLowerInvariant()}.body";
        foreach (var language in new[] { Language.Es, Language.En })
        {
            var table = Loc.Load(language);
            Assert.True(table.ContainsKey(titleKey), $"Missing {titleKey} in {language}");
            Assert.True(table.ContainsKey(bodyKey), $"Missing {bodyKey} in {language}");
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
    public void ProcessCheckFailedBodyOmitsTheClientPathAndDiagnostic()
    {
        var error = new ServiceError(
            ServiceErrorCode.ProcessCheckFailed,
            Path: "/games/wow-secret",
            Diagnostic: "probe boom");

        Loc.Instance.SetLanguage(Language.En);
        var english = ErrorText.Body(error);
        Assert.Equal("The process list could not be read. Nothing was changed.", english);
        Assert.DoesNotContain("/games/wow-secret", english);
        Assert.DoesNotContain("probe boom", english);
        Assert.DoesNotContain("{0}", english);

        Loc.Instance.SetLanguage(Language.Es);
        var spanish = ErrorText.Body(error);
        Assert.Equal("No se pudo leer la lista de procesos. No se ha modificado nada.", spanish);
        Assert.DoesNotContain("/games/wow-secret", spanish);
        Assert.DoesNotContain("probe boom", spanish);
        Assert.DoesNotContain("{0}", spanish);
    }

    [AvaloniaFact]
    public void ServiceErrorBodiesNeverIncludeDiagnostics()
    {
        const string diagnostic = "SECRET_DIAGNOSTIC_do_not_show";
        var unexpected = new ServiceError(ServiceErrorCode.Unexpected, Diagnostic: diagnostic);
        var inconsistent = new ServiceError(
            ServiceErrorCode.InconsistentState,
            Consistency: Consistency.Inconsistent,
            Diagnostic: diagnostic);
        var launch = new ServiceError(ServiceErrorCode.LaunchFailed, Diagnostic: diagnostic);
        var applied = new ServiceError(
            ServiceErrorCode.LaunchFailed,
            Consistency: Consistency.AppliedLaunchFailed,
            Diagnostic: diagnostic);

        Loc.Instance.SetLanguage(Language.En);
        Assert.Equal("Try again. Nothing was changed.", ErrorText.Body(unexpected));
        Assert.Equal(
            "Realm Portal could not put everything back. Review the client files.",
            ErrorText.Body(inconsistent));
        Assert.Equal(
            "The game could not be started. Check the launcher settings and try again. Nothing was changed.",
            ErrorText.Body(launch));
        Assert.Equal(
            "The server was applied and is now active, but the game didn't start. Check the launcher settings and try again.",
            ErrorText.Body(applied));

        Loc.Instance.SetLanguage(Language.Es);
        Assert.Equal("Vuelve a intentarlo. No se ha modificado nada.", ErrorText.Body(unexpected));
        Assert.Equal(
            "Realm Portal no pudo dejar todo como estaba. Revisa los archivos del cliente.",
            ErrorText.Body(inconsistent));
        Assert.Equal(
            "No se pudo iniciar el juego. Revisa los ajustes del lanzador e inténtalo de nuevo. No se ha modificado nada.",
            ErrorText.Body(launch));
        Assert.Equal(
            "El servidor quedó aplicado y activo, pero el juego no se abrió. Revisa los ajustes del lanzador e inténtalo de nuevo.",
            ErrorText.Body(applied));

        foreach (var body in new[]
        {
            ErrorText.Body(unexpected),
            ErrorText.Body(inconsistent),
            ErrorText.Body(launch),
            ErrorText.Body(applied),
        })
        {
            Assert.DoesNotContain(diagnostic, body);
        }

        Loc.Instance.SetLanguage(Language.En);
    }

    [AvaloniaTheory]
    [InlineData(Language.En, Consistency.Unchanged, "Nothing was changed.")]
    [InlineData(Language.Es, Consistency.Unchanged, "No se ha modificado nada.")]
    [InlineData(Language.En, Consistency.RolledBack, "Changes were rolled back; nothing was left half-done.")]
    [InlineData(Language.Es, Consistency.RolledBack, "Los cambios se revirtieron; no quedó nada a medias.")]
    public void PermissionDenied_ReassuranceFollowsConsistency(Language language, Consistency consistency, string sentence)
    {
        var original = Loc.Instance.Current;
        try
        {
            Loc.Instance.SetLanguage(language);
            var body = ErrorText.Body(new ServiceError(
                ServiceErrorCode.PermissionDenied,
                Path: "/games/wow",
                Consistency: consistency));
            Assert.Contains("/games/wow", body);
            Assert.Contains(sentence, body);
            if (consistency != Consistency.Unchanged)
            {
                Assert.DoesNotContain("Nothing was changed", body);
                Assert.DoesNotContain("No se ha modificado nada", body);
            }
        }
        finally
        {
            Loc.Instance.SetLanguage(original);
        }
    }

    [AvaloniaTheory]
    [InlineData(Language.En, Consistency.Inconsistent, "Review the client files.")]
    [InlineData(Language.Es, Consistency.Inconsistent, "Revisa los archivos del cliente.")]
    [InlineData(Language.En, Consistency.Pending, "Some files may have changed.")]
    [InlineData(Language.Es, Consistency.Pending, "Puede que algunos archivos hayan cambiado.")]
    public void PermissionDenied_InconsistentOrUnknownState_DoesNotClaimNothingChanged(
        Language language,
        Consistency consistency,
        string sentence)
    {
        // Consistency is a non-nullable enum, so a missing value cannot be represented.
        // Inconsistent keeps the inconsistent-state guidance. Pending (and any other value) uses the unknown-state sentence.
        var original = Loc.Instance.Current;
        try
        {
            Loc.Instance.SetLanguage(language);
            var body = ErrorText.Body(new ServiceError(
                ServiceErrorCode.PermissionDenied,
                Path: "/games/wow",
                Consistency: consistency));
            Assert.Contains(sentence, body);
            Assert.DoesNotContain("Nothing was changed", body);
            Assert.DoesNotContain("No se ha modificado nada", body);
        }
        finally
        {
            Loc.Instance.SetLanguage(original);
        }
    }

    [AvaloniaFact]
    public void ClientFilesPreCheck_KeepsUnchangedOnlyWhenNoWriteHappened()
    {
        var original = Loc.Instance.Current;
        try
        {
            Loc.Instance.SetLanguage(Language.En);
            var preCheck = ErrorText.Body(ClientFilesErrorCode.GameRunning);
            Assert.Contains("Nothing was changed.", preCheck);
            Assert.DoesNotContain("Some files may have changed", preCheck);

            var afterWrite = ErrorText.Body(ClientFilesErrorCode.VerifyFailed, "/games/wow");
            Assert.Contains("Some files may have changed.", afterWrite);
            Assert.DoesNotContain("Nothing was changed", afterWrite);

            var denied = ErrorText.Body(ClientFilesErrorCode.PermissionDenied, "/games/wow");
            Assert.Contains("/games/wow", denied);
            Assert.Contains("Some files may have changed.", denied);
            Assert.DoesNotContain("Nothing was changed", denied);

            Loc.Instance.SetLanguage(Language.Es);
            Assert.Contains("No se ha modificado nada.", ErrorText.Body(ClientFilesErrorCode.NothingToUndo));
            var spanishWrite = ErrorText.Body(ClientFilesErrorCode.PermissionDenied, "/games/wow");
            Assert.Contains("/games/wow", spanishWrite);
            Assert.Contains("Puede que algunos archivos hayan cambiado.", spanishWrite);
            Assert.DoesNotContain("No se ha modificado nada", spanishWrite);
        }
        finally
        {
            Loc.Instance.SetLanguage(original);
        }
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
        var rejected = ConfirmDialogViewModel.DiscardBackup();
        Assert.False(await shell.Model.ConfirmAsync(rejected));
        Assert.True(rejected.Result.IsCompletedSuccessfully);
        shell.Model.Dialog!.CancelCommand.Execute(null);
        Assert.False(await first);
    }

    [AvaloniaFact]
    public async Task DiscardDialogTrapsFocusAndRestoresIt()
    {
        var shell = new Shell();
        var language = shell.Window.FindControl<Button>("LanguageButton")!;
        language.Focus();
        Screenshots.Settle();
        Assert.True(language.IsFocused);

        var answer = shell.Model.ConfirmAsync(ConfirmDialogViewModel.DiscardBackup());
        Screenshots.Settle();

        var mainChrome = shell.Window.FindControl<Panel>("MainChrome")!;
        Assert.False(mainChrome.IsEnabled);

        var host = shell.Window.FindControl<ContentControl>("DialogHost")!;
        var cancel = host.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        var confirm = host.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmButton");
        Assert.True(cancel.IsFocused);

        confirm.Focus();
        Screenshots.Settle();
        Assert.True(confirm.IsFocused);
        cancel.Focus();
        Assert.True(cancel.IsFocused);

        cancel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(await answer);
        Screenshots.Settle();
        Assert.Null(shell.Model.Dialog);
        Assert.True(mainChrome.IsEnabled);
        Screenshots.Settle();
        Assert.True(language.IsFocused);
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
        var items = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>().ToList();
        var selected = items[0];
        var hovered = items[1];
        var selectedBar = selected.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Bar");
        Assert.Equal(3, selectedBar.Width);
        Assert.Equal(0, selectedBar.Opacity); // selected: fill only, no bar
        var bar = hovered.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Bar");
        ((Avalonia.Controls.IPseudoClasses)hovered.Classes).Set(":pointerover", true);
        Screenshots.Settle();
        Assert.Equal(1, bar.Opacity); // unselected + hovered: fill + bar
        combo.IsDropDownOpen = false;
    }
}
