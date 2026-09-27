using Sweeply.Services;

namespace Sweeply.Models;

/// <summary>Aynı ada sahip işlemlerin toplamı (ör. 13 adet chrome.exe tek satır).</summary>
/// <param name="Share">Listedeki en büyük gruba göre oran (0–1); satırdaki çubuğun uzunluğu.</param>
public sealed record ProcessGroup(string Name, int Count, long Bytes, double Share = 1)
{
    public string MemoryText => Format.Bytes(Bytes);
    public string CountText => Count == 1 ? "1 işlem" : $"{Count} işlem";
    public double SharePercent => Share * 100;
}
