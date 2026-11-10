using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PcPowerMeter.Core;

/// <summary>The tariff the user ended up with, or none (cost not shown).</summary>
/// <param name="Schedule">Null when no price is set: cost is then not shown.</param>
/// <param name="Tariff">The table entry, or null for a manual price.</param>
/// <param name="Tier">The chosen tier of a tiered tariff.</param>
/// <param name="Automatic">True when the tariff was picked from the Windows region.</param>
public sealed record TariffChoice(PriceSchedule? Schedule, Tariff? Tariff, TariffTier? Tier, bool Manual, bool Automatic)
{
    public static readonly TariffChoice None = new(null, null, null, false, false);
}

public static class TariffResolver
{
    /// <summary>Standard day / peak / night limits used for a manual three-time price (as in Türkiye).</summary>
    public const int DayStart = 6 * 60, PeakStart = 17 * 60, NightStart = 22 * 60;

    /// <summary>
    /// Resolves the settings to a price schedule. Automatic (no tariff id) uses the first
    /// tariff of the Windows region's country, if the table has one.
    /// </summary>
    public static TariffChoice Resolve(TariffTable table, AppSettings settings, string? regionCode)
    {
        switch (settings.TariffId)
        {
            case AppSettings.TariffOff:
                return TariffChoice.None;
            case AppSettings.TariffManual:
                return new TariffChoice(ManualSchedule(settings, regionCode), null, null, true, false);
        }

        bool automatic = settings.TariffId is null;
        Tariff? tariff = automatic
            ? table.Countries.FirstOrDefault(c => c.Code == regionCode?.ToUpperInvariant())?.Tariffs.FirstOrDefault()
            : table.Find(settings.TariffId);
        if (tariff is null)
        {
            return TariffChoice.None;
        }
        TariffTier? tier = tariff.Type == Tariff.TypeTiered ? PriceSchedule.SelectTier(tariff, settings.TariffTier) : null;
        return new TariffChoice(PriceSchedule.FromTariff(tariff, settings.TariffTier), tariff, tier, false, automatic);
    }

    /// <summary>The manual price, or null while it has not been entered (all prices 0).</summary>
    public static PriceSchedule? ManualSchedule(AppSettings settings, string? regionCode)
    {
        string currency = settings.ManualCurrency ?? RegionCurrency(regionCode) ?? "EUR";
        if (!settings.ManualTimeOfUse)
        {
            return settings.ManualPrice > 0 ? PriceSchedule.Flat(currency, settings.ManualPrice) : null;
        }
        if (settings.ManualDayPrice <= 0 || settings.ManualPeakPrice <= 0 || settings.ManualNightPrice <= 0)
        {
            return null;
        }
        return PriceSchedule.TimeOfUse(currency, new[]
        {
            (DayStart, PeakStart, settings.ManualDayPrice, "day"),
            (PeakStart, NightStart, settings.ManualPeakPrice, "peak"),
            (NightStart, DayStart, settings.ManualNightPrice, "night"),
        });
    }

    public static string? RegionCurrency(string? regionCode)
    {
        try
        {
            return regionCode is { Length: 2 } ? new RegionInfo(regionCode).ISOCurrencySymbol : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>Result of an online tariff check.</summary>
public enum TariffCheckResult
{
    /// <summary>Not checked: the last check was less than a day ago.</summary>
    Skipped,
    /// <summary>A newer valid table was downloaded and cached.</summary>
    Updated,
    /// <summary>The online table is not newer than the one in use.</summary>
    UpToDate,
    /// <summary>Network error, HTTP error or an invalid table; nothing changed.</summary>
    Failed,
}

/// <summary>When the online check last ran and how it went (tariffs-update.json).</summary>
public sealed class TariffUpdateState
{
    public DateTime? LastCheckUtc { get; set; }
    public string? LastResult { get; set; }
}

/// <summary>
/// Provides the tariff table: the embedded copy, or a newer validated copy cached from the
/// optional online update. The online update is the app's only network access: a plain
/// GET of one fixed URL, at most once a day, only when the user has turned it on.
/// </summary>
public sealed class TariffRepository
{
    public const string UpdateUrl = "https://raw.githubusercontent.com/Zeruh31/pc-power-meter/main/data/tariffs.json";

    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions StateOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _cachePath;
    private readonly string _statePath;
    private readonly object _lock = new();
    private TariffTable _table;

    public TariffRepository(string dataFolder)
    {
        _cachePath = Path.Combine(dataFolder, "tariffs-cache.json");
        _statePath = Path.Combine(dataFolder, "tariffs-update.json");
        Embedded = TariffTable.LoadEmbedded();
        _table = Embedded;
        TableFromCache = false;
        TariffTable? cached = ReadCache(_cachePath);
        if (cached is not null && cached.Version > Embedded.Version)
        {
            _table = cached;
            TableFromCache = true;
        }
    }

    public TariffTable Embedded { get; }

    /// <summary>The table in use: the cached online copy if it is newer, otherwise the embedded one.</summary>
    public TariffTable Table
    {
        get
        {
            lock (_lock)
            {
                return _table;
            }
        }
    }

    public bool TableFromCache { get; private set; }

    /// <summary>Reads a cached table; a missing, corrupt or invalid cache gives null.</summary>
    public static TariffTable? ReadCache(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > TariffTable.MaxBytes)
            {
                return null;
            }
            return TariffTable.TryParse(File.ReadAllText(path, Encoding.UTF8), out TariffTable? table, out _) ? table : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public TariffUpdateState ReadState()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                return JsonSerializer.Deserialize<TariffUpdateState>(File.ReadAllText(_statePath), StateOptions) ?? new();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        return new TariffUpdateState();
    }

    private void WriteState(TariffUpdateState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            File.WriteAllText(_statePath, JsonSerializer.Serialize(state, StateOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Checks the online table if the last check is at least a day old. Never throws for
    /// network or data problems: the table in use simply stays as it is.
    /// </summary>
    /// <param name="handler">Test hook; null uses a plain <see cref="SocketsHttpHandler"/>.</param>
    public async Task<TariffCheckResult> CheckOnlineAsync(string appVersion, DateTime nowUtc,
        HttpMessageHandler? handler = null, CancellationToken cancel = default)
    {
        TariffUpdateState state = ReadState();
        if (state.LastCheckUtc is DateTime last && nowUtc - last < CheckInterval && nowUtc >= last)
        {
            return TariffCheckResult.Skipped;
        }
        // Record the attempt first, so a failing server is not asked again within the day.
        state.LastCheckUtc = nowUtc;
        state.LastResult = "checking";
        WriteState(state);

        (TariffCheckResult result, string detail) = await DownloadAsync(appVersion, handler, cancel).ConfigureAwait(false);
        state.LastResult = detail;
        WriteState(state);
        return result;
    }

    private async Task<(TariffCheckResult, string)> DownloadAsync(string appVersion, HttpMessageHandler? handler, CancellationToken cancel)
    {
        // No cookies, no credentials, no redirects to other hosts, no query string; the
        // only identifying header is the generic product/version User-Agent.
        HttpMessageHandler h = handler ?? new SocketsHttpHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            Credentials = null,
            PreAuthenticate = false,
            ConnectTimeout = Timeout,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        using var client = new HttpClient(h, disposeHandler: true)
        {
            Timeout = Timeout,
            MaxResponseContentBufferSize = TariffTable.MaxBytes,
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, UpdateUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("PcPowerMeter", appVersion));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

        string json;
        try
        {
            using HttpResponseMessage response = await client.SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (TariffCheckResult.Failed, "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }
            json = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return (TariffCheckResult.Failed, ex is TaskCanceledException or OperationCanceledException ? "timeout" : "network error");
        }

        if (!TariffTable.TryParse(json, out TariffTable? online, out string error))
        {
            return (TariffCheckResult.Failed, "invalid table: " + Shorten(error));
        }
        lock (_lock)
        {
            if (online!.Version <= _table.Version)
            {
                return (TariffCheckResult.UpToDate, "up to date");
            }
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            string temp = _cachePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Use it for this run anyway; it will be downloaded again tomorrow.
        }
        lock (_lock)
        {
            _table = online;
            TableFromCache = true;
        }
        return (TariffCheckResult.Updated, "updated");
    }

    private static string Shorten(string text) => text.Length > 120 ? text[..120] : text;
}
