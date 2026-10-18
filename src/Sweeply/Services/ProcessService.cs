using System.Diagnostics;
using Sweeply.Models;

namespace Sweeply.Services;

/// <param name="Remaining">Kapatma denemesinden sonra hâlâ açık olan işlem sayısı.</param>
/// <param name="HadWindow">Kapatma isteği en az bir pencereye gönderildi mi? Hayırsa program arka planda çalışıyordur.</param>
public sealed record CloseOutcome(int Remaining, bool HadWindow);

public static class ProcessService
{
    // Windows'un çalışması için gereken veya kapatılması sorun çıkaran işlemler listede hiç gösterilmez.
    internal static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso",
        "svchost", "dwm", "fontdrvhost", "Memory Compression", "MsMpEng", "NisSrv", "SecurityHealthService",
        "explorer", "audiodg", "sihost", "ctfmon", "SearchHost", "SearchIndexer", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "RuntimeBroker", "conhost", "dllhost", "spoolsv", "WmiPrvSE",
        "taskhostw", "vmmem", "vmmemWSL", "vmcompute", "vmwp", "Sweeply",
    };

    /// <summary>Kapatma denemesinin en fazla bekleme süresi; işlemler bundan önce kapanırsa hemen döner.</summary>
    static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(3);

    public static List<ProcessGroup> GetTop(int count, IEnumerable<string>? extraProtected = null)
    {
        var samples = new List<(string Name, long Bytes)>();
        foreach (var p in Process.GetProcesses())
        {
            try { samples.Add((p.ProcessName, p.WorkingSet64)); }
            catch { }
            finally { p.Dispose(); }
        }
        return Group(samples, count, extraProtected);
    }

    /// <summary>İşlemleri ada göre toplar, korunanları eler, RAM'e göre sıralar ve ilk <paramref name="count"/> tanesini döndürür.</summary>
    internal static List<ProcessGroup> Group(IEnumerable<(string Name, long Bytes)> samples, int count, IEnumerable<string>? extraProtected = null)
    {
        var hidden = extraProtected == null ? Protected : new HashSet<string>(Protected.Concat(extraProtected), StringComparer.OrdinalIgnoreCase);
        var groups = samples
            .Where(s => !hidden.Contains(s.Name))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Name, Count: g.Count(), Bytes: g.Sum(s => s.Bytes)))
            .OrderByDescending(g => g.Bytes)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();

        long max = groups.Count > 0 ? Math.Max(1, groups[0].Bytes) : 1;
        return groups.Select(g => new ProcessGroup(g.Name, g.Count, g.Bytes, (double)g.Bytes / max)).ToList();
    }

    /// <summary>
    /// Programa "kapan" mesajı gönderir (kaydetme sorabilsin diye). Hepsi kapanana ya da süre dolana kadar bekler;
    /// pencere olmayan (arka plan) işlemlere mesaj gönderilemeyeceği için <see cref="CloseOutcome.HadWindow"/> bunu söyler.
    /// </summary>
    public static async Task<CloseOutcome> CloseGracefullyAsync(string name)
    {
        bool hadWindow = false;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { hadWindow |= p.CloseMainWindow(); } catch { }
            p.Dispose();
        }

        var deadline = DateTime.UtcNow + GracePeriod;
        int remaining = CommandRunner.CountProcesses(name);
        while (remaining > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            remaining = CommandRunner.CountProcesses(name);
        }
        return new CloseOutcome(remaining, hadWindow);
    }

    public static void Kill(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(); } catch { }
            p.Dispose();
        }
    }
}
