using Sweeply.Services;

namespace Sweeply.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var dir = new TempDir();
        var settings = AppSettings.Load(Path.Combine(dir.Path, "none.json"));

        Assert.Null(settings.SelectedTasks);
        Assert.Empty(settings.CustomFolders);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json", 0);
        File.WriteAllText(file, "{ bozuk json");

        var settings = AppSettings.Load(file);

        Assert.Empty(settings.ExcludedFolders);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "sub", "settings.json");
        var original = new AppSettings
        {
            SelectedTasks = ["Geçici dosyalar", "Tarayıcı önbellekleri"],
            CustomFolders = [@"D:\Cache"],
            ExcludedFolders = [@"C:\Temp\keep"],
            ProtectedProcesses = ["obs64"],
        };

        Assert.True(original.Save(file));
        var loaded = AppSettings.Load(file);

        Assert.Equal(original.SelectedTasks, loaded.SelectedTasks);
        Assert.Equal(original.CustomFolders, loaded.CustomFolders);
        Assert.Equal(original.ExcludedFolders, loaded.ExcludedFolders);
        Assert.Equal(original.ProtectedProcesses, loaded.ProtectedProcesses);
    }

    [Fact]
    public void Save_UnwritablePath_ReturnsFalse()
    {
        using var dir = new TempDir();
        // Dizin olan bir yola dosya olarak yazılamaz.
        Assert.False(new AppSettings().Save(dir.Path));
    }
}
