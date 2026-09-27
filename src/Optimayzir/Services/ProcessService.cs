using System.Diagnostics;
using Optimayzir.Models;

namespace Optimayzir.Services;

public static class ProcessService
{
    // Windows'un çalışması için gereken veya kapatılması sorun çıkaran işlemler listede hiç gösterilmez.
    static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso",
        "svchost", "dwm", "fontdrvhost", "Memory Compression", "MsMpEng", "NisSrv", "SecurityHealthService",
        "explorer", "audiodg", "sihost", "ctfmon", "SearchHost", "SearchIndexer", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "RuntimeBroker", "conhost", "dllhost", "spoolsv", "WmiPrvSE",
        "taskhostw", "vmmem", "vmmemWSL", "vmcompute", "vmwp", "Optimayzir",
    };

    public static List<ProcessGroup> GetTop(int count)
    {
        var totals = new Dictionary<string, (int Count, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (Protected.Contains(p.ProcessName)) continue;
                var current = totals.GetValueOrDefault(p.ProcessName);
                totals[p.ProcessName] = (current.Count + 1, current.Bytes + p.WorkingSet64);
            }
            catch { }
            finally { p.Dispose(); }
        }

        return totals
            .Select(kv => new ProcessGroup(kv.Key, kv.Value.Count, kv.Value.Bytes))
            .OrderByDescending(g => g.Bytes)
            .Take(count)
            .ToList();
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
