namespace Optimayzir.Services;

public static class ShortcutService
{
    /// <summary>Masaüstüne, uygulamayı --auto ile açan bir kısayol ekler ve kısayolun yolunu döndürür.</summary>
    public static string CreateDesktopShortcut()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string link = Path.Combine(desktop, "Optimayzır.lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell kullanılamıyor.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(link);
        shortcut.TargetPath = exe;
        shortcut.Arguments = "--auto";
        shortcut.WorkingDirectory = Path.GetDirectoryName(exe);
        shortcut.IconLocation = exe + ",0";
        shortcut.Description = "Tek tıkla bilgisayarı optimize et";
        shortcut.Save();
        return link;
    }
}
