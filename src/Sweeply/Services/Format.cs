using System.Globalization;
using System.Text.RegularExpressions;

namespace Sweeply.Services;

public static class Format
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string Bytes(long bytes)
    {
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // GB ve üstü bir ondalıkla, altı tam sayı olarak gösterilir.
        int decimals = unit >= 3 ? 1 : 0;
        value = Math.Round(value, decimals);

        // Yuvarlama bir üst birime taşırdıysa ("1024 MB" yerine "1,0 GB").
        if (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
            decimals = unit >= 3 ? 1 : 0;
        }
        return value.ToString(decimals == 1 ? "0.0" : "0", Tr) + " " + Units[unit];
    }

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} gün {t.Hours} sa" : $"{t.Hours} sa {t.Minutes} dk";

    /// <summary>Docker çıktısındaki "19.68GB", "532.9MB (41%)", "0B" gibi değerleri bayta çevirir (Docker 1000 tabanını kullanır).</summary>
    public static long ParseDockerSize(string text)
    {
        var m = Regex.Match(text.Trim(), @"^([\d.]+)\s*([kKMGTP]?B)");
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return 0;

        long multiplier = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1_000L,
            "MB" => 1_000_000L,
            "GB" => 1_000_000_000L,
            "TB" => 1_000_000_000_000L,
            "PB" => 1_000_000_000_000_000L,
            _ => 1L,
        };
        // 19.68 × 10⁹ kayan noktada 19679999999,99… çıkabilir; kesmek yerine yuvarla.
        return (long)Math.Round(value * multiplier);
    }
}
