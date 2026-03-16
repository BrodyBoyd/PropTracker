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
        private const string BaseUrl = "https://api.balldontlie.io/v1";

        public NbaStatsService(HttpClient http, IConfiguration config)
        {
            _http = http;
            var apiKey = config["BallDontLie:ApiKey"] ?? string.Empty;
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
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<BdlPlayer>();

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

        // ── Last 5 Games ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the last 5 completed game stat lines for a player this season.
        /// Displayed in the PropDetails view.
        /// </summary>
        public async Task<List<BdlGameLog>> GetLastFiveGamesAsync(int bdlPlayerId)
        {
            var season = GetCurrentSeason();
            var url = $"{BaseUrl}/stats?player_ids[]={bdlPlayerId}&seasons[]={season}&per_page=10";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new List<BdlGameLog>();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            return doc.RootElement
                .GetProperty("data")
                .EnumerateArray()
                .Select(s => new BdlGameLog
                {
                    Date = s.GetProperty("game").GetProperty("date").GetString() ?? "",
                    PTS = s.GetProperty("pts").GetInt32(),
                    AST = s.GetProperty("ast").GetInt32(),
                    REB = s.GetProperty("reb").GetInt32(),
                    THREEPM = s.GetProperty("fg3m").GetInt32(),
                    Min = s.GetProperty("min").GetString() ?? "0"
                })
                .OrderByDescending(g => g.Date)
                .Take(5)
                .ToList();
        }

        // ── Auto-Check Pending Props ──────────────────────────────────────

        /// <summary>
        /// Checks a pending prop against the BallDontLie API.
        ///
        /// If the prop has a GameDate set, it fetches stats for that specific date
        /// so the right game is always used regardless of when CheckPending is run.
        ///
        /// If no GameDate is set it falls back to the player's most recent game —
        /// which may not match the intended game, so setting GameDate is strongly recommended.
        ///
        /// Returns:
        ///   Hit     — game found, stat clears the line in the correct direction
        ///   Miss    — game found, stat does not clear the line
        ///   Pending — no completed game found for the given date yet (game hasn't happened or stats not posted)
        /// </summary>
        public async Task<Prop.PropResult> CheckPropResultAsync(Prop prop)
        {
            if (prop.BdlPlayerId <= 0)
                return Prop.PropResult.Pending;

            JsonElement statElement;

            if (prop.GameDate.HasValue)
            {
                // Fetch stats for the exact game date — this is the precise, correct approach
                statElement = await GetStatForDateAsync(prop.BdlPlayerId, prop.GameDate.Value);
            }
            else
            {
                // No date set — fall back to most recent game
                statElement = await GetMostRecentStatAsync(prop.BdlPlayerId);
            }

            if (statElement.ValueKind == JsonValueKind.Undefined)
                return Prop.PropResult.Pending; // game not played yet or stats not posted

            double actual = ComputeStatValue(prop.PropType, statElement);
            return EvaluateResult(actual, prop.PropValue, prop.OverUnder);
        }

        // ── Private API helpers ───────────────────────────────────────────

        /// <summary>
        /// Fetches the stat line for a player on a specific calendar date.
        /// BallDontLie stores game dates as YYYY-MM-DD UTC, so we query that exact date.
        /// Returns Undefined if no game was played or stats haven't posted yet.
        /// </summary>
        private async Task<JsonElement> GetStatForDateAsync(int bdlPlayerId, DateTime gameDate)
        {
            // BDL dates are stored as the UTC start date of the game (e.g. "2025-03-14T00:00:00.000Z")
            // We pass the date in YYYY-MM-DD format via the dates[] filter
            var dateStr = gameDate.ToString("yyyy-MM-dd");
            var url = $"{BaseUrl}/stats?player_ids[]={bdlPlayerId}&dates[]={dateStr}";
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode) return default;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            // Should be exactly one result if the player played that day
            return data.GetArrayLength() > 0 ? data[0] : default;
        }

        /// <summary>
        /// Falls back to the most recent game log when no GameDate is set.
        /// Less reliable — use GameDate whenever possible.
        /// </summary>
        private async Task<JsonElement> GetMostRecentStatAsync(int bdlPlayerId)
        {
            var season = GetCurrentSeason();
            var url = $"{BaseUrl}/stats?player_ids[]={bdlPlayerId}&seasons[]={season}&per_page=5";
            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode) return default;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");

            if (data.GetArrayLength() == 0) return default;

            // Sort by date descending, take most recent
            return data.EnumerateArray()
                .OrderByDescending(s => s.GetProperty("game").GetProperty("date").GetString())
                .FirstOrDefault();
        }

        // ── Stat computation ──────────────────────────────────────────────

        private static double ComputeStatValue(Prop.BetType type, JsonElement stat)
        {
            int pts = stat.GetProperty("pts").GetInt32();
            int ast = stat.GetProperty("ast").GetInt32();
            int reb = stat.GetProperty("reb").GetInt32();
            int fg3m = stat.GetProperty("fg3m").GetInt32();

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

        // NBA season year = the year the season started (Oct).
        // e.g. the 2024-25 season started Oct 2024, so season = 2024.
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