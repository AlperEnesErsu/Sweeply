using System.Text.Json;

namespace Sweeply.Services;

/// <summary>
/// Kullanıcının ayarları: seçili görevler, kendi temizlik klasörleri, hariç tutulanlar ve RAM kapatmada korunan programlar.
/// %LOCALAPPDATA%\Sweeply\settings.json altında tutulur. Dosya bozuksa veya yoksa varsayılanlarla devam edilir.
/// </summary>
public sealed class AppSettings
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Son seçilen görevlerin başlıkları. null ise varsayılan seçim kullanılır.</summary>
    public List<string>? SelectedTasks { get; set; }

    /// <summary>Kullanıcının temizlemek istediği ek klasörler (içerikleri, 24 saatten eski dosyalarıyla birlikte).</summary>
    public List<string> CustomFolders { get; set; } = [];

    /// <summary>Hiçbir temizlik görevinin dokunmayacağı klasörler (alt klasörleriyle birlikte).</summary>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>RAM kapatma listesinde hiç gösterilmeyecek ek program adları (varsayılan korumaya eklenir).</summary>
    public List<string> ProtectedProcesses { get; set; } = [];

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sweeply", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <returns>Kaydedilebildiyse true. Kayıt hatası uygulamayı durdurmaz.</returns>
    public bool Save(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
