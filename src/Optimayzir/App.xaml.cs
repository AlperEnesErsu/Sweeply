using System.Windows;

namespace Optimayzir;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Masaüstü kısayolu uygulamayı --auto ile açar: pencere açılır açılmaz optimizasyon başlar.
        bool autoRun = e.Args.Any(a => a.Equals("--auto", StringComparison.OrdinalIgnoreCase));
        new MainWindow(autoRun).Show();
    }
}
