<p align="center">
  <img src="docs/icon.png" width="96" alt="Optimayzır ikonu">
</p>

<h1 align="center">Optimayzır</h1>

<p align="center">Tek tıkla, <b>güvenli</b> Windows temizliği ve RAM takibi.</p>

<p align="center">
  <img src="docs/screenshot.png" alt="Optimayzır ekran görüntüsü" width="820">
</p>

## Neden bir tane daha "optimizasyon" aracı?

Çoğu "RAM temizleyici", programların belleğini zorla boşaltır. Windows o verilere birkaç saniye sonra tekrar ihtiyaç duyar ve onları diskten geri okur. Sonuçta bilgisayar hızlanmaz, aksine kısa süreliğine yavaşlar.

Optimayzır bunun yerine **gerçekten fark yaratan** işleri yapar:

- **Gereksiz dosyaları siler.** Silinen her şey zaten yeniden oluşturulabilir türden dosyalardır: önbellekler, geçici dosyalar, çökme dökümleri.
- **RAM'i kimin kullandığını gösterir.** Kullanmadığınız programı tek tıkla kapatabilirsiniz. Kapatmadan önce onay ister, program kaydetme sorabilsin diye önce nazikçe kapatmayı dener.
- **Sistemi yavaşlatan durumlar için uyarır:** disk dolmak üzereyse, bilgisayar günlerdir yeniden başlatılmadıysa veya açılışta çok program çalışıyorsa.

## Temizlik görevleri

| Görev | Ne yapar | Varsayılan |
|---|---|---|
| Geçici dosyalar | Kullanıcı ve Windows Temp klasörlerindeki **24 saatten eski** dosyaları siler. Kullanımdaki dosyalar atlanır. | ✅ |
| Tarayıcı önbellekleri | Chrome, Edge ve Brave'in `Cache`, `Code Cache` ve `GPUCache` klasörlerini temizler. Geçmiş, şifreler ve çerezler **silinmez**. Açık tarayıcılar atlanır. | ✅ |
| Geliştirici önbellekleri | npm (`npm cache clean --force`) ve pip önbelleklerini temizler. | ✅ |
| Çökme dökümleri | `CrashDumps` ve Windows hata raporu arşivini temizler. | ✅ |
| Docker build cache | Sadece `docker builder prune -a` çalıştırır. **İmajlara, konteynerlere ve volume'lara dokunmaz.** | ✅ |
| WSL ve Docker'ı kapat | `wsl --shutdown` ile birkaç GB RAM geri kazandırır. Çalışan konteynerler durur. | ⬜ |

Geri Dönüşüm Kutusu, İndirilenler klasörü ve kişisel dosyalar **hiçbir zaman** silinmez.

## Kurulum

**Hazır sürüm:** [Releases](../../releases) sayfasından `Optimayzir.exe` dosyasını indirin. [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) gerekir.

**Kaynaktan kurulum** (.NET 8 SDK gerekir):

```powershell
git clone https://github.com/AlperEnesErsu/Optimayzir.git
cd Optimayzir
powershell -ExecutionPolicy Bypass -File install.ps1
```

Bu betik uygulamayı `%LOCALAPPDATA%\Programs\Optimayzir` klasörüne kurar ve masaüstüne bir **Optimayzır** kısayolu ekler.

## Kullanım

- **Masaüstü kısayolu** uygulamayı `--auto` parametresiyle açar: pencere açılır, analiz yapılır ve işaretli görevler **otomatik olarak** çalışır.
- Uygulamayı normal açarsanız önce analiz sonuçlarını görürsünüz. İstediğiniz görevleri seçip **Tek Tıkla Optimize Et** butonuna basarsınız.
- Kısayolu uygulama içinden de oluşturabilirsiniz: **Masaüstü kısayolu oluştur**.

Yönetici izni gerekmez. Yönetici yetkisi gerektiren dosyalar (ör. bazı `C:\Windows\Temp` içerikleri) sessizce atlanır.

## İpucu: Docker'ın kapladığı alanı Windows'a geri vermek

Docker build cache'i silindiğinde alan Docker'ın sanal diski (`docker_data.vhdx`) içinde boşalır, ama dosyanın kendisi küçülmez. Alanı Windows'a geri vermek için:

1. Docker Desktop'tan çıkın (**Quit Docker Desktop**).
2. **Yönetici olarak** bir terminal açıp şunları çalıştırın:

```powershell
wsl --shutdown
diskpart
```

```text
select vdisk file="%LOCALAPPDATA%\Docker\wsl\disk\docker_data.vhdx"
attach vdisk readonly
compact vdisk
detach vdisk
exit
```

> `diskpart` ortam değişkenlerini açmaz; `%LOCALAPPDATA%` yerine tam yolu yazın (ör. `C:\Users\<kullanıcı>\AppData\Local\...`).

## Geliştirme

```powershell
dotnet build Optimayzir.sln
dotnet run --project src/Optimayzir
```

Yeni bir temizlik görevi eklemek için `src/Optimayzir/Tasks` altında `ICleanupTask` arayüzünü uygulayan bir sınıf yazın ve `MainWindow.xaml.cs` içindeki listeye ekleyin. Uygulama ikonu `tools/make-icon.ps1` ile üretilir.

## Lisans

[MIT](LICENSE)
