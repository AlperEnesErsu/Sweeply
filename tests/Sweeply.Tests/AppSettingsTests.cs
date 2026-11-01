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
    public void Load_CorruptFile_BacksUpOriginalAndReportsProblem()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json", 0);
        File.WriteAllText(file, "{ bozuk json");

        var settings = AppSettings.Load(file);

        Assert.NotNull(settings.LoadProblem);
        Assert.False(settings.ReadOnly);
        Assert.Equal("{ bozuk json", File.ReadAllText(AppSettings.BackupPath(file)));
        Assert.Contains(AppSettings.BackupPath(file), settings.LoadProblem);
    }

    [Fact]
    public void Load_CorruptFile_SavingAfterwardsKeepsBackup()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json", 0);
        File.WriteAllText(file, "{ bozuk json");

        var settings = AppSettings.Load(file);
        settings.CustomFolders.Add(@"D:\Cache");
        Assert.True(settings.Save(file));

        Assert.Equal("{ bozuk json", File.ReadAllText(AppSettings.BackupPath(file)));
        Assert.Equal([@"D:\Cache"], AppSettings.Load(file).CustomFolders);
    }

    [Fact]
    public void Load_CorruptFile_WhenBackupImpossible_RefusesToOverwrite()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json", 0);
        File.WriteAllText(file, "{ bozuk json");
        // Yedeğin yazılacağı yolda bir klasör varsa kopyalama başarısız olur.
        Directory.CreateDirectory(AppSettings.BackupPath(file));

        var settings = AppSettings.Load(file);

        Assert.True(settings.ReadOnly);
        Assert.NotNull(settings.LoadProblem);
        Assert.False(settings.Save(file));
        Assert.Equal("{ bozuk json", File.ReadAllText(file));
    }

    [Fact]
    public void Load_ValidFile_HasNoProblem()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");
        new AppSettings().Save(file);

        var settings = AppSettings.Load(file);

        Assert.Null(settings.LoadProblem);
        Assert.False(File.Exists(AppSettings.BackupPath(file)));
    }

    [Fact]
    public void Load_NullListsAndBadSchedule_FallBackToDefaults()
    {
        using var dir = new TempDir();
        var file = dir.File("settings.json", 0);
        File.WriteAllText(file, """{ "CustomFolders": null, "ExcludedFolders": null, "ProtectedProcesses": null, "ScheduleDay": 42, "ScheduleTime": "99:99" }""");

        var settings = AppSettings.Load(file);

        Assert.Empty(settings.CustomFolders);
        Assert.Empty(settings.ExcludedFolders);
        Assert.Empty(settings.ProtectedProcesses);
        Assert.Equal(ScheduleService.DefaultDay, settings.ScheduleDay);
        Assert.Equal(ScheduleService.DefaultTime, settings.ScheduleTime);
        Assert.Null(settings.LoadProblem);
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");

        Assert.True(new AppSettings().Save(file));

        Assert.Equal(["settings.json"], Directory.GetFiles(dir.Path).Select(Path.GetFileName));
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
            ScheduleDay = DayOfWeek.Friday,
            ScheduleTime = "18:00",
        };

        Assert.True(original.Save(file));
        var loaded = AppSettings.Load(file);

        Assert.Equal(original.SelectedTasks, loaded.SelectedTasks);
        Assert.Equal(original.CustomFolders, loaded.CustomFolders);
        Assert.Equal(original.ExcludedFolders, loaded.ExcludedFolders);
        Assert.Equal(original.ProtectedProcesses, loaded.ProtectedProcesses);
        Assert.Equal(DayOfWeek.Friday, loaded.ScheduleDay);
        Assert.Equal("18:00", loaded.ScheduleTime);
    }

    [Fact]
    public void Save_UnwritablePath_ReturnsFalse()
    {
        using var dir = new TempDir();
        // Dizin olan bir yola dosya olarak yazılamaz.
        Assert.False(new AppSettings().Save(dir.Path));
        Assert.False(File.Exists(dir.Path + ".tmp"));
    }
}
