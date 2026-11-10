# PC Power Meter

A small Windows app that shows how many watts your PC is drawing right now: GPU, CPU,
the rest of the system and an estimate of the power at the wall socket.

![PC Power Meter main window](docs/screenshot.png)

- **No drivers.** It installs no kernel driver (no WinRing0, PawnIO or similar), so it is
  friendly to games with anti-cheat software.
- **No administrator rights.** It runs as a normal user and changes no system settings.
- **No network.** No telemetry, no update check, no internet access at all.
- Live chart of the last five minutes, session average / maximum / energy (Wh), optional
  CSV log, notification area icon with the current wattage, English and Turkish UI,
  dark and light theme following Windows.

## What is measured and what is estimated

| Row | Source | Kind |
|---|---|---|
| GPU | NVIDIA Management Library (`nvml.dll`, part of the NVIDIA driver); `nvidia-smi` as a fallback. Board power of all NVIDIA GPUs. | **Measured** |
| CPU package | Windows Energy Meter performance counter (`Win32_PerfRawData_PowerMeterCounter_EnergyMeter`, the CPU's RAPL package energy counter). Power = energy delta / time delta, using the meter's own timestamps. | **Measured** (value reported by the CPU) |
| CPU package, no counter | `idle + load x (TDP - idle)`, with CPU load from `GetSystemTimes`. TDP and idle power are set in Settings. | **Estimate** |
| Other components | Fixed allowance for motherboard, RAM, drives, fans and USB devices (default 40 W). | **Estimate** |
| DC total | GPU + CPU + other: the power used inside the case. | Mixed |
| Wall | DC total / PSU efficiency (default 90 %). | **Estimate** |

Every row is labelled *measured*, *estimate* or *unavailable* in the window, together with
its source.

- If the GPU cannot be read, the GPU row says **unavailable** and the totals are marked
  **"GPU NOT included"**. A missing GPU is never counted as 0 W.
- The CPU value is **package power**: cores, I/O die / SoC and cache together. It is higher
  than the "core power" some other tools show.
- Energy (Wh) is accumulated per interval, and each row's session average is its energy
  divided by the time that row covered. The GPU row only covers intervals in which the GPU
  was read. The DC and wall rows cover every interval; in intervals where the GPU could
  not be read they contain no GPU energy, so their average is then too low. The window
  shows this as "N of M intervals without GPU" under those rows, and the CSV has a
  `gpu_included` column per reading.
- After sleep or hibernation (or any interval longer than three sampling intervals and at
  least 5 s) that span is skipped, not counted: the sensors take new baselines and the
  footer shows how many gaps were skipped.

## Supported hardware

- **CPU:** processors for which Windows exposes the Energy Meter counter (RAPL package
  energy). This includes Intel CPUs and AMD Ryzen CPUs on current Windows 10/11 builds.
  You can check in Performance Monitor: the counter set "Energy Meter" should list
  `RAPL_Package0_PKG`. Without it the app falls back to the load-based estimate.
- **GPU:** NVIDIA GPUs with the NVIDIA driver installed.
- **Windows:** Windows 10 or 11, 64-bit (x64).

## Limitations

- **The wall value is an estimate.** Software cannot measure wall power; it depends on the
  real PSU efficiency, which varies with load. Use a plug-in watt meter for exact numbers.
- **Monitors are not included**, nor speakers, routers or anything else on the socket.
- **AMD and Intel GPU power is not read.** Only NVIDIA GPUs report power without a driver;
  on other GPUs the totals exclude the GPU and the app says so.
- "Other components" is a fixed allowance, not a measurement.

## Install and run

1. Download `PcPowerMeter-<version>-win-x64.exe` from the
   [Releases](../../releases) page. It is a single, self-contained file; no .NET install
   is needed. Each release also has a `.sha256` file to verify the download.
2. Run it. There is no installer.

**SmartScreen warning:** the exe is not code-signed, so Windows SmartScreen may show
"Windows protected your PC" on first start. Click **More info**, then **Run anyway**. If
you prefer, build it yourself from source (see below).

Settings are stored in `%APPDATA%\PcPowerMeter\settings.json`. If the app hits an
unexpected error, it writes the details to `%LOCALAPPDATA%\PcPowerMeter\error.log` (nothing
is sent anywhere) and closes. To remove the app, delete the exe and those two folders.

### Notification area

The tray icon shows the current wall estimate when you hover over it. Its menu has
**Show window**, **Reset session** and **Exit**. In Settings you can choose that closing
the window keeps the app running in the notification area.

### Settings

| Setting | Meaning | Default |
|---|---|---|
| Sampling interval | Seconds between readings (0.25 - 60) | 1 |
| PSU efficiency | Used for the wall estimate (50 - 100 %). At low load real PSUs are often less efficient (about 85 %). | 90 % |
| Other components | Fixed watts for everything else in the case | 40 W |
| CPU TDP, CPU idle power | Only used when the CPU has no energy counter | 120 W, 25 W |
| Language | System default, English or Turkish | System default |
| CSV log, folder | Write every reading to a CSV file | Off, `Documents\PC Power Meter` |

### Command-line switches

| Switch | Effect |
|---|---|
| `--lang en` / `--lang tr` | Use this language for this run only |
| `--no-gpu` | Pretend there is no GPU sensor (to test the "unavailable" path) |
| `--no-cpu-meter` | Ignore the CPU energy counter and use the load-based estimate |

## CSV log format

Each session (and each **Reset session**) starts a new file named
`power-log_YYYYMMDD_HHMMSS.csv`. If that name is taken or locked by another program, a
suffix such as `_2` is added; existing files are never overwritten. If writing fails later
(for example the disk is removed), logging stops with a warning and the app keeps running.

The format is the same on every system, whatever the regional settings:

- UTF-8, comma (`,`) as the separator, dot (`.`) as the decimal point, two decimals.
- `timestamp` is local time as `yyyy-MM-dd HH:mm:ss`.
- An empty field means the value was not available (for example no GPU).

Columns: `timestamp, elapsed_s, gpu_w, gpu_load_pct, gpu_source, cpu_w, cpu_load_pct,
cpu_source, other_w_est, dc_w, wall_w_est, psu_efficiency, gpu_included`.

**Excel note:** in regions that use a comma as the decimal separator (for example Turkish
or German), double-clicking the file may put everything in one column or read `12.50` as a
date or a whole number. Use **Data > From Text/CSV**, choose **Comma** as the delimiter and
set **File Origin / Locale** to *English (United States)*, or open it in LibreOffice and
pick the same options.

## Build from source

Requirements: Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build PcPowerMeter.sln
dotnet test PcPowerMeter.sln
dotnet run --project src/PcPowerMeter
```

Single-file, self-contained exe (as on the Releases page):

```
dotnet publish src/PcPowerMeter/PcPowerMeter.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

Repository layout:

- `src/PcPowerMeter.Core` - platform-independent calculations (energy deltas, statistics,
  CSV format, settings); covered by the tests.
- `src/PcPowerMeter` - the WPF app: sensors (NVML, nvidia-smi, Energy Meter, CPU times),
  UI, tray icon.
- `tests/PcPowerMeter.Tests` - xUnit tests.
- `tools/make-icon.ps1` - regenerates the app icon.

The only NuGet package the app uses is `System.Management` (MIT, from Microsoft) for WMI.

Pushing a tag such as `v1.0.0` runs `.github/workflows/release.yml`, which builds the exe
and attaches it to a GitHub release.

## License

[MIT](LICENSE). Copyright (c) 2026 Zeruh31.

---

## Türkçe

Bilgisayarın o anda kaç watt çektiğini gösteren küçük bir Windows uygulaması: GPU, CPU,
sistemin geri kalanı ve prizden çekilen gücün tahmini.

- **Sürücü kurmaz.** Hiçbir çekirdek sürücüsü yüklemez (WinRing0, PawnIO vb. yok); bu
  yüzden hile önleme (anti-cheat) yazılımı olan oyunlarla sorun çıkarmaz.
- **Yönetici yetkisi istemez**, sistem ayarlarını değiştirmez.
- **İnternete bağlanmaz.** Telemetri yok, güncelleme kontrolü yok.
- Son beş dakikanın canlı grafiği, oturum ortalaması / en yüksek değer / toplam enerji
  (Wh), isteğe bağlı CSV kaydı, bildirim alanında anlık watt, Türkçe ve İngilizce arayüz,
  Windows'a uyan koyu ve açık tema.

### Ne ölçülüyor, ne tahmin

| Satır | Kaynak | Tür |
|---|---|---|
| GPU | NVIDIA sürücüsüyle gelen NVML (`nvml.dll`); yedek olarak `nvidia-smi`. Kartın toplam gücü. | **Ölçüm** |
| CPU paketi | Windows'un yerleşik Enerji Ölçer sayacı (işlemcinin RAPL paket enerji sayacı). Güç = enerji farkı / zaman farkı. | **Ölçüm** (işlemcinin bildirdiği değer) |
| CPU paketi, sayaç yoksa | `boşta + yük x (TDP - boşta)`; TDP ve boşta güç Ayarlar'dan girilir. | **Tahmin** |
| Diğer bileşenler | Anakart, RAM, disk, fan, USB için sabit pay (varsayılan 40 W). | **Tahmin** |
| Toplam DC | GPU + CPU + diğer: kasanın içinde harcanan güç. | Karışık |
| Duvar | Toplam DC / PSU verimi (varsayılan %90). | **Tahmin** |

- GPU okunamazsa GPU satırında **kullanılamıyor** yazar ve toplamlar **"GPU HARİÇ"** diye
  işaretlenir; eksik GPU asla 0 W sayılmaz.
- CPU değeri **paket gücüdür** (çekirdekler + IO/SoC + önbellek); bazı programlarda görülen
  "çekirdek gücü"nden yüksektir.
- GPU'nun okunamadığı aralıklar toplam DC ve duvar satırlarına GPU enerjisi olmadan girer;
  pencere bunu bu satırların altında "M aralığın N tanesinde GPU yok" diye gösterir.
- Uyku/hazırda bekleme sonrası (ya da üç ölçüm aralığından ve 5 sn'den uzun bir aralıkta) o
  süre sayılmaz; sensörler yeniden başlangıç alır.
- Beklenmeyen bir hata olursa ayrıntılar `%LOCALAPPDATA%\PcPowerMeter\error.log` dosyasına
  yazılır; hiçbir yere gönderilmez.

### Desteklenen donanım ve sınırlamalar

- **CPU:** Windows'un Enerji Ölçer sayacını sunduğu işlemciler (Intel ve güncel Windows
  10/11'de AMD Ryzen). Sayaç yoksa yükten tahmin edilir ve bu ekranda yazılır.
- **GPU:** sürücüsü kurulu NVIDIA kartlar. **AMD ve Intel GPU gücü okunmaz**; bu durumda
  toplamlar GPU hariçtir ve uygulama bunu açıkça gösterir.
- **Duvar değeri tahmindir.** Kesin değer için prize takılan bir watt-metre gerekir.
- **Monitörler dahil değildir.**

### Kurulum ve çalıştırma

[Releases](../../releases) sayfasından `PcPowerMeter-<sürüm>-win-x64.exe` dosyasını indirip
çalıştırın; kurulum ve .NET gerekmez. Exe imzasız olduğu için ilk açılışta Windows
SmartScreen "Windows bilgisayarınızı korudu" uyarısı verebilir: **Ek bilgi** ve ardından
**Yine de çalıştır**'a tıklayın. İsterseniz kaynak koddan kendiniz derleyebilirsiniz
(yukarıdaki "Build from source" bölümü).

Ayarlar `%APPDATA%\PcPowerMeter\settings.json` dosyasında tutulur.

### CSV kaydı ve Excel

CSV dosyası her sistemde aynı biçimdedir: virgül ayraç, nokta ondalık işareti, UTF-8.
Kayıt dosyası başka bir programda (ör. Excel'de) açık ya da kilitliyse uygulama çökmez,
yeni bir dosya adı kullanır. Türkçe Excel'de dosyaya çift tıklamak sütunları karıştırabilir;
**Veri > Metinden/CSV'den** ile açıp ayırıcı olarak **Virgül**, dosya kaynağı / yerel ayar
olarak **İngilizce (ABD)** seçin.

### Lisans

[MIT](LICENSE). Copyright (c) 2026 Zeruh31.
