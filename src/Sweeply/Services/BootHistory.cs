using System.Diagnostics.Eventing.Reader;
using Microsoft.Win32;

namespace Sweeply.Services;

public enum BootKind { Full, FastStartup, Hibernate, Unknown }

public sealed record BootEvent(DateTime Time, BootKind Kind);

/// <summary>
/// Hızlı Başlangıç açıkken "Kapat" aslında hazırda bekletmedir: çekirdek diske kaydedilir ve açık kalma süresi
/// sıfırlanmaz. Kullanıcı "ama ben kapatıyorum" diye şaşırmasın diye açılışların gerçek türünü olay günlüğünden okuruz.
/// </summary>
public static class BootHistory
{
    const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    public static bool FastStartupEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PowerKey);
            return key?.GetValue("HiberbootEnabled") is int value && value == 1;
        }
        catch { return false; }
    }

    /// <summary>Kernel-Boot olay 27'deki açılış türü kodu.</summary>
    internal static BootKind KindFrom(int code) => code switch
    {
        0 => BootKind.Full,
        1 => BootKind.FastStartup,
        2 => BootKind.Hibernate,
        _ => BootKind.Unknown,
    };

    public static string Describe(BootKind kind) => kind switch
    {
        BootKind.Full => "Tam açılış",
        BootKind.FastStartup => "Hızlı Başlangıç (tam kapanmamıştı)",
        BootKind.Hibernate => "Hazırda bekletmeden dönüş",
        _ => "Bilinmeyen açılış türü",
    };

    /// <summary>En yeniden eskiye son açılışlar. Olay günlüğü okunamazsa boş liste.</summary>
    public static List<BootEvent> Recent(int max = 15)
    {
        var events = new List<BootEvent>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Kernel-Boot'] and (EventID=27)]]")
            {
                ReverseDirection = true,
            };
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record != null && events.Count < max; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (record.TimeCreated is not { } time || record.Properties.Count == 0) continue;
                    events.Add(new BootEvent(time, KindFrom(Convert.ToInt32(record.Properties[0].Value))));
                }
            }
        }
        catch { }
        return events;
    }
}
