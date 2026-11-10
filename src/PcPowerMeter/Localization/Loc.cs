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

        // Other components
        ["SrcComponents"] = ("component estimate", "bileşen tahmini"),
        ["OtherHover"] = ("{0} items · hover or click for details", "{0} kalem · ayrıntı için üzerine gelin ya da tıklayın"),
        ["OtherFixedHover"] = ("fixed value from Settings", "Ayarlar'daki sabit değer"),
        ["BreakdownTitle"] = ("Other components (estimate)", "Diğer bileşenler (tahmin)"),
        ["BreakdownTotal"] = ("Total", "Toplam"),
        ["BreakdownNote"] = (
            "Typical values, not measurements. Change them in Settings > Other components.",
            "Tipik değerlerdir, ölçüm değildir. Ayarlar > Diğer bileşenler'den değiştirebilirsiniz."),
        ["BreakdownFixed"] = (
            "A fixed value of {0} W set in Settings. You can switch to the component estimate in Settings > Other components.",
            "Ayarlar'da girilen sabit {0} W. Ayarlar > Diğer bileşenler'den bileşen tahminine geçebilirsiniz."),
        ["CompBoard"] = ("Motherboard / chipset", "Anakart / yonga seti"),
        ["CompRam"] = ("RAM modules", "RAM modülleri"),
        ["CompCaseFans"] = ("Case fans (incl. radiator fans)", "Kasa fanları (radyatör fanları dahil)"),
        ["CompCooler"] = ("CPU cooler fan / AIO pump", "CPU soğutucu fanı / AIO pompası"),
        ["CompNvme"] = ("NVMe SSDs", "NVMe SSD'ler"),
        ["CompSataSsd"] = ("SATA SSDs", "SATA SSD'ler"),
        ["CompHdd"] = ("Hard disks (HDD)", "Sabit diskler (HDD)"),
        ["CompRgb"] = ("RGB lighting", "RGB aydınlatma"),
        ["CompUsb"] = ("USB devices allowance", "USB cihazları payı"),
        ["CompExtra"] = ("Extra (anything else)", "Ek (diğer her şey)"),
        ["OptEntry"] = ("Entry", "Giriş"),
        ["OptMainstream"] = ("Mainstream", "Orta"),
        ["OptHighEnd"] = ("High-end", "Üst"),
        ["OptNone"] = ("None", "Yok"),
        ["OptAir"] = ("Air fan", "Hava fanı"),
        ["OptAio"] = ("AIO pump", "AIO pompa"),
        ["OptFew"] = ("A little", "Az"),
        ["OptMany"] = ("A lot", "Çok"),
        ["ColCount"] = ("Count", "Adet"),
        ["ColWattsEach"] = ("W each", "W/adet"),

        // Settings tabs
        ["TabGeneral"] = ("General", "Genel"),
        ["TabOther"] = ("Other components", "Diğer bileşenler"),
        ["TabCost"] = ("Electricity cost", "Elektrik maliyeti"),
        ["SetOtherFixedMode"] = ("Fixed value (W)", "Sabit değer (W)"),
        ["SetOtherComponentsMode"] = ("Estimate from my components", "Bileşenlerimden tahmin et"),
        ["SetOtherIntro"] = (
            "Everything in the case except the CPU package and the GPU. This cannot be measured here: motherboard sensors do not report power without a kernel driver. The values are typical; adjust them to your PC.",
            "CPU paketi ve GPU dışında kasadaki her şey. Burada ölçülemez: anakart sensörleri çekirdek sürücüsü olmadan güç bildirmez. Değerler tipiktir; kendi bilgisayarınıza göre ayarlayın."),
        ["SetOtherTotal"] = ("Total: {0} W (estimate)", "Toplam: {0} W (tahmin)"),

        // Electricity cost settings
        ["SetTariff"] = ("Tariff", "Tarife"),
        ["TariffAuto"] = ("Automatic (Windows region {0}): {1}", "Otomatik (Windows bölgesi {0}): {1}"),
        ["TariffAutoNone"] = ("no tariff for this region, cost not shown", "bu bölge için tarife yok, maliyet gösterilmez"),
        ["TariffOff"] = ("Off (do not show cost)", "Kapalı (maliyet gösterme)"),
        ["TariffManual"] = ("My own price", "Kendi fiyatım"),
        ["SetTier"] = ("Tier", "Kademe"),
        ["SetTierHint"] = (
            "The tier depends on the whole household's consumption, not on the PC alone (the PC is only part of it). Check your bill: the low tier applies while the household averages at most {0} kWh a day (about {1} kWh a month).",
            "Kademe bilgisayarın değil, tüm hanenin tüketimine bağlıdır (bilgisayar bunun yalnız bir parçası). Faturanıza bakın: hanenin günlük ortalaması en fazla {0} kWh (ayda yaklaşık {1} kWh) iken düşük kademe uygulanır."),
        ["SetManualCurrency"] = ("Currency (ISO code, e.g. TRY, EUR)", "Para birimi (ISO kodu, ör. TRY, EUR)"),
        ["SetManualFlat"] = ("One price", "Tek fiyat"),
        ["SetManualTou"] = ("Three-time", "Üç zamanlı"),
        ["SetManualPrice"] = ("Price per kWh, taxes included", "kWh fiyatı, vergiler dahil"),
        ["SetManualDay"] = ("Day 06:00-17:00, per kWh", "Gündüz 06:00-17:00, kWh başına"),
        ["SetManualPeak"] = ("Peak 17:00-22:00, per kWh", "Puant 17:00-22:00, kWh başına"),
        ["SetManualNight"] = ("Night 22:00-06:00, per kWh", "Gece 22:00-06:00, kWh başına"),
        ["InvalidCurrency"] = ("Currency: enter a three-letter code such as EUR.", "Para birimi: EUR gibi üç harfli bir kod girin."),
        ["TariffValidFrom"] = ("Valid from {0}", "Geçerlilik başlangıcı {0}"),
        ["TariffDataPeriod"] = ("data period {0}", "veri dönemi {0}"),
        ["TariffSource"] = ("Source", "Kaynak"),
        ["TableInfo"] = ("Tariff table version {0}, dated {1} ({2}).", "Tarife tablosu sürüm {0}, tarih {1} ({2})."),
        ["TableBuiltIn"] = ("built into the app", "uygulamaya gömülü"),
        ["TableDownloaded"] = ("downloaded update", "indirilen güncelleme"),
        ["SetOnline"] = ("Update tariffs online", "Tarifeleri çevrimiçi güncelle"),
        ["SetOnlineHint"] = (
            "Off by default. When on, the app downloads data/tariffs.json from the project's GitHub repository at most once a day: a plain request without cookies, IDs or any of your data. This is the only network access of the app.",
            "Varsayılan olarak kapalıdır. Açıkken uygulama, projenin GitHub deposundan data/tariffs.json dosyasını günde en fazla bir kez indirir: çerez, kimlik ya da hiçbir verinizi içermeyen düz bir istek. Uygulamanın tek ağ erişimi budur."),
        ["SetOnlineLast"] = ("Last check: {0} ({1})", "Son kontrol: {0} ({1})"),
        ["SetOnlineNever"] = ("Not checked yet.", "Henüz kontrol edilmedi."),

        // Cost in the main window
        ["CostLine"] = ("Cost (estimate): {0}/h now · this session {1}", "Maliyet (tahmini): şu an {0}/sa · bu oturum {1}"),
        ["CostMonthly"] = ("≈ {0} a month at the last {1} day(s)' rate", "son {1} günün hızıyla ayda ≈ {0}"),
        ["CostNone"] = (
            "Electricity cost: no price set. Choose a tariff in Settings > Electricity cost.",
            "Elektrik maliyeti: fiyat seçilmedi. Ayarlar > Elektrik maliyeti'nden bir tarife seçin."),

        // Usage window
        ["Usage"] = ("Usage", "Kullanım"),
        ["UsageTitle"] = ("Usage history", "Kullanım geçmişi"),
        ["UsageNote"] = (
            "Energy at the wall (estimate) and its estimated cost. The app can only measure while it is running; \"Measured\" shows for how long it did. Costs use the current tariff for all hours.",
            "Duvardaki enerji (tahmin) ve tahmini maliyeti. Uygulama yalnız açıkken ölçebilir; \"Ölçülen süre\" ne kadar ölçtüğünü gösterir. Maliyetler tüm saatler için şu anki tarifeyle hesaplanır."),
        ["PeriodToday"] = ("Today", "Bugün"),
        ["PeriodWeek"] = ("This week", "Bu hafta"),
        ["PeriodMonth"] = ("This month", "Bu ay"),
        ["Period12Months"] = ("Last 12 months", "Son 12 ay"),
        ["PeriodAll"] = ("All time", "Tüm zamanlar"),
        ["ColPeriod"] = ("Period", "Dönem"),
        ["ColKwh"] = ("kWh (est.)", "kWh (tahmin)"),
        ["ColCost"] = ("Cost (est.)", "Maliyet (tahmin)"),
        ["ColMeasured"] = ("Measured", "Ölçülen süre"),
        ["ColNoGpu"] = ("Without GPU", "GPU'suz"),
        ["ColMonth"] = ("Month", "Ay"),
        ["UsageProjection"] = (
            "At the last {0} day(s)' rate: ≈ {1} kWh and {2} a month (estimate).",
            "Son {0} günün hızıyla: ayda ≈ {1} kWh ve {2} (tahmin)."),
        ["UsageProjectionNoCost"] = (
            "At the last {0} day(s)' rate: ≈ {1} kWh a month (estimate).",
            "Son {0} günün hızıyla: ayda ≈ {1} kWh (tahmin)."),
        ["UsageEmpty"] = ("No usage recorded yet.", "Henüz kullanım kaydı yok."),
        ["DailyChartTitle"] = ("Last 30 days (kWh at the wall, estimate)", "Son 30 gün (duvarda kWh, tahmin)"),
        ["MonthlyTitle"] = ("Monthly summary", "Aylık özet"),
        ["ExportHourly"] = ("Export hourly CSV…", "Saatlik CSV dışa aktar…"),
        ["ExportDaily"] = ("Export daily CSV…", "Günlük CSV dışa aktar…"),
        ["ResetHistory"] = ("Reset history…", "Geçmişi sıfırla…"),
        ["ResetHistoryConfirm"] = (
            "Delete the whole usage history? This cannot be undone. (CSV logs are not affected.)",
            "Tüm kullanım geçmişi silinsin mi? Bu geri alınamaz. (CSV kayıtları etkilenmez.)"),
        ["ExportFailed"] = ("The file could not be written: {0}", "Dosya yazılamadı: {0}"),
        ["HistoryError"] = ("Usage history could not be saved: {0}", "Kullanım geçmişi kaydedilemedi: {0}"),
        ["Close"] = ("Close", "Kapat"),
        ["TariffLine"] = ("Prices: {0}", "Fiyatlar: {0}"),
        ["NoPrice"] = ("no price set", "fiyat seçilmedi"),
        ["DurationHm"] = ("{0} h {1} min", "{0} sa {1} dk"),
        ["InvalidDataDir"] = ("The data folder is not a valid path:\n{0}", "Veri klasörü geçerli bir yol değil:\n{0}"),
        ["Tou_day"] = ("day", "gündüz"),
        ["Tou_peak"] = ("peak", "puant"),
        ["Tou_night"] = ("night", "gece"),
        ["TouOther"] = ("period", "dilim"),
    };

    public static bool IsTurkish { get; private set; }

    /// <summary>Culture for numbers shown in the UI.</summary>
    public static CultureInfo Culture => CultureInfo.CurrentCulture;

    /// <summary>
    /// Culture for month and day names: the Windows format when it matches the UI language,
    /// otherwise the UI language, so an English UI never shows Turkish month names or vice versa.
    /// </summary>
    public static CultureInfo DateCulture =>
        (CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "tr") == IsTurkish
            ? CultureInfo.CurrentCulture
            : CultureInfo.GetCultureInfo(IsTurkish ? "tr-TR" : "en-US");

    /// <summary>Energy in kWh with two decimals, three for small values so they do not show as 0.</summary>
    public static string Kwh(double kwh) => kwh.ToString(kwh != 0 && Math.Abs(kwh) < 1 ? "N3" : "N2", Culture);

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

    /// <summary>The string for <paramref name="key"/>, or null when there is none (never the raw key).</summary>
    public static string? TryT(string key) =>
        Strings.TryGetValue(key, out var s) ? (IsTurkish ? s.Tr : s.En) : null;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>An amount of money with its currency, e.g. "1,23 TL" or "0.35 €".</summary>
    public static string Money(double amount, string currency, int decimals = 2)
    {
        string symbol = currency switch
        {
            "TRY" => "TL",
            "EUR" => "€",
            "USD" => "$",
            "GBP" => "£",
            _ => currency,
        };
        return amount.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), Culture) + " " + symbol;
    }

    /// <summary>A duration as hours and minutes, e.g. "12 h 05 min".</summary>
    public static string Duration(double seconds)
    {
        long minutes = (long)Math.Round(Math.Max(seconds, 0) / 60);
        return F("DurationHm", (minutes / 60).ToString("N0", Culture), (minutes % 60).ToString("00", Culture));
    }

    /// <summary>A whole percentage: "%90" in Turkish, "90%" in English.</summary>
    public static string Percent(double percent)
    {
        string number = percent.ToString("N0", Culture);
        return IsTurkish ? "%" + number : number + "%";
    }
}
