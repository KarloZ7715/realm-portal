using Avalonia.Headless.XUnit;
using RealmPortal.App.Localization;
using RealmPortal.App.Settings;
using RealmPortal.App.ViewModels;

namespace RealmPortal.App.Tests;

public class SettingsViewModelTests
{
    private sealed class MemoryStore : ISettingsStore
    {
        private SettingsModel _model = new(
            Language.En,
            "wine {exe}",
            [],
            [],
            new Dictionary<string, string?>(),
            "/tmp/backups");

        public SettingsModel Load() => _model;

        public SettingsError? Save(SettingsModel settings)
        {
            if (string.IsNullOrWhiteSpace(settings.CommandTemplate)
                || !settings.CommandTemplate.Contains("{exe}", StringComparison.Ordinal))
            {
                return new SettingsError("InvalidLauncherSettings");
            }

            if (string.IsNullOrWhiteSpace(settings.BackupFolder))
            {
                return new SettingsError("PathNotFound");
            }

            _model = settings;
            return null;
        }
    }

    [AvaloniaFact]
    public void ApplySavedLanguage_RestoresPersistedLanguage()
    {
        Loc.Instance.SetLanguage(Language.En);
        var store = new MemoryStore();
        store.Save(new SettingsModel(Language.Es, "wine {exe}", [], [], new Dictionary<string, string?>(), "/tmp/backups"));
        var vm = new SettingsViewModel(store);
        Loc.Instance.SetLanguage(Language.En);
        vm.ApplySavedLanguage();
        Assert.Equal(Language.Es, Loc.Instance.Current);
    }

    [AvaloniaFact]
    public void Save_RejectsEmptyOrMissingExeTemplate()
    {
        var vm = new SettingsViewModel(new MemoryStore());
        vm.Template = "   ";
        vm.SaveCommand.Execute(null);
        Assert.NotNull(vm.TemplateError);

        vm.Template = "wine";
        vm.SaveCommand.Execute(null);
        Assert.NotNull(vm.TemplateError);
    }

    [AvaloniaFact]
    public void Back_DiscardsDirtyFieldsAndLanguagePreviewLikeCancel()
    {
        var vm = new SettingsViewModel(new MemoryStore());
        vm.Template = "wine {exe} -windowed";
        vm.IsEnglish = false;
        Assert.True(vm.IsDirty);

        vm.BackCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.Equal(Language.En, Loc.Instance.Current);
        Assert.Equal("wine {exe}", vm.Template);
    }

    [AvaloniaFact]
    public void TemplateWatermarkKey_IsDefinedInBothLanguages()
    {
        Assert.Equal("wine {exe}", Loc.Load(Language.En)["settings.template.watermark"]);
        Assert.Equal("wine {exe}", Loc.Load(Language.Es)["settings.template.watermark"]);
    }
}
