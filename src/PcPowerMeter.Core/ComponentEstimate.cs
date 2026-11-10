using System.Text.Json.Serialization;

namespace PcPowerMeter.Core;

/// <summary>One line of the "other components" breakdown.</summary>
/// <param name="Key">Stable key of the item (also the UI string key suffix).</param>
/// <param name="Count">Number of units; 1 for items that have no count.</param>
/// <param name="UnitWatts">Watts per unit.</param>
public readonly record struct ComponentItem(string Key, int Count, double UnitWatts)
{
    public double Watts => Count * UnitWatts;
}

/// <summary>
/// Estimate of everything inside the case except the CPU package and the GPU, built from
/// the user's hardware. All values are typical DC draws while the PC is in use, not
/// measurements; the user can change every one of them.
/// </summary>
/// <remarks>
/// Sources of the typical values (rounded, deliberately middle-of-the-range):
/// - 120/140 mm case fan: rated 0.1-0.3 A at 12 V (1.2-3.6 W at full speed); at normal
///   speeds about 1-2 W.
/// - CPU air cooler fan about 2 W; an AIO pump about 3-6 W (radiator fans count as case fans).
/// - NVMe SSD: 0.05-1 W idle, 3-8 W under load; average in desktop use about 2 W.
/// - SATA SSD: about 0.3-0.5 W idle, 2-3 W active; about 1 W average.
/// - 3.5" HDD: about 4-6 W idle, 6-9 W active; 6 W.
/// - RAM: DDR4 about 2-4 W per module, DDR5 (with its own PMIC) about 3-5 W per module.
/// - Motherboard: chipset (about 5-15 W, two-chip X670E boards at the top), onboard LAN,
///   audio and USB controllers and conversion losses: about 12 / 20 / 30 W for entry,
///   mainstream and high-end boards.
/// - RGB: a few LEDs about 3-5 W, many strips and RGB fans 10-15 W.
/// - USB: keyboard, mouse and a headset together about 2-3 W.
/// </remarks>
public sealed record ComponentEstimate
{
    public const string CoolerNone = "none", CoolerAir = "air", CoolerAio = "aio";
    public const string RamDdr4 = "ddr4", RamDdr5 = "ddr5";
    public const string BoardEntry = "entry", BoardMainstream = "mainstream", BoardHighEnd = "high";
    public const string RgbNone = "none", RgbFew = "few", RgbMany = "many";

    public const int MaxCount = 32;
    public const double MaxUnitWatts = 500;

    public int CaseFans { get; set; } = 3;
    public double CaseFanWatts { get; set; } = 1.5;

    public string Cooler { get; set; } = CoolerAir;
    public double CoolerWatts { get; set; } = DefaultCoolerWatts(CoolerAir);

    public int NvmeDrives { get; set; } = 1;
    public double NvmeWatts { get; set; } = 2.0;

    public int SataSsds { get; set; }
    public double SataSsdWatts { get; set; } = 1.0;

    public int Hdds { get; set; }
    public double HddWatts { get; set; } = 6.0;

    public int RamModules { get; set; } = 2;
    public string RamType { get; set; } = RamDdr5;
    public double RamModuleWatts { get; set; } = DefaultRamWatts(RamDdr5);

    public string Board { get; set; } = BoardMainstream;
    public double BoardWatts { get; set; } = DefaultBoardWatts(BoardMainstream);

    public string Rgb { get; set; } = RgbNone;
    public double RgbWatts { get; set; } = DefaultRgbWatts(RgbNone);

    public double UsbWatts { get; set; } = 2.5;

    /// <summary>Anything else the user wants to add (sound card, capture card, ...).</summary>
    public double ExtraWatts { get; set; }

    public static double DefaultCoolerWatts(string type) => type switch
    {
        CoolerNone => 0,
        CoolerAio => 4.5,
        _ => 2.0,
    };

    public static double DefaultRamWatts(string type) => type == RamDdr4 ? 3.0 : 4.0;

    public static double DefaultBoardWatts(string type) => type switch
    {
        BoardEntry => 12,
        BoardHighEnd => 30,
        _ => 20,
    };

    public static double DefaultRgbWatts(string type) => type switch
    {
        RgbFew => 4,
        RgbMany => 12,
        _ => 0,
    };

    /// <summary>The individual items; items with zero watts are included so the UI can list them.</summary>
    public IReadOnlyList<ComponentItem> Breakdown()
    {
        ComponentEstimate n = Normalized();
        return new[]
        {
            new ComponentItem("Board", 1, n.BoardWatts),
            new ComponentItem("Ram", n.RamModules, n.RamModuleWatts),
            new ComponentItem("CaseFans", n.CaseFans, n.CaseFanWatts),
            new ComponentItem("Cooler", 1, n.CoolerWatts),
            new ComponentItem("Nvme", n.NvmeDrives, n.NvmeWatts),
            new ComponentItem("SataSsd", n.SataSsds, n.SataSsdWatts),
            new ComponentItem("Hdd", n.Hdds, n.HddWatts),
            new ComponentItem("Rgb", 1, n.RgbWatts),
            new ComponentItem("Usb", 1, n.UsbWatts),
            new ComponentItem("Extra", 1, n.ExtraWatts),
        };
    }

    /// <summary>Sum of all items in watts.</summary>
    [JsonIgnore]
    public double TotalWatts => Breakdown().Sum(i => i.Watts);

    /// <summary>Returns a copy with counts and watts forced into their valid ranges.</summary>
    public ComponentEstimate Normalized() => this with
    {
        CaseFans = Count(CaseFans),
        CaseFanWatts = Watts(CaseFanWatts, 1.5),
        Cooler = Cooler is CoolerNone or CoolerAir or CoolerAio ? Cooler : CoolerAir,
        CoolerWatts = Watts(CoolerWatts, 2.0),
        NvmeDrives = Count(NvmeDrives),
        NvmeWatts = Watts(NvmeWatts, 2.0),
        SataSsds = Count(SataSsds),
        SataSsdWatts = Watts(SataSsdWatts, 1.0),
        Hdds = Count(Hdds),
        HddWatts = Watts(HddWatts, 6.0),
        RamModules = Count(RamModules),
        RamType = RamType is RamDdr4 or RamDdr5 ? RamType : RamDdr5,
        RamModuleWatts = Watts(RamModuleWatts, 4.0),
        Board = Board is BoardEntry or BoardMainstream or BoardHighEnd ? Board : BoardMainstream,
        BoardWatts = Watts(BoardWatts, 20),
        Rgb = Rgb is RgbNone or RgbFew or RgbMany ? Rgb : RgbNone,
        RgbWatts = Watts(RgbWatts, 0),
        UsbWatts = Watts(UsbWatts, 2.5),
        ExtraWatts = Watts(ExtraWatts, 0),
    };

    private static int Count(int value) => Math.Clamp(value, 0, MaxCount);

    private static double Watts(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, MaxUnitWatts) : fallback;
}
