using System.Text.Json;

namespace Sweeply.Services;

/// <summary>Bir günün disk doluluğu ve RAM yükü (günün son ölçümü).</summary>
public sealed record DaySample(DateOnly Day, double DiskUsedPercent, int RamLoad);

/// <summary>
/// Disk ve RAM kullanımının günlük örnekleri. Anlık değer tek başına "şu an dolu mu" der; geçmiş ise
/// "hızla doluyor" gibi eğilimi gösterir. %LOCALAPPDATA%\Sweeply\history.json altında tutulur.
/// </summary>
public sealed class HealthHistory
{
    public const int KeepDays = 60;

    /// <summary>Diskin büyüdüğünü söylemek için en az bu kadar gün geçmiş olmalı.</summary>
    public const int MinTrendDays = 7;

    static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(10);

    readonly string _path;
    readonly List<DaySample> _samples;
    DateTime _lastSave = DateTime.MinValue;

    public HealthHistory(string? path = null)
    {
        _path = path ?? DefaultPath;
        _samples = Load(_path);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sweeply", "history.json");

    public IReadOnlyList<DaySample> Samples => _samples;

    /// <summary>Bugünün örneğini günceller. Diske yazma en fazla 10 dakikada bir yapılır; kapanışta <see cref="Save"/> çağrılır.</summary>
    public void Record(double diskUsedPercent, int ramLoad, DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        var sample = new DaySample(day, Math.Round(diskUsedPercent, 1), ramLoad);

        int index = _samples.FindIndex(s => s.Day == day);
        if (index >= 0) _samples[index] = sample;
        else _samples.Add(sample);

        _samples.RemoveAll(s => s.Day < day.AddDays(-KeepDays));
        _samples.Sort((a, b) => a.Day.CompareTo(b.Day));

        if (now - _lastSave >= SaveEvery) Save(now);
    }

    /// <returns>Yazılabildiyse true. Yazma hatası uygulamayı durdurmaz; geçmiş bir sonraki kayıtta tekrar denenir.</returns>
    public bool Save(DateTime now)
    {
        _lastSave = now;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_samples));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static List<DaySample> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<DaySample>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
