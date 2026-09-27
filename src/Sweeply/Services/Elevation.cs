using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace Sweeply.Services;

public static class Elevation
{
    public static bool IsAdmin { get; } = Check();

    static bool Check()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Uygulamayı yönetici olarak yeniden açar. Kullanıcı UAC penceresinde "Hayır" derse false döner.</summary>
    public static bool RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
