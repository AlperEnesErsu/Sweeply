using Sweeply.Services;

namespace Sweeply.Tests;

public class StartupServiceTests
{
    [Fact]
    public void ApprovedValue_Enabled_IsTwelveBytesWithFlag2()
    {
        var data = StartupService.ApprovedValue(enabled: true);

        Assert.Equal(12, data.Length);
        Assert.Equal(0x02, data[0]);
        Assert.False(StartupService.IsDisabledFlag(data));
    }

    [Fact]
    public void ApprovedValue_Disabled_HasFlag3AndTimestamp()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var data = StartupService.ApprovedValue(enabled: false, when);

        Assert.Equal(0x03, data[0]);
        Assert.Equal(when.ToFileTimeUtc(), BitConverter.ToInt64(data, 4));
        Assert.True(StartupService.IsDisabledFlag(data));
    }

    [Fact]
    public void ValueName_RunEntry_UsesRegistryName()
    {
        var item = new StartupItem("Spotify", @"C:\Users\x\Spotify.exe /min", "Kullanıcı · kayıt defteri");

        Assert.Equal("Spotify", item.ValueName);
        Assert.False(item.FromFolder);
        Assert.True(item.PerUser);
    }

    [Fact]
    public void ValueName_FolderEntry_UsesFileNameWithExtension()
    {
        var item = new StartupItem("Notlar", @"C:\Users\x\AppData\Start\Notlar.lnk", "Tüm kullanıcılar · Başlangıç klasörü");

        Assert.Equal("Notlar.lnk", item.ValueName);
        Assert.True(item.FromFolder);
        Assert.False(item.PerUser);
    }
}
