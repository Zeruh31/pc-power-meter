using System.Globalization;
using System.Windows;
using PcPowerMeter.Core;

namespace PcPowerMeter.Localization;

/// <summary>
/// English and Turkish UI strings. XAML uses them as DynamicResource "L.Key";
/// code uses <see cref="T"/>.
/// </summary>
internal static class Loc
{
    private static readonly Dictionary<string, (string En, string Tr)> Strings = new()
    {
        ["AppTitle"] = ("PC Power Meter", "PC Power Meter"),
        ["WallLabel"] = ("Estimated wall power", "Tahmini duvar gücü"),
        ["ColComponent"] = ("Component", "Bileşen"),
        ["ColSource"] = ("Source", "Kaynak"),
        ["ColNow"] = ("Now", "Şimdi"),
        ["ColAvg"] = ("Average", "Ortalama"),
        ["ColMax"] = ("Maximum", "Maksimum"),
        ["ColEnergy"] = ("Energy", "Enerji"),
        ["RowGpu"] = ("GPU", "GPU"),
        ["RowCpu"] = ("CPU package", "CPU paketi"),
        ["RowOther"] = ("Other components", "Diğer bileşenler"),
        ["RowDc"] = ("DC total (inside the case)", "Toplam DC (kasa içi)"),
        ["RowDcNoGpu"] = ("DC total (GPU NOT included)", "Toplam DC (GPU HARİÇ)"),
        ["RowWall"] = ("Wall (PSU {0})", "Duvar (PSU verimi {0})"),
        ["TagMeasured"] = ("measured", "ölçüm"),
        ["TagEstimated"] = ("estimate", "tahmin"),
        ["TagUnavailable"] = ("unavailable", "kullanılamıyor"),
        ["TagMixed"] = ("mixed", "karışık"),
        ["SrcNvml"] = ("NVML", "NVML"),
        ["SrcNvidiaSmi"] = ("nvidia-smi", "nvidia-smi"),
        ["SrcEnergyMeter"] = ("Energy Meter (RAPL)", "Enerji Ölçer (RAPL)"),
        ["SrcLoadEstimate"] = ("load estimate", "yük tahmini"),
        ["SrcFixed"] = ("fixed value", "sabit değer"),
        ["SrcNone"] = ("not found", "bulunamadı"),
        ["SrcSum"] = ("GPU + CPU + other", "GPU + CPU + diğer"),
        ["SrcPsu"] = ("DC ÷ PSU efficiency", "DC ÷ PSU verimi"),
        ["Detecting"] = ("Detecting sensors…", "Sensörler aranıyor…"),
        ["WarnGpuMissing"] = (
            "GPU power cannot be read (NVIDIA GPU with driver required). The totals do NOT include the GPU.",
            "GPU gücü okunamıyor (sürücüsü kurulu NVIDIA GPU gerekir). Toplamlara GPU DAHİL DEĞİL."),
        ["WarnGpuSamples"] = (
            "GPU power was unreadable in {0} samples; those samples do not include the GPU.",
            "GPU gücü {0} örnekte okunamadı; o örneklerde toplamlara GPU dahil değil."),
        ["WarnCpuEstimate"] = (
            "No CPU energy counter found: CPU power is an ESTIMATE from CPU load (idle {0} W, TDP {1} W).",
            "CPU enerji sayacı bulunamadı: CPU gücü yükten TAHMİN ediliyor (boşta {0} W, TDP {1} W)."),
        ["WarnCpuWaiting"] = (
            "Waiting for the first CPU energy reading; the totals do not include the CPU yet.",
            "İlk CPU enerji okuması bekleniyor; toplamlara CPU henüz dahil değil."),
        ["Note"] = (
            "Monitors are not included. The wall value is an estimate; only a plug-in watt meter measures it exactly.",
            "Monitörler dahil değil. Duvar değeri tahmindir; kesin değeri yalnız prize takılan bir watt-metre ölçer."),
        ["RowNoGpuIntervals"] = ("{0} of {1} intervals without GPU", "{1} aralığın {0} tanesinde GPU yok"),
        ["GapsSkipped"] = ("{0} gap(s) skipped (sleep or stall)", "{0} boşluk atlandı (uyku ya da takılma)"),
        ["CrashMessage"] = (
            "PC Power Meter hit an unexpected error and has to close. Details were written to:\n{0}",
            "PC Power Meter beklenmeyen bir hatayla karşılaştı ve kapanacak. Ayrıntılar şuraya yazıldı:\n{0}"),
        ["Session"] = ("Session", "Oturum"),
        ["Samples"] = ("samples", "örnek"),
        ["GpuLoad"] = ("GPU load", "GPU yükü"),
        ["CpuLoad"] = ("CPU load", "CPU yükü"),
        ["ChartTitle"] = ("Last 5 minutes", "Son 5 dakika"),
        ["LegendWall"] = ("Wall (est.)", "Duvar (tahmin)"),
        ["LegendDc"] = ("DC total", "Toplam DC"),
        ["LegendGpu"] = ("GPU", "GPU"),
        ["LegendCpu"] = ("CPU", "CPU"),
        ["LegendOther"] = ("Other", "Diğer"),
        ["Settings"] = ("Settings", "Ayarlar"),
        ["Reset"] = ("Reset session", "Oturumu sıfırla"),
        ["CsvOff"] = ("CSV logging is off", "CSV kaydı kapalı"),
        ["CsvOn"] = ("CSV log: {0}", "CSV kaydı: {0}"),
        ["CsvError"] = ("CSV logging stopped: {0}", "CSV kaydı durdu: {0}"),
        ["OpenFolder"] = ("Open folder", "Klasörü aç"),
        ["TrayShow"] = ("Show window", "Pencereyi göster"),
        ["TrayExit"] = ("Exit", "Çıkış"),
        ["TrayTip"] = ("{0} W at the wall (estimate)", "Duvarda {0} W (tahmin)"),
        ["TrayTipNoGpu"] = ("{0} W at the wall (estimate, GPU not included)", "Duvarda {0} W (tahmin, GPU hariç)"),
        ["TrayStillRunning"] = (
            "PC Power Meter keeps running in the notification area.",
            "PC Power Meter bildirim alanında çalışmaya devam ediyor."),
        ["SettingsTitle"] = ("Settings", "Ayarlar"),
        ["SetInterval"] = ("Sampling interval (seconds)", "Ölçüm aralığı (saniye)"),
        ["SetPsu"] = ("PSU efficiency (%)", "PSU verimi (%)"),
        ["SetOther"] = ("Other components (W)", "Diğer bileşenler (W)"),
        ["SetOtherHint"] = (
            "Motherboard, RAM, drives, fans, USB. A fixed estimate.",
            "Anakart, RAM, diskler, fanlar, USB. Sabit bir tahmin."),
        ["SetTdp"] = ("CPU TDP (W)", "CPU TDP (W)"),
        ["SetIdle"] = ("CPU idle power (W)", "CPU boşta güç (W)"),
        ["SetCpuHint"] = (
            "Used only when the CPU has no energy counter.",
            "Yalnız CPU'da enerji sayacı yoksa kullanılır."),
        ["SetLanguage"] = ("Language", "Dil"),
        ["LangAuto"] = ("System default", "Sistem dili"),
        ["SetCsv"] = ("Write a CSV log", "CSV kaydı yaz"),
        ["SetCsvFolder"] = ("CSV folder", "CSV klasörü"),
        ["Browse"] = ("Browse…", "Gözat…"),
        ["SetCloseToTray"] = (
            "Closing the window keeps the app running in the notification area",
            "Pencere kapatılınca uygulama bildirim alanında çalışmaya devam etsin"),
        ["Save"] = ("Save", "Kaydet"),
        ["Cancel"] = ("Cancel", "İptal"),
        ["InvalidValue"] = ("{0}: enter a number between {1} and {2}.", "{0}: {1} ile {2} arasında bir sayı girin."),
        ["SaveFailed"] = ("The settings could not be saved to disk.", "Ayarlar diske kaydedilemedi."),
        ["AlreadyRunning"] = ("PC Power Meter is already running.", "PC Power Meter zaten çalışıyor."),
    };

    public static bool IsTurkish { get; private set; }

    /// <summary>Culture for numbers shown in the UI.</summary>
    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>Resolves "auto" to the Windows display language.</summary>
    public static bool ResolveTurkish(string language) => language switch
    {
        AppSettings.LanguageTurkish => true,
        AppSettings.LanguageEnglish => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr",
    };

    /// <summary>Selects the language and publishes every string as an application resource.</summary>
    public static void Apply(string language, ResourceDictionary resources)
    {
        IsTurkish = ResolveTurkish((language ?? "").Trim().ToLowerInvariant());
        foreach (string key in Strings.Keys)
        {
            resources["L." + key] = T(key);
        }
    }

    public static string T(string key) =>
        Strings.TryGetValue(key, out var s) ? (IsTurkish ? s.Tr : s.En) : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>A whole percentage: "%90" in Turkish, "90%" in English.</summary>
    public static string Percent(double percent)
    {
        string number = percent.ToString("N0", Culture);
        return IsTurkish ? "%" + number : number + "%";
    }
}
