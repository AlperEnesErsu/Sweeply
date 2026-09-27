using System.Diagnostics;
using Sweeply.Models;

namespace Sweeply.Services;

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

    public static List<ProcessGroup> GetTop(int count)
    {
        var samples = new List<(string Name, long Bytes)>();
        foreach (var p in Process.GetProcesses())
        {
            try { samples.Add((p.ProcessName, p.WorkingSet64)); }
            catch { }
            finally { p.Dispose(); }
        }
        return Group(samples, count);
    }

    /// <summary>İşlemleri ada göre toplar, korunanları eler, RAM'e göre sıralar ve ilk <paramref name="count"/> tanesini döndürür.</summary>
    internal static List<ProcessGroup> Group(IEnumerable<(string Name, long Bytes)> samples, int count)
    {
        var groups = samples
            .Where(s => !Protected.Contains(s.Name))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Name, Count: g.Count(), Bytes: g.Sum(s => s.Bytes)))
            .OrderByDescending(g => g.Bytes)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();

        long max = groups.Count > 0 ? Math.Max(1, groups[0].Bytes) : 1;
        return groups.Select(g => new ProcessGroup(g.Name, g.Count, g.Bytes, (double)g.Bytes / max)).ToList();
    }

    /// <summary>Programa "kapan" mesajı gönderir (kaydetme sorabilsin diye) ve hâlâ açık kalan işlem sayısını döndürür.</summary>
    public static async Task<int> CloseGracefullyAsync(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.CloseMainWindow(); } catch { }
            p.Dispose();
        }
        await Task.Delay(TimeSpan.FromSeconds(3));
        return CommandRunner.CountProcesses(name);
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
