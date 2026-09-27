using Optimayzir.Services;

namespace Optimayzir.Models;

/// <summary>Aynı ada sahip işlemlerin toplamı (ör. 13 adet chrome.exe tek satır).</summary>
public sealed record ProcessGroup(string Name, int Count, long Bytes)
{
    public string MemoryText => Format.Bytes(Bytes);
    public string CountText => Count == 1 ? "1 işlem" : $"{Count} işlem";
}
