using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PcPowerMeter.Core;

/// <summary>A text in English and Turkish.</summary>
public sealed class LocalizedText
{
    public string En { get; set; } = "";
    public string? Tr { get; set; }

    public string Get(bool turkish) => turkish && !string.IsNullOrWhiteSpace(Tr) ? Tr : En;
}

/// <summary>One step of a tiered tariff. The user picks the tier; see the README.</summary>
public sealed class TariffTier
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public double Price { get; set; }

    /// <summary>Household limit for this tier in kWh per day, if the tariff defines one.</summary>
    public double? LimitKwhPerDay { get; set; }

    public LocalizedText? Note { get; set; }
}

/// <summary>A time-of-use period, "HH:mm" local time; an end before the start runs past midnight.</summary>
public sealed class TariffPeriod
{
    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public double Price { get; set; }
}

/// <summary>An electricity tariff. Prices are per kWh, final consumer prices with all taxes.</summary>
public sealed class Tariff
{
    public const string TypeFlat = "flat", TypeTiered = "tiered", TypeTimeOfUse = "time_of_use";

    public string Id { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public LocalizedText? Provider { get; set; }
    public string Currency { get; set; } = "";
    public string Type { get; set; } = "";
    public double? Price { get; set; }
    public List<TariffTier>? Tiers { get; set; }
    public List<TariffPeriod>? Periods { get; set; }
    public string ValidFrom { get; set; } = "";

    /// <summary>Statistical period the price comes from, e.g. "2026-S1" for Eurostat data.</summary>
    public string? DataPeriod { get; set; }

    public string Source { get; set; } = "";
    public LocalizedText? Note { get; set; }
}

public sealed class TariffCountry
{
    /// <summary>ISO 3166 alpha-2 code ("EU" for the EU average).</summary>
    public string Code { get; set; } = "";
    public LocalizedText Name { get; set; } = new();
    public List<Tariff> Tariffs { get; set; } = new();
}

/// <summary>
/// The tariff table (data/tariffs.json). A copy is embedded in the app; a newer validated
/// copy may come from the optional online update.
/// </summary>
public sealed partial class TariffTable
{
    public const int SupportedSchemaVersion = 1;

    /// <summary>Largest accepted response or file, in bytes.</summary>
    public const int MaxBytes = 256 * 1024;

    public int SchemaVersion { get; set; }

    /// <summary>Data revision; a higher number replaces a lower one.</summary>
    public int Version { get; set; }

    /// <summary>Date of the data, "yyyy-MM-dd".</summary>
    public string Updated { get; set; } = "";

    public List<TariffCountry> Countries { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };

    /// <summary>
    /// Upper price limit per kWh by currency. Anything above is treated as a data error
    /// (e.g. a price in cents or kuruş instead of euros or lira). Unknown currencies get a
    /// generous limit.
    /// </summary>
    private static readonly Dictionary<string, double> MaxPrice = new()
    {
        ["EUR"] = 3, ["USD"] = 3, ["GBP"] = 3, ["CHF"] = 3,
        ["TRY"] = 100, ["PLN"] = 15, ["SEK"] = 30, ["DKK"] = 25, ["NOK"] = 30, ["CZK"] = 80, ["HUF"] = 1000,
    };

    private const double MaxPriceOtherCurrencies = 10000;

    public IEnumerable<Tariff> AllTariffs => Countries.SelectMany(c => c.Tariffs);

    public Tariff? Find(string? id) => id is null ? null : AllTariffs.FirstOrDefault(t => t.Id == id);

    public TariffCountry? CountryOf(Tariff tariff) => Countries.FirstOrDefault(c => c.Tariffs.Contains(tariff));

