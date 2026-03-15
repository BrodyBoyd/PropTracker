using System.Text.Json;
using PropTracker.Models;

namespace PropTracker.Services
{
    /// <summary>
    /// Wraps the BallDontLie NBA API (balldontlie.io).
    /// Register in Program.cs:
    ///   builder.Services.AddHttpClient<NbaStatsService>();
    ///   builder.Services.Configure<NbaStatsOptions>(builder.Configuration.GetSection("BallDontLie"));
    /// appsettings.json:
    ///   "BallDontLie": { "ApiKey": "YOUR_KEY_HERE" }
    /// </summary>
    public class NbaStatsService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.balldontlie.io/v1";

        public NbaStatsService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["BallDontLie:ApiKey"] ?? string.Empty;
            _http.DefaultRequestHeaders.Add("Authorization", _apiKey);
        }

        // ── Player Search ─────────────────────────────────────────────────

        /// <summary>
        /// Search for players by name. Returns up to 5 matches.
        /// Used by the player search field on Create/Edit forms.
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
        /// Returns the last 5 game stat lines for a player in the current season.
        /// Displayed in the PropDetails sidebar.
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
                    Opponent = s.GetProperty("game").GetProperty("home_team_id").GetInt32() == bdlPlayerId
                                    ? s.GetProperty("game").GetProperty("visitor_team").GetProperty("abbreviation").GetString() ?? ""
                                    : s.GetProperty("game").GetProperty("home_team").GetProperty("abbreviation").GetString() ?? "",
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
        /// Fetches the most recent completed game stat for a player and
        /// compares it against the prop line. Returns Hit, Miss, or Pending
        /// (Pending means no completed game found yet).
        /// </summary>
        public async Task<Prop.PropResult> CheckPropResultAsync(Prop prop)
        {
            if (prop.BdlPlayerId <= 0)
                return Prop.PropResult.Pending;

            var season = GetCurrentSeason();
            var url = $"{BaseUrl}/stats?player_ids[]={prop.BdlPlayerId}&seasons[]={season}&per_page=5";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return Prop.PropResult.Pending;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var latest = doc.RootElement
                .GetProperty("data")
                .EnumerateArray()
                .OrderByDescending(s => s.GetProperty("game").GetProperty("date").GetString())
                .FirstOrDefault();

            if (latest.ValueKind == JsonValueKind.Undefined)
                return Prop.PropResult.Pending;

            double actual = ComputeStatValue(prop.PropType, latest);

            return EvaluateResult(actual, prop.PropValue, prop.OverUnder);
        }

        // ── Helpers ───────────────────────────────────────────────────────

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
                // DD/TD: count categories with >= 10
                Prop.BetType.DD => new[] { pts, ast, reb }.Count(v => v >= 10) >= 2 ? 1 : 0,
                Prop.BetType.TD => new[] { pts, ast, reb }.Count(v => v >= 10) >= 3 ? 1 : 0,
                _ => 0
            };
        }

        private static Prop.PropResult EvaluateResult(double actual, double line, Prop.OverUnderType side)
        {
            // DD/TD use 0/1 — treat line as threshold (e.g. line = 0.5 means "did they get one?")
            return side == Prop.OverUnderType.Over
                ? (actual > line ? Prop.PropResult.Hit : Prop.PropResult.Miss)
                : (actual < line ? Prop.PropResult.Hit : Prop.PropResult.Miss);
        }

        // NBA season runs Oct–Jun. If we're before October, use prior year.
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
        public string Opponent { get; set; } = "";
        public int PTS { get; set; }
        public int AST { get; set; }
        public int REB { get; set; }
        public int THREEPM { get; set; }
        public string Min { get; set; } = "";
    }
}