using System.Text.Json;
using PropTracker.Models;

namespace PropTracker.Services
{
    /// <summary>
    /// Wraps the BallDontLie NBA API (balldontlie.io).
    /// Register in Program.cs:
    ///   builder.Services.AddHttpClient&lt;NbaStatsService&gt;();
    /// appsettings.json:
    ///   "BallDontLie": { "ApiKey": "YOUR_KEY_HERE" }
    /// </summary>
    public class NbaStatsService
    {
        private readonly HttpClient _http;
        private readonly ILogger<NbaStatsService> _logger;
        private const string BaseUrl = "https://api.balldontlie.io/v1";

        public NbaStatsService(HttpClient http, IConfiguration config, ILogger<NbaStatsService> logger)
        {
            _http = http;
            _logger = logger;

            var apiKey = config["BallDontLie:ApiKey"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(apiKey))
                _logger.LogWarning("BallDontLie:ApiKey is not set in appsettings.json");

            // Only add the header once — HttpClient is reused across requests
            if (!_http.DefaultRequestHeaders.Contains("Authorization"))
                _http.DefaultRequestHeaders.Add("Authorization", apiKey);
        }

        // ── Player Search ─────────────────────────────────────────────────

        /// <summary>
        /// Search for players by name. Returns up to 5 matches.
        /// Called by GET /Prop/SearchPlayers?name=... from the Create/Edit form.
        /// </summary>
        public async Task<List<BdlPlayer>> SearchPlayersAsync(string name)
        {
            var url = $"{BaseUrl}/players?search={Uri.EscapeDataString(name)}&per_page=5";

            try
            {
                var response = await _http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("BDL SearchPlayers returned {Status} for query '{Name}'",
                        (int)response.StatusCode, name);
                    return new List<BdlPlayer>();
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                return doc.RootElement
                    .GetProperty("data")
                    .EnumerateArray()
                    .Select(p => new BdlPlayer
                    {
                        Id = p.GetProperty("id").GetInt32(),
                        FirstName = p.GetProperty("first_name").GetString() ?? "",
                        LastName = p.GetProperty("last_name").GetString() ?? "",
                        Team = p.TryGetProperty("team", out var t) && t.ValueKind != JsonValueKind.Null
                                        ? t.GetProperty("abbreviation").GetString() ?? ""
                                        : ""
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SearchPlayersAsync for '{Name}'", name);
                return new List<BdlPlayer>();
            }
        }

        // ── Last 5 Games ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the last 5 completed game stat lines for a player this season.
        /// Displayed in the PropDetails view.
        /// </summary>
        public async Task<List<BdlGameLog>> GetLastFiveGamesAsync(int bdlPlayerId)
        {
            var season = GetCurrentSeason();
            var url = $"{BaseUrl}/stats?player_ids[]={bdlPlayerId}&seasons[]={season}&per_page=10";

            try
            {
                var response = await _http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("BDL GetLastFiveGames returned {Status} for player {Id}",
                        (int)response.StatusCode, bdlPlayerId);
                    return new List<BdlGameLog>();
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("BDL GetLastFiveGames response: {Json}", json);

                var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.GetProperty("data");

                var logs = new List<BdlGameLog>();

                foreach (var s in data.EnumerateArray())
                {
                    // BDL returns 0-minute DNP entries — skip them
                    var minStr = s.GetProperty("min").GetString() ?? "0";
                    if (minStr == "0" || minStr == "00" || string.IsNullOrWhiteSpace(minStr))
                        continue;

                    logs.Add(new BdlGameLog
                    {
                        Date = s.GetProperty("game").GetProperty("date").GetString() ?? "",
                        PTS = SafeGetInt(s, "pts"),
                        AST = SafeGetInt(s, "ast"),
                        REB = SafeGetInt(s, "reb"),
                        THREEPM = SafeGetInt(s, "fg3m"),
                        Min = minStr
                    });
                }

                return logs
                    .OrderByDescending(g => g.Date)
                    .Take(5)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetLastFiveGamesAsync for player {Id}", bdlPlayerId);
                return new List<BdlGameLog>();
            }
        }

        // ── Auto-Check Pending Props ──────────────────────────────────────

        /// <summary>
        /// Checks a pending prop against the BallDontLie API.
        ///
        /// If the prop has a GameDate set, it fetches stats for that specific date.
        /// If no GameDate is set it falls back to the player's most recent game.
        ///
        /// Returns:
        ///   Hit     — game found, stat clears the line in the correct direction
        ///   Miss    — game found, stat does not clear the line
        ///   Pending — no completed game found yet (game not played or stats not posted)
        /// </summary>
        public async Task<Prop.PropResult> CheckPropResultAsync(Prop prop)
        {
            if (prop.BdlPlayerId <= 0)
            {
                _logger.LogDebug("Prop {Id}: no BdlPlayerId set, skipping", prop.PropId);
                return Prop.PropResult.Pending;
            }

            try
            {
                JsonElement statElement = prop.GameDate.HasValue
                    ? await GetStatForDateAsync(prop.BdlPlayerId, prop.GameDate.Value)
                    : await GetMostRecentStatAsync(prop.BdlPlayerId);

                if (statElement.ValueKind == JsonValueKind.Undefined)
                {
                    _logger.LogDebug("Prop {Id}: no stat found (game not played yet or stats not posted)", prop.PropId);
                    return Prop.PropResult.Pending;
                }

                // Skip DNP — BDL returns a stat line with min=0 for did-not-plays
                var minStr = statElement.GetProperty("min").GetString() ?? "0";
                if (minStr == "0" || minStr == "00" || string.IsNullOrWhiteSpace(minStr))
                {
                    _logger.LogDebug("Prop {Id}: player DNP on that date", prop.PropId);
                    return Prop.PropResult.Pending;
                }

                double actual = ComputeStatValue(prop.PropType, statElement);
                var result = EvaluateResult(actual, prop.PropValue, prop.OverUnder);

                _logger.LogInformation(
                    "Prop {Id} ({Player} {Type} {OU} {Line}): actual={Actual} → {Result}",
                    prop.PropId,
                    $"{prop.PlayerFirstName} {prop.PlayerLastName}",
                    prop.PropType,
                    prop.OverUnder,
                    prop.PropValue,
                    actual,
                    result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking prop {Id}", prop.PropId);
                return Prop.PropResult.Pending;
            }
        }

        // ── Private API helpers ───────────────────────────────────────────

        /// <summary>
        /// Fetches the stat line for a player on a specific calendar date.
        /// BDL dates are UTC — we pass YYYY-MM-DD via the dates[] filter.
        /// Returns default (Undefined) if the player didn't play or stats aren't posted yet.
        /// </summary>
        private async Task<JsonElement> GetStatForDateAsync(int bdlPlayerId, DateTime gameDate)
        {
            var dateStr = gameDate.ToUniversalTime().ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/stats?player_ids[]={bdlPlayerId}&dates[]={dateStr}";

            _logger.LogDebug("BDL GetStatForDate: {Url}", url);

            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("BDL GetStatForDate returned {Status}", (int)response.StatusCode);
                return default;
            }

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("BDL GetStatForDate response: {Json}", json);

            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            return data.GetArrayLength() > 0 ? data[0].Clone() : default;
        }

        /// <summary>
        /// Falls back to the player's most recent game log when no GameDate is set.
        /// Less reliable — always set GameDate when creating props.
        /// </summary>
        private async Task<JsonElement> GetMostRecentStatAsync(int bdlPlayerId)
        {
            var season = GetCurrentSeason();
            var url = $"{BaseUrl}/stats?player_ids[]={bdlPlayerId}&seasons[]={season}&per_page=10";

            _logger.LogDebug("BDL GetMostRecentStat: {Url}", url);

            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("BDL GetMostRecentStat returned {Status}", (int)response.StatusCode);
                return default;
            }

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("BDL GetMostRecentStat response: {Json}", json);

            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            if (data.GetArrayLength() == 0)
                return default;

            // Sort by date descending, skip DNPs, take most recent actual game
            var latest = data.EnumerateArray()
                .Where(s =>
                {
                    var m = s.GetProperty("min").GetString() ?? "0";
                    return m != "0" && m != "00" && !string.IsNullOrWhiteSpace(m);
                })
                .OrderByDescending(s => s.GetProperty("game").GetProperty("date").GetString())
                .FirstOrDefault();

            return latest.ValueKind != JsonValueKind.Undefined ? latest.Clone() : default;
        }

        // ── Stat computation ──────────────────────────────────────────────

        private static double ComputeStatValue(Prop.BetType type, JsonElement stat)
        {
            int pts = SafeGetInt(stat, "pts");
            int ast = SafeGetInt(stat, "ast");
            int reb = SafeGetInt(stat, "reb");
            int fg3m = SafeGetInt(stat, "fg3m");

            return type switch
            {
                Prop.BetType.PTS => pts,
                Prop.BetType.AST => ast,
                Prop.BetType.REB => reb,
                Prop.BetType.PA => pts + ast,
                Prop.BetType.PR => pts + reb,
                Prop.BetType.AR => ast + reb,
                Prop.BetType.PRA => pts + ast + reb,
                Prop.BetType.THREEMADE => fg3m,
                // DD: at least 2 of pts/ast/reb >= 10. TD: all 3.
                Prop.BetType.DD => new[] { pts, ast, reb }.Count(v => v >= 10) >= 2 ? 1 : 0,
                Prop.BetType.TD => new[] { pts, ast, reb }.Count(v => v >= 10) >= 3 ? 1 : 0,
                _ => 0
            };
        }

        private static Prop.PropResult EvaluateResult(double actual, double line, Prop.OverUnderType side)
        {
            return side == Prop.OverUnderType.Over
                ? (actual > line ? Prop.PropResult.Hit : Prop.PropResult.Miss)
                : (actual < line ? Prop.PropResult.Hit : Prop.PropResult.Miss);
        }

        // BDL stat fields can sometimes come back as null on partial game data —
        // safely default to 0 rather than throwing
        private static int SafeGetInt(JsonElement el, string property)
        {
            if (!el.TryGetProperty(property, out var val)) return 0;
            if (val.ValueKind == JsonValueKind.Null) return 0;
            if (val.ValueKind == JsonValueKind.Number) return val.GetInt32();
            if (val.ValueKind == JsonValueKind.String &&
                int.TryParse(val.GetString(), out var parsed)) return parsed;
            return 0;
        }

        // NBA season year = the year the season started (Oct).
        // e.g. the 2024-25 season started Oct 2024, so GetCurrentSeason() returns 2024.
        private static int GetCurrentSeason()
        {
            var now = DateTime.UtcNow;
            return now.Month >= 10 ? now.Year : now.Year - 1;
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    public class BdlPlayer
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Team { get; set; } = "";
    }

    public class BdlGameLog
    {
        public string Date { get; set; } = "";
        public int PTS { get; set; }
        public int AST { get; set; }
        public int REB { get; set; }
        public int THREEPM { get; set; }
        public string Min { get; set; } = "";
    }
}