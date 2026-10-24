using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RealmPortal.App.Controls;
using RealmPortal.App.Demo;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Tests;

public class ClientsScreenTests
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
    public async Task ClientsScreenListsTheDetectedClients()
    {
        var shell = new Shell();
        shell.Model.SectionIndex = 1;
        await Pump();
        var clients = shell.Model.Clients;
        Assert.Same(clients, shell.Model.Content);
        Assert.Equal("3 clientes encontrados · 2 en uso. Solo leemos las rutas.", clients.Summary);
        Assert.Equal("Clientes · 3 detectados", shell.Model.StatusText);
        Assert.Equal(["Cliente 3.3.5a (12340)", "Cliente 1.12.1 (5875)", "Cliente 3.3.5a (12340)"], clients.Items.Select(i => i.Title));
        Assert.Equal(["Lutris", "Wine", "Steam"], clients.Items.Select(i => i.OriginText));
        Assert.All(clients.Items, i => Assert.StartsWith("/home/demo/", i.Path));
        Assert.Equal(3, shell.Window.GetVisualDescendants().OfType<CheckBox>().Count());
        shell.Snap("clients-list");
    }

    [AvaloniaFact]
    public async Task TickingARowUpdatesTheSummary()
    {
        var shell = new Shell();
        shell.Model.SectionIndex = 1;
        await Pump();
        var box = shell.Window.GetVisualDescendants().OfType<CheckBox>().Last();
        Assert.False(box.IsChecked);
        box.IsChecked = true;
        await Pump();
        Assert.True(shell.Model.Clients.Items[2].IsInUse);
        Assert.Contains("3 en uso", shell.Model.Clients.Summary);
    }

    [AvaloniaFact]
    public async Task PickingAFolderAddsTheNextExampleFolderUntilTheyRunOut()
    {
        var shell = new Shell();
        var clients = shell.Model.Clients;
        shell.Model.SectionIndex = 1;
        clients.PickFolderCommand.Execute(null);
        Assert.Equal(4, clients.Items.Count);
        Assert.Equal(ClientOrigin.Manual, clients.Items[3].Origin);
        Assert.Equal("4 clientes encontrados · 2 en uso. Solo leemos las rutas.", clients.Summary);
        Assert.Equal("Carpeta añadida (simulada)", shell.Model.StatusText);
        clients.PickFolderCommand.Execute(null);
        clients.PickFolderCommand.Execute(null);
        Assert.Equal(5, clients.Items.Count);
        Assert.Equal("No hay más carpetas de ejemplo", clients.StatusText);
        await Pump();
    }

    [AvaloniaFact]
    public async Task RescanShowsAStatusWhileItRuns()
    {
        var shell = new Shell();
        var clients = shell.Model.Clients;
        shell.Backend.TimeScale = 1;
        var run = clients.RescanCommand.ExecuteAsync(null);
        Assert.True(clients.IsScanning);
        Assert.Equal("Buscando clientes…", clients.StatusText);
        Assert.False(clients.RescanCommand.CanExecute(null));
        await run;
        Assert.False(clients.IsScanning);
        Assert.Equal("Clientes · 3 detectados", clients.StatusText);
    }

    [AvaloniaFact]
    public async Task ClientEditOpensItsSheetAndUpdatesThePreviewRow()
    {
        var shell = new Shell();
        shell.Model.SectionIndex = 1;
        var client = shell.Model.Clients.Items[0];

        client.EditCommand.Execute(null);
        await Pump();

        Assert.IsType<EditClientViewModel>(shell.Model.Content);
        Assert.Contains(shell.Window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "ClientNameBox");
        var editor = (EditClientViewModel)shell.Model.Content;
        editor.Name = "WotLK local";
        editor.Folder = "/tmp/wotlk-test";
        await editor.SaveCommand.ExecuteAsync(null);
        await Pump();

        Assert.Equal("WotLK local", client.Title);
        Assert.Equal("/tmp/wotlk-test", client.Path);
        Assert.Same(shell.Model.Clients, shell.Model.Content);
    }

    [AvaloniaFact]
    public async Task ClientRemoveUsesConfirmationAndCancelKeepsTheRow()
    {
        var shell = new Shell();
        shell.Model.SectionIndex = 1;
        var clients = shell.Model.Clients;
        var first = clients.Items[0];

        first.RemoveCommand.Execute(null);
        await Pump();
        Assert.NotNull(shell.Model.Dialog);
        Assert.Equal("¿Quitar este cliente?", shell.Model.Dialog!.Title);
        shell.Model.Dialog.CancelCommand.Execute(null);
        await Pump();
        Assert.Equal(3, clients.Items.Count);

        first.RemoveCommand.Execute(null);
        await Pump();
        shell.Model.Dialog!.ConfirmCommand.Execute(null);
        await Pump();
        Assert.Equal(2, clients.Items.Count);
    }

    [AvaloniaFact]
    public void EveryOriginHasATranslation()
    {
        foreach (var origin in Enum.GetValues<ClientOrigin>())
        {
            var item = new ClientItemViewModel(new ClientSeed("3.3.5a (12340)", "/home/demo/x", origin, false));
            Assert.DoesNotContain("!", item.OriginText);
        }
    }
}

