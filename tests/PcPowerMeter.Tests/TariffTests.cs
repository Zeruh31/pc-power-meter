using System.Net;
using System.Text.Json.Nodes;
using PcPowerMeter.Core;

namespace PcPowerMeter.Tests;

public sealed class TariffTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pcpm-tests-" + Guid.NewGuid().ToString("N"));

    public TariffTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly TariffTable Table = TariffTable.LoadEmbedded();

    private static string EmbeddedJson()
    {
        using Stream s = typeof(TariffTable).Assembly.GetManifestResourceStream("PcPowerMeter.Core.tariffs.json")!;
        return new StreamReader(s).ReadToEnd();
    }

    private static PriceSchedule ThreeTime() => PriceSchedule.FromTariff(Table.Find("tr-residential-three-time")!, null);

    // ------------------------------------------------------------------ embedded table

    [Fact]
    public void EmbeddedTable_IsValid_AndHasTheTurkishTariffs()
    {
        Assert.Empty(Table.Validate());
        Tariff single = Table.Find("tr-residential-single")!;
        Assert.Equal(Tariff.TypeTiered, single.Type);
        Assert.Equal("TRY", single.Currency);
        Assert.Equal("2026-04-04", single.ValidFrom);
        Assert.Equal(Tariff.TypeTimeOfUse, Table.Find("tr-residential-three-time")!.Type);
    }

    [Theory]
    // (energy + 5 % BTV x energy + distribution 242.49) x 1.10, kr -> TL, 4 decimals.
    [InlineData("tr-residential-single", "low", 49.4065)]
    [InlineData("tr-residential-single", "high", 189.5808)]
    public void TurkishTierPrices_FollowTheFormula(string id, string tier, double energyKurus)
    {
        double expected = Math.Round((energyKurus * 1.05 + 242.49) * 1.10 / 100, 4, MidpointRounding.AwayFromZero);
        Tariff t = Table.Find(id)!;

        Assert.Equal(expected, t.Tiers!.Single(x => x.Id == tier).Price, 6);
    }

    [Theory]
    [InlineData("day", 195.0640)]
    [InlineData("peak", 374.7551)]
    [InlineData("night", 51.3656)]
    public void TurkishThreeTimePrices_FollowTheFormula(string period, double energyKurus)
    {
        double expected = Math.Round((energyKurus * 1.05 + 242.49) * 1.10 / 100, 4, MidpointRounding.AwayFromZero);

        Assert.Equal(expected, Table.Find("tr-residential-three-time")!.Periods!.Single(p => p.Id == period).Price, 6);
    }

    // ------------------------------------------------------------------ three-time pricing

    [Theory]
    [InlineData(5, 59, "night")]
    [InlineData(6, 0, "day")]
    [InlineData(16, 59, "day")]
    [InlineData(17, 0, "peak")]
    [InlineData(21, 59, "peak")]
    [InlineData(22, 0, "night")]
    [InlineData(23, 59, "night")]
    [InlineData(0, 0, "night")]
    public void ThreeTime_PeriodLimitsAt06_17_22(int hour, int minute, string expected)
    {
        Assert.Equal(expected, ThreeTime().PeriodAt(new DateTime(2026, 6, 1, hour, minute, 0)));
    }

    [Fact]
    public void ThreeTime_HourPrice_UsesThePeriodOfThatHour()
    {
        PriceSchedule p = ThreeTime();
        double day = 4.9204, peak = 6.9958, night = 3.2607;

        Assert.Equal(night, p.HourPrice(new DateTime(2026, 6, 1, 5, 0, 0)), 6);
        Assert.Equal(day, p.HourPrice(new DateTime(2026, 6, 1, 6, 0, 0)), 6);
        Assert.Equal(day, p.HourPrice(new DateTime(2026, 6, 1, 16, 0, 0)), 6);
        Assert.Equal(peak, p.HourPrice(new DateTime(2026, 6, 1, 17, 0, 0)), 6);
        Assert.Equal(peak, p.HourPrice(new DateTime(2026, 6, 1, 21, 0, 0)), 6);
        Assert.Equal(night, p.HourPrice(new DateTime(2026, 6, 1, 22, 0, 0)), 6);
        Assert.Equal(night, p.HourPrice(new DateTime(2026, 6, 1, 0, 0, 0)), 6);
    }

    [Fact]
    public void ThreeTime_HourPrice_WeighsAPeriodChangeInsideTheHour()
    {
        // A made-up tariff with a limit at 06:30: half the hour at each price.
        PriceSchedule p = PriceSchedule.TimeOfUse("EUR", new[] { (390, 1320, 0.30, "day"), (1320, 390, 0.10, "night") });

        Assert.Equal(0.20, p.HourPrice(new DateTime(2026, 6, 1, 6, 0, 0)), 9);
    }

    [Fact]
    public void IntervalAcrossMidnight_IsPricedAsNightOnBothDays()
    {
        var history = new UsageHistory(Path.Combine(_dir, UsageHistory.FileName));
        // 2 minutes at 3600 W wall, from 23:59 to 00:01: 0.06 kWh before and 0.06 kWh after midnight.
        history.Add(new DateTime(2026, 6, 2, 0, 1, 0), new IntervalEnergy(120, 120, 108, 0, 0, 0, false));
        PriceSchedule p = ThreeTime();

        List<HourlyUsage> hours = history.Snapshot();
        UsageTotals total = UsageReport.Total(hours, p);

        Assert.Equal(new[] { new DateTime(2026, 6, 1, 23, 0, 0), new DateTime(2026, 6, 2, 0, 0, 0) }, hours.Select(h => h.Hour));
        Assert.Equal(0.12 * 3.2607, total.Cost!.Value, 9);
    }

    [Fact]
    public void ThreeTime_DayCost_SumsEachHourAtItsOwnPrice()
    {
        // 1 kWh in every hour of one day: 8 night hours, 11 day hours, 5 peak hours.
        var hours = Enumerable.Range(0, 24)
            .Select(h => new HourlyUsage(new DateTime(2026, 6, 1, h, 0, 0), 1000, 900, 0, 0, 0, 3600, 0))
            .ToList();

        UsageTotals t = UsageReport.Total(hours, ThreeTime());

        Assert.Equal(8 * 3.2607 + 11 * 4.9204 + 5 * 6.9958, t.Cost!.Value, 9);
    }

    // ------------------------------------------------------------------ tier selection

    [Theory]
    [InlineData(null, 3.238)]
    [InlineData("low", 3.238)]
    [InlineData("high", 4.857)]
    [InlineData("does-not-exist", 3.238)]
    public void TierSelection_UsesTheChosenTier_DefaultLow(string? tier, double expected)
    {
        var settings = new AppSettings { TariffId = "tr-residential-single", TariffTier = tier };

        TariffChoice choice = TariffResolver.Resolve(Table, settings, "TR");

        Assert.Equal(expected, choice.Schedule!.PriceAt(new DateTime(2026, 6, 1, 18, 0, 0)), 6);
        Assert.Equal(tier == "high" ? "high" : "low", choice.Tier!.Id);
    }

    [Fact]
    public void Automatic_PicksTheRegionsFirstTariff_OrNone()
    {
        var auto = new AppSettings();

        Assert.Equal("tr-residential-single", TariffResolver.Resolve(Table, auto, "TR").Tariff!.Id);
        Assert.True(TariffResolver.Resolve(Table, auto, "DE").Automatic);
        Assert.Equal("EUR", TariffResolver.Resolve(Table, auto, "DE").Schedule!.Currency);
        Assert.Null(TariffResolver.Resolve(Table, auto, "JP").Schedule);
        Assert.Null(TariffResolver.Resolve(Table, new AppSettings { TariffId = AppSettings.TariffOff }, "TR").Schedule);
    }

    [Fact]
    public void Manual_FlatAndThreeTime()
    {
        var flat = new AppSettings { TariffId = AppSettings.TariffManual, ManualCurrency = "USD", ManualPrice = 0.17 };
        var unset = new AppSettings { TariffId = AppSettings.TariffManual };
        var tou = new AppSettings
        {
            TariffId = AppSettings.TariffManual, ManualCurrency = "TRY", ManualTimeOfUse = true,
            ManualDayPrice = 5, ManualPeakPrice = 7, ManualNightPrice = 3,
        };

        Assert.Equal(0.17, TariffResolver.Resolve(Table, flat, "TR").Schedule!.PriceAt(DateTime.Now));
        Assert.Equal("USD", TariffResolver.Resolve(Table, flat, "TR").Schedule!.Currency);
        Assert.Null(TariffResolver.Resolve(Table, unset, "TR").Schedule); // nothing entered yet
        PriceSchedule p = TariffResolver.Resolve(Table, tou, "TR").Schedule!;
        Assert.Equal(3, p.PriceAt(new DateTime(2026, 1, 1, 2, 0, 0)));
        Assert.Equal(5, p.PriceAt(new DateTime(2026, 1, 1, 6, 0, 0)));
        Assert.Equal(7, p.PriceAt(new DateTime(2026, 1, 1, 17, 0, 0)));
        Assert.Equal(3, p.PriceAt(new DateTime(2026, 1, 1, 22, 0, 0)));
    }

    // ------------------------------------------------------------------ schema validation

    private static string Mutate(Action<JsonNode> change)
    {
        JsonNode root = JsonNode.Parse(EmbeddedJson())!;
        change(root);
        return root.ToJsonString();
    }

    private static JsonNode FirstTariff(JsonNode root, string id) =>
        root["countries"]!.AsArray().SelectMany(c => c!["tariffs"]!.AsArray()).First(t => (string?)t!["id"] == id)!;

    public static IEnumerable<object[]> BrokenTables()
    {
        yield return new object[] { "", "empty" };
        yield return new object[] { "{ \"schemaVersion\": 1, ", "truncated" };
        yield return new object[] { "[]", "array" };
        yield return new object[] { "null", "null" };
        yield return new object[] { Mutate(r => r["schemaVersion"] = 99), "future schema" };
        yield return new object[] { Mutate(r => r["countries"] = new JsonArray()), "no countries" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["price"] = -0.3), "negative price" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["price"] = 0), "zero price" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["price"] = 36.18), "price in cents" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-single")["tiers"]![1]!["price"] = 486), "price in kurus" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["type"] = "spot"), "unknown type" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["currency"] = "euro"), "bad currency" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["source"] = "http://example.com"), "http source" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["id"] = "tr-residential-single"), "duplicate id" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-three-time")["periods"]![0]!["end"] = "16:00"), "gap in periods" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-three-time")["periods"]![0]!["end"] = "18:00"), "overlapping periods" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-three-time")["periods"]![0]!["start"] = "6am"), "bad time" };
        yield return new object[] { Mutate(r => FirstTariff(r, "de-average")["price"] = "0.36"), "price as string" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-three-time")["periods"]![0]!["id"] = "Day <b>"), "bad period id" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-three-time")["periods"]![1]!["id"] = "day"), "duplicate period id" };
        yield return new object[] { Mutate(r => FirstTariff(r, "tr-residential-three-time")["periods"]![2]!["name"] = null), "period without name" };
    }

    [Theory]
    [MemberData(nameof(BrokenTables))]
    public void BrokenTable_IsRejected_WithoutThrowing(string json, string what)
    {
        bool ok = TariffTable.TryParse(json, out TariffTable? table, out string error);

        Assert.False(ok, what);
        Assert.Null(table);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void CorruptCache_FallsBackToTheEmbeddedTable()
    {
        File.WriteAllText(Path.Combine(_dir, "tariffs-cache.json"), "{ corrupt");

        var repo = new TariffRepository(_dir);

        Assert.False(repo.TableFromCache);
        Assert.Equal(repo.Embedded.Version, repo.Table.Version);
    }

    [Fact]
    public void NewerValidCache_IsUsed_OlderIsNot()
    {
        string cache = Path.Combine(_dir, "tariffs-cache.json");
        File.WriteAllText(cache, Mutate(r => r["version"] = 100));
        Assert.True(new TariffRepository(_dir).TableFromCache);

        File.WriteAllText(cache, Mutate(r => r["version"] = 0));
        Assert.False(new TariffRepository(_dir).TableFromCache);
    }

    // ------------------------------------------------------------------ online update

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Online404_KeepsTheTable_AndRecordsTheCheck()
    {
        var repo = new TariffRepository(_dir);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        TariffCheckResult result = await repo.CheckOnlineAsync("0.2.0", Now, handler);

        Assert.Equal(TariffCheckResult.Failed, result);
        Assert.False(repo.TableFromCache);
        Assert.Equal("HTTP 404", repo.ReadState().LastResult);
        Assert.Equal(Now, repo.ReadState().LastCheckUtc);
        Assert.False(File.Exists(Path.Combine(_dir, "tariffs-cache.json")));
    }

    [Fact]
    public async Task OnlineRequest_IsAPlainGetOfTheFixedUrl()
    {
        var repo = new TariffRepository(_dir);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await repo.CheckOnlineAsync("0.2.0", Now, handler);

        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(TariffRepository.UpdateUrl, request.RequestUri!.ToString());
        Assert.Equal("", request.RequestUri.Query);
        Assert.Equal("PcPowerMeter/0.2.0", request.Headers.UserAgent.ToString());
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task Online_AtMostOncePerDay()
    {
        var repo = new TariffRepository(_dir);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await repo.CheckOnlineAsync("0.2.0", Now, handler);
        TariffCheckResult again = await repo.CheckOnlineAsync("0.2.0", Now.AddHours(23), handler);
        TariffCheckResult nextDay = await repo.CheckOnlineAsync("0.2.0", Now.AddHours(24), handler);

        Assert.Equal(TariffCheckResult.Skipped, again);
        Assert.Equal(TariffCheckResult.Failed, nextDay);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OnlineInvalidTable_IsRejected()
    {
        var repo = new TariffRepository(_dir);
        string bad = Mutate(r => { r["version"] = 100; FirstTariff(r, "de-average")["price"] = -1; });
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(bad) });

        TariffCheckResult result = await repo.CheckOnlineAsync("0.2.0", Now, handler);

        Assert.Equal(TariffCheckResult.Failed, result);
        Assert.StartsWith("invalid table", repo.ReadState().LastResult);
        Assert.False(repo.TableFromCache);
    }

    [Fact]
    public async Task OnlineNetworkError_IsSwallowed()
    {
        var repo = new TariffRepository(_dir);
        var handler = new FakeHandler(_ => throw new HttpRequestException("no route"));

        TariffCheckResult result = await repo.CheckOnlineAsync("0.2.0", Now, handler);

        Assert.Equal(TariffCheckResult.Failed, result);
        Assert.Equal("network error", repo.ReadState().LastResult);
    }

    [Fact]
    public async Task OnlineNewerTable_IsCachedAndUsed()
    {
        var repo = new TariffRepository(_dir);
        string newer = Mutate(r => { r["version"] = 7; FirstTariff(r, "de-average")["price"] = 0.40; });
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(newer) });

        TariffCheckResult result = await repo.CheckOnlineAsync("0.2.0", Now, handler);

        Assert.Equal(TariffCheckResult.Updated, result);
        Assert.Equal(7, repo.Table.Version);
        Assert.Equal(0.40, repo.Table.Find("de-average")!.Price);
        Assert.True(new TariffRepository(_dir).TableFromCache); // survives a restart
    }
}
