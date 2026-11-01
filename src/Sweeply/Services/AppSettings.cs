using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sweeply.Services;

/// <summary>
/// Kullanıcının ayarları: seçili görevler, kendi temizlik klasörleri, hariç tutulanlar, RAM kapatmada korunan programlar
/// ve otomatik temizlik zamanı. %LOCALAPPDATA%\Sweeply\settings.json altında tutulur.
/// Dosya yoksa varsayılanlarla devam edilir; okunamıyorsa önce yedeği alınır, böylece ilk kayıt kullanıcının ayarlarını yok etmez.
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

    /// <summary>Haftalık otomatik temizliğin günü ve saati; zamanlama penceresi bunlarla açılır.</summary>
    public DayOfWeek ScheduleDay { get; set; } = ScheduleService.DefaultDay;
    public string ScheduleTime { get; set; } = ScheduleService.DefaultTime;

    /// <summary>Ayar dosyası okunamadıysa kullanıcıya gösterilecek açıklama; sorun yoksa null.</summary>
    [JsonIgnore]
    public string? LoadProblem { get; private set; }

    /// <summary>Bozuk dosyanın yedeği alınamadıysa true: o zaman dosyanın üzerine yazılmaz.</summary>
    [JsonIgnore]
    public bool ReadOnly { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sweeply", "settings.json");

    /// <summary>Okunamayan ayar dosyasının yedeği: aynı klasörde <c>settings.json.bozuk</c>.</summary>
    public static string BackupPath(string file) => file + ".bozuk";

    public static AppSettings Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file)) return new AppSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex)
        {
            var backup = BackupPath(file);
            try
            {
                File.Copy(file, backup, overwrite: true);
                return new AppSettings
                {
                    LoadProblem = $"Ayar dosyası okunamadığı için varsayılan ayarlarla açıldı. Eski dosyanın kopyası: {backup}",
                };
            }
            catch
            {
                return new AppSettings
                {
                    LoadProblem = $"Ayar dosyası okunamadı ({ex.Message}). Dosyayı korumak için bu oturumda değişiklikler kaydedilmeyecek: {file}",
                    ReadOnly = true,
                };
            }
        }
    }

    /// <summary>JSON'da <c>null</c> yazan listeler ve geçersiz zamanlama değerleri varsayılana döner.</summary>
    void Normalize()
    {
        CustomFolders ??= [];
        ExcludedFolders ??= [];
        ProtectedProcesses ??= [];
        if (!Enum.IsDefined(ScheduleDay)) ScheduleDay = ScheduleService.DefaultDay;
        if (ScheduleService.ParseTime(ScheduleTime) == null) ScheduleTime = ScheduleService.DefaultTime;
    }

    /// <returns>Kaydedilebildiyse true. Kayıt hatası uygulamayı durdurmaz.</returns>
    public bool Save(string? path = null)
    {
        if (ReadOnly) return false;
        var file = path ?? DefaultPath;
        // Önce geçici dosyaya yazılır: yazma yarıda kalırsa eski ayarlar bozulmaz.
        var temp = file + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
            File.Move(temp, file, overwrite: true);
            return true;
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            return false;
        }
    }
}