    /// <summary>Parses and validates a table. Never throws; returns false with a reason instead.</summary>
    public static bool TryParse(string json, out TariffTable? table, out string error)
    {
        table = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty";
            return false;
        }
        if (json.Length > MaxBytes)
        {
            error = "too large";
            return false;
        }
        TariffTable? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TariffTable>(json, Options);
        }
        catch (JsonException ex)
        {
            error = "invalid JSON: " + ex.Message;
            return false;
        }
        catch (NotSupportedException ex)
        {
            error = "invalid JSON: " + ex.Message;
            return false;
        }
        if (parsed is null)
        {
            error = "empty";
            return false;
        }
        List<string> errors = parsed.Validate();
        if (errors.Count > 0)
        {
            error = string.Join("; ", errors.Take(5));
            return false;
        }
        table = parsed;
        error = "";
        return true;
    }

    /// <summary>The copy of data/tariffs.json built into the app.</summary>
    public static TariffTable LoadEmbedded()
    {
        using Stream stream = typeof(TariffTable).Assembly.GetManifestResourceStream("PcPowerMeter.Core.tariffs.json")
            ?? throw new InvalidOperationException("Embedded tariff table missing.");
        using var reader = new StreamReader(stream);
        if (!TryParse(reader.ReadToEnd(), out TariffTable? table, out string error))
        {
            throw new InvalidOperationException("Embedded tariff table is invalid: " + error);
        }
        return table!;
    }

    /// <summary>Schema and plausibility checks. Returns the problems found; empty means valid.</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != SupportedSchemaVersion)
        {
            errors.Add($"unsupported schemaVersion {SchemaVersion}");
            return errors;
        }
        if (Version < 1)
        {
            errors.Add("version must be 1 or higher");
        }
        if (!IsDate(Updated))
        {
            errors.Add("updated must be yyyy-MM-dd");
        }
        if (Countries is null || Countries.Count == 0)
        {
            errors.Add("no countries");
            return errors;
        }
        var ids = new HashSet<string>();
        foreach (TariffCountry? country in Countries)
        {
            if (country is null || country.Code is null || !CountryCodeRegex().IsMatch(country.Code))
            {
                errors.Add("country code must be two capital letters");
                continue;
            }
            CheckText(country.Name, $"{country.Code}: name", errors);
            if (country.Tariffs is null || country.Tariffs.Count == 0)
            {
                errors.Add($"{country.Code}: no tariffs");
                continue;
            }
            foreach (Tariff? tariff in country.Tariffs)
            {
                if (tariff is null)
                {
                    errors.Add($"{country.Code}: empty tariff");
                    continue;
                }
                ValidateTariff(tariff, ids, errors);
            }
        }
        return errors;
    }

    private static void ValidateTariff(Tariff t, HashSet<string> ids, List<string> errors)
    {
        string where = t.Id ?? "?";
        if (t.Id is null || !IdRegex().IsMatch(t.Id))
        {
            errors.Add($"{where}: id must be lower-case letters, digits and '-'");
        }
        else if (!ids.Add(t.Id))
        {
            errors.Add($"{where}: duplicate id");
        }
        CheckText(t.Name, where + ": name", errors);
        if (t.Currency is null || !CurrencyRegex().IsMatch(t.Currency))
        {
            errors.Add($"{where}: currency must be an ISO 4217 code");
            return;
        }
        if (!IsDate(t.ValidFrom))
        {
            errors.Add($"{where}: validFrom must be yyyy-MM-dd");
        }
        if (t.Source is null || !Uri.TryCreate(t.Source, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{where}: source must be an https URL");
        }
        if (t.Note is not null && (t.Note.En?.Length > 2000 || t.Note.Tr?.Length > 2000))
        {
            errors.Add($"{where}: note too long");
        }

        switch (t.Type)
        {
            case Tariff.TypeFlat:
                CheckPrice(t.Price, t.Currency, where, errors);
                break;
            case Tariff.TypeTiered:
                if (t.Tiers is null || t.Tiers.Count < 2)
                {
                    errors.Add($"{where}: a tiered tariff needs at least two tiers");
                    break;
                }
                var tierIds = new HashSet<string>();
                foreach (TariffTier? tier in t.Tiers)
                {
                    if (tier is null || tier.Id is null || !IdRegex().IsMatch(tier.Id) || !tierIds.Add(tier.Id))
                    {
                        errors.Add($"{where}: tier ids must be unique");
                        continue;
                    }
                    CheckText(tier.Name, $"{where}/{tier.Id}: name", errors);
                    CheckPrice(tier.Price, t.Currency, $"{where}/{tier.Id}", errors);
                    if (tier.LimitKwhPerDay is double limit && !(limit > 0 && limit < 1000))
                    {
                        errors.Add($"{where}/{tier.Id}: limitKwhPerDay out of range");
                    }
                }
                break;
            case Tariff.TypeTimeOfUse:
                if (t.Periods is null || t.Periods.Count == 0)
                {
                    errors.Add($"{where}: a time-of-use tariff needs periods");
                    break;
                }
                var covered = new int[MinutesPerDay];
                var periodIds = new HashSet<string>();
                foreach (TariffPeriod? p in t.Periods)
                {
                    // Ids key the schedule and the UI looks names up by them: they must be
                    // well-formed and unique; the display name comes from the period's name.
                    if (p is null || p.Id is null || !IdRegex().IsMatch(p.Id) || !periodIds.Add(p.Id))
                    {
                        errors.Add($"{where}: period ids must be lower-case letters, digits and '-', and unique");
                        continue;
                    }
                    if (!TryParseTime(p.Start, out int start) || !TryParseTime(p.End, out int end) || start == end)
                    {
                        errors.Add($"{where}: period times must be HH:mm and differ");
                        continue;
                    }
                    CheckText(p.Name, $"{where}/{p.Id}: name", errors);
                    CheckPrice(p.Price, t.Currency, $"{where}/{p.Id}", errors);
                    for (int m = start; m != end; m = (m + 1) % MinutesPerDay)
                    {
                        covered[m]++;
                    }
                }
                if (covered.Any(c => c != 1))
                {
                    errors.Add($"{where}: periods must cover the day exactly once");
                }
                break;
            default:
                errors.Add($"{where}: unknown type '{t.Type}'");
                break;
        }
    }

    private static void CheckPrice(double? price, string currency, string where, List<string> errors)
    {
        double max = MaxPrice.TryGetValue(currency, out double m) ? m : MaxPriceOtherCurrencies;
        if (price is not double p || !double.IsFinite(p) || p <= 0 || p > max)
        {
            errors.Add($"{where}: price must be above 0 and at most {max.ToString(CultureInfo.InvariantCulture)} {currency}/kWh");
        }
    }

    private static void CheckText(LocalizedText? text, string where, List<string> errors)
    {
        if (text is null || string.IsNullOrWhiteSpace(text.En) || text.En.Length > 200 || text.Tr?.Length > 200)
        {
            errors.Add(where + " missing or too long");
        }
    }

    private static bool IsDate(string? text) =>
        text is not null && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    internal const int MinutesPerDay = 24 * 60;

    /// <summary>Parses "HH:mm" (00:00-23:59) into minutes after midnight.</summary>
    public static bool TryParseTime(string? text, out int minutes)
    {
        minutes = 0;
        if (text is null || !TimeRegex().IsMatch(text))
        {
            return false;
        }
        int h = int.Parse(text.AsSpan(0, 2), CultureInfo.InvariantCulture);
        int m = int.Parse(text.AsSpan(3, 2), CultureInfo.InvariantCulture);
        if (h > 23 || m > 59)
        {
            return false;
        }
        minutes = h * 60 + m;
        return true;
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex CountryCodeRegex();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdRegex();

    [GeneratedRegex("^[0-9]{2}:[0-9]{2}$")]
    private static partial Regex TimeRegex();
}

/// <summary>
/// The price per kWh for any moment, resolved from a tariff and the user's choices.
/// Internally a list of non-overlapping day segments that cover 00:00-24:00.
/// </summary>
public sealed class PriceSchedule
{
    private readonly (int Start, int End, double Price, string Period)[] _segments;

    private PriceSchedule(string currency, (int Start, int End, double Price, string Period)[] segments)
    {
        Currency = currency;
        _segments = segments;
    }

    public string Currency { get; }

    public bool IsTimeOfUse => _segments.Select(s => s.Period).Distinct().Count() > 1;

    /// <summary>Each period once with its price, in the order of the day starting at 00:00.</summary>
    public IReadOnlyList<(string Period, double Price)> PeriodPrices =>
        _segments.Select(s => (s.Period, s.Price)).DistinctBy(p => p.Period).ToList();

    public static PriceSchedule Flat(string currency, double price) =>
        new(currency, new[] { (0, TariffTable.MinutesPerDay, price, "") });

    /// <summary>Periods may run past midnight (end before start); they must cover the day.</summary>
    public static PriceSchedule TimeOfUse(string currency, IEnumerable<(int Start, int End, double Price, string Period)> periods)
    {
        var segments = new List<(int, int, double, string)>();
        foreach (var (start, end, price, period) in periods)
        {
            if (end > start)
            {
                segments.Add((start, end, price, period));
            }
            else
            {
                segments.Add((start, TariffTable.MinutesPerDay, price, period));
                if (end > 0)
                {
                    segments.Add((0, end, price, period));
                }
            }
        }
        return new PriceSchedule(currency, segments.OrderBy(s => s.Item1).ToArray());
    }

    /// <summary>
    /// Builds the schedule for a validated tariff. For a tiered tariff the user's tier is
    /// used; an unknown or missing tier id falls back to the first (lowest) tier.
    /// </summary>
    public static PriceSchedule FromTariff(Tariff tariff, string? tierId) => tariff.Type switch
    {
        Tariff.TypeTiered => Flat(tariff.Currency, SelectTier(tariff, tierId).Price),
        Tariff.TypeTimeOfUse => TimeOfUse(tariff.Currency, tariff.Periods!.Select(p =>
        {
            TariffTable.TryParseTime(p.Start, out int s);
            TariffTable.TryParseTime(p.End, out int e);
            return (s, e, p.Price, p.Id);
        })),
        _ => Flat(tariff.Currency, tariff.Price ?? 0),
    };

    public static TariffTier SelectTier(Tariff tariff, string? tierId) =>
        tariff.Tiers!.FirstOrDefault(t => t.Id == tierId) ?? tariff.Tiers![0];

    /// <summary>Price per kWh at a local time.</summary>
    public double PriceAt(DateTime time)
    {
        int minute = (int)time.TimeOfDay.TotalMinutes;
        foreach (var s in _segments)
        {
            if (minute >= s.Start && minute < s.End)
            {
                return s.Price;
            }
        }
        return _segments[^1].Price;
    }

    /// <summary>Id of the time-of-use period at a local time ("" for a flat price).</summary>
    public string PeriodAt(DateTime time)
    {
        int minute = (int)time.TimeOfDay.TotalMinutes;
        foreach (var s in _segments)
        {
            if (minute >= s.Start && minute < s.End)
            {
                return s.Period;
            }
        }
        return _segments[^1].Period;
    }

    /// <summary>
    /// Average price over the hour that starts at <paramref name="hourStart"/>, weighting
    /// each period by its minutes in that hour. With period limits on full hours (as in
    /// all current tariffs) this is simply the price of the period the hour belongs to.
    /// </summary>
    public double HourPrice(DateTime hourStart)
    {
        int from = hourStart.Hour * 60, to = from + 60;
        double sum = 0;
        foreach (var s in _segments)
        {
            int overlap = Math.Min(to, s.End) - Math.Max(from, s.Start);
            if (overlap > 0)
            {
                sum += overlap * s.Price;
            }
        }
        return sum / 60.0;
    }

    /// <summary>Cost of an energy amount in watt-hours used during the given hour.</summary>
    public double HourCost(DateTime hourStart, double wattHours) => wattHours / 1000.0 * HourPrice(hourStart);
}