public class AddServerScreenTests
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

    private static AddServerViewModel Open(Shell shell)
    {
        shell.Servers.RequestAddServerCommand.Execute(null);
        return shell.Model.Adding ?? throw new InvalidOperationException("the form did not open");
    }

    [AvaloniaFact]
    public async Task AddButtonOpensTheEmptyForm()
    {
        var shell = new Shell();
        var form = Open(shell);
        await Pump();
        Assert.Same(form, shell.Model.Content);
        Assert.Equal("Añadir servidor · sin guardar", shell.Model.StatusText);
        Assert.Equal(0, shell.Model.SectionIndex);
        Assert.Equal("3.3.5a (12340)", form.SelectedClient!.Version);
        Assert.False(form.HasNameError);
        shell.Snap("add-server-empty");
    }

    [AvaloniaFact]
    public async Task SavingAnEmptyFormShowsTheErrorsAndSavesNothing()
    {
        var shell = new Shell();
        var form = Open(shell);
        form.SaveCommand.Execute(null);
        await Pump();
        Assert.True(form.HasNameError);
        Assert.True(form.HasAddressError);
        Assert.False(form.HasClientError);
        Assert.Equal(4, shell.Servers.All.Count);
        Assert.Same(form, shell.Model.Content);
        shell.Snap("add-server-errors");
    }

    [AvaloniaTheory]
    [InlineData("logon.ejemplo.com", true, "logon.ejemplo.com", 3724)]
    [InlineData("logon.ejemplo.com:3724", true, "logon.ejemplo.com", 3724)]
    [InlineData(" 127.0.0.1:8085 ", true, "127.0.0.1", 8085)]
    [InlineData("localhost", true, "localhost", 3724)]
    [InlineData("", false, "", 3724)]
    [InlineData("bad host", false, "", 3724)]
    [InlineData("host:0", false, "", 3724)]
    [InlineData("host:70000", false, "", 3724)]
    [InlineData("host:", false, "", 3724)]
    [InlineData("-host.com", false, "", 3724)]
    public void AddressParsing(string text, bool valid, string host, int port)
    {
        Assert.Equal(valid, AddServerViewModel.TryParseAddress(text, out var parsedHost, out var parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [AvaloniaFact]
    public async Task TestConnectionShowsTheResultInTheSheetHeader()
    {
        var shell = new Shell();
        var form = Open(shell);
        form.Name = "Mi Servidor";
        form.Address = "logon.ejemplo.com:3724";
        form.Account = "demo";
        await form.TestCommand.ExecuteAsync(null);
        await Pump();
        Assert.Equal(ConnectionTest.Ok, form.TestState);
        Assert.Equal("Conexión correcta", form.ResultText);
        Assert.Equal(ServerStatus.Ready, form.ResultStatus);
        var address = shell.Window.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "AddressBox");
        address.Focus();
        await Pump();
        shell.Snap("add-server");

        form.Address = "game.offline.example";
        Assert.Equal(ConnectionTest.None, form.TestState);
        await form.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Failed, form.TestState);
        Assert.Equal("Sin respuesta", form.ResultText);
        Assert.Equal(ServerStatus.NoResponse, form.ResultStatus);
    }

    [AvaloniaFact]
    public async Task TestConnectionNeedsAValidAddress()
    {
        var shell = new Shell();
        var form = Open(shell);
        await form.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.None, form.TestState);
        Assert.True(form.HasAddressError);
    }

    [AvaloniaFact]
    public async Task TestingShowsABusyResultAndBlocksASecondTest()
    {
        var shell = new Shell();
        shell.Backend.TimeScale = 1;
        var form = Open(shell);
        form.Address = "logon.ejemplo.com";
        var run = form.TestCommand.ExecuteAsync(null);
        Assert.Equal(ConnectionTest.Testing, form.TestState);
        Assert.Equal("Probando…", form.ResultText);
        Assert.Equal("Probando conexión…", shell.Model.StatusText);
        Assert.False(form.TestCommand.CanExecute(null));
        await run;
        Assert.Equal(ConnectionTest.Ok, form.TestState);
    }

    [AvaloniaFact]
    public async Task SavingAddsTheServerAndReturnsToTheList()
    {
        var shell = new Shell();
        var form = Open(shell);
        form.Name = "  Reino Nuevo ";
        form.Address = "play.nuevo.net:8085";
        form.Account = "";
        form.SaveCommand.Execute(null);
        await Pump();
        Assert.Null(shell.Model.Adding);
        Assert.Same(shell.Servers, shell.Model.Content);
        var added = shell.Servers.All.Last();
        Assert.Equal("Reino Nuevo", added.Name);
        Assert.Equal("play.nuevo.net:8085", added.Address);
        Assert.Null(added.Account);
        Assert.False(added.IsActive);
        Assert.Contains(added, shell.Servers.Visible);
        Assert.Equal("Cliente 3.3.5a (12340) · Sin cuenta", added.DetailText);
    }

    [AvaloniaFact]
    public async Task CancelAndLeavingTheSectionDropTheForm()
    {
        var shell = new Shell();
        var form = Open(shell);
        form.CancelCommand.Execute(null);
        Assert.Null(shell.Model.Adding);
        Assert.Equal(4, shell.Servers.All.Count);

        Open(shell);
        shell.Model.SectionIndex = 1;
        Assert.Null(shell.Model.Adding);
        await Pump();
    }

    [AvaloniaFact]
    public async Task NameLongerThanTheLimitIsRejected()
    {
        var shell = new Shell();
        var form = Open(shell);
        form.Name = new string('a', 65);
        form.Address = "logon.ejemplo.com";
        form.SaveCommand.Execute(null);
        await Pump();
        Assert.True(form.HasNameError);
        Assert.Equal(4, shell.Servers.All.Count);
    }

    [AvaloniaFact]
    public async Task ClientListFollowsTheClientsScreen()
    {
        var shell = new Shell();
        shell.Model.Clients.PickFolderCommand.Execute(null);
        var form = Open(shell);
        await Pump();
        Assert.Equal(4, form.Clients.Items.Count);
        var box = shell.Window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "ClientBox");
        Assert.Equal(4, box.ItemCount);
        Assert.Equal("Cliente 3.3.5a (12340) · Lutris", form.SelectedClient!.Label);
    }
}
