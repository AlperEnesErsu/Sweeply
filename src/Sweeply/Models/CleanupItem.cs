using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sweeply.Tasks;

namespace Sweeply.Models;

/// <summary>Skipped: kullanıcı temizliği durdurduğu için bu görev hiç çalıştırılmadı.</summary>
public enum ItemState { Idle, Running, Done, Failed, Skipped }

/// <summary>Arayüzdeki bir temizlik satırı: görevin kendisi, seçili olup olmadığı ve son analiz/çalıştırma sonucu.</summary>
public sealed class CleanupItem : INotifyPropertyChanged
{
    public CleanupItem(ICleanupTask job)
    {
        Job = job;
        _isEnabled = job.EnabledByDefault;
    }

    public ICleanupTask Job { get; }
    public string Title => Job.Title;
    public string Description => Job.Description;

    /// <summary>Listede bölüm başlığı olarak gösterilir; sıralama <see cref="TaskGroup"/> sırasını izler.</summary>
    public string GroupName => Job.Group switch
    {
        TaskGroup.System => "Sistem",
        TaskGroup.Apps => "Tarayıcılar ve uygulamalar",
        TaskGroup.Developer => "Geliştirici",
        _ => "Bellek",
    };

    bool _isEnabled;
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }

    bool _isAvailable = true;
    public bool IsAvailable { get => _isAvailable; set => Set(ref _isAvailable, value); }

    long _bytes;
    public long Bytes { get => _bytes; set => Set(ref _bytes, value); }

    string _estimate = "…";
    public string Estimate { get => _estimate; set => Set(ref _estimate, value); }

    string? _note;
    public string? Note { get => _note; set => Set(ref _note, value); }

    string? _status;
    public string? Status { get => _status; set => Set(ref _status, value); }

    ItemState _state;
    public ItemState State { get => _state; set => Set(ref _state, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
