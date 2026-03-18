using System.Text.Json;
using PropTracker.Models;

namespace PropTracker.Services
{
    /// <summary>
    /// Uses the NBA.com stats API — free, no key required, reliable.
    ///
    /// Endpoints:
    ///   All players:  stats.nba.com/stats/commonallplayers?LeagueID=00&Season=2024-25&IsOnlyCurrentSeason=1
    ///   Player log:   stats.nba.com/stats/playergamelog?PlayerID={id}&Season=2024-25&SeasonType=Regular+Season
    ///
    /// NBA.com requires specific headers or it returns 403.
    /// Register in Program.cs: builder.Services.AddHttpClient&lt;NbaStatsService&gt;();
    /// </summary>
    public class NbaStatsService
    {
        private readonly HttpClient _http;
        private readonly ILogger<NbaStatsService> _logger;

        // In-memory player list cache — loaded once per app lifetime
        private static List<NbaPlayer>? _playerCache;
        private static DateTime _cacheLoaded = DateTime.MinValue;
        private static readonly SemaphoreSlim _cacheLock = new(1, 1);

        private const string StatsBase = "https://stats.nba.com/stats";

        public NbaStatsService(HttpClient http, ILogger<NbaStatsService> logger)
        {
            _http = http;
            _logger = logger;

            // NBA.com requires these headers or returns 403
            if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
                _http.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36");

            if (!_http.DefaultRequestHeaders.Contains("Referer"))
                _http.DefaultRequestHeaders.Add("Referer", "https://www.nba.com/");

            if (!_http.DefaultRequestHeaders.Contains("Origin"))
                _http.DefaultRequestHeaders.Add("Origin", "https://www.nba.com");

            if (!_http.DefaultRequestHeaders.Contains("Accept"))
                _http.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");

            if (!_http.DefaultRequestHeaders.Contains("x-nba-stats-origin"))
                _http.DefaultRequestHeaders.Add("x-nba-stats-origin", "stats");

            if (!_http.DefaultRequestHeaders.Contains("x-nba-stats-token"))
                _http.DefaultRequestHeaders.Add("x-nba-stats-token", "true");

            // Short timeout so blocked requests fail fast instead of hanging
            _http.Timeout = TimeSpan.FromSeconds(8);
        }

        // ── Player Search ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the full active player list for client-side filtering.
        /// Called once by the browser and cached locally.
        /// NBA.com blocks server-side requests so we use a bundled static list.
        /// </summary>
        public Task<List<NbaPlayer>> SearchPlayersAsync(string name)
        {
            var query = name.Trim().ToLower();
            var results = GetStaticPlayerList()
                .Where(p => p.FullName.ToLower().Contains(query))
                .Take(8)
                .ToList();
            return Task.FromResult(results);
        }

        /// <summary>
        /// Returns all players as a JSON-serializable list.
        /// Called by GET /prop/searchplayers with no name param to seed the browser cache.
        /// </summary>
        public Task<List<NbaPlayer>> GetAllPlayersAsync()
        {
            return Task.FromResult(GetStaticPlayerList());
        }

        private static List<NbaPlayer> GetStaticPlayerList() => new()
        {
            new() { Id=2544,    FirstName="LeBron",    LastName="James",               FullName="LeBron James",               Team="LAL" },
            new() { Id=203999,  FirstName="Nikola",    LastName="Jokic",               FullName="Nikola Jokic",               Team="DEN" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=1628369, FirstName="Jayson",    LastName="Tatum",               FullName="Jayson Tatum",               Team="BOS" },
            new() { Id=203507,  FirstName="Giannis",   LastName="Antetokounmpo",       FullName="Giannis Antetokounmpo",      Team="MIL" },
            new() { Id=201939,  FirstName="Stephen",   LastName="Curry",               FullName="Stephen Curry",              Team="GSW" },
            new() { Id=203954,  FirstName="Joel",      LastName="Embiid",              FullName="Joel Embiid",                Team="PHI" },
            new() { Id=1629028, FirstName="Shai",      LastName="Gilgeous-Alexander",  FullName="Shai Gilgeous-Alexander",    Team="OKC" },
            new() { Id=1628389, FirstName="Donovan",   LastName="Mitchell",            FullName="Donovan Mitchell",           Team="CLE" },
            new() { Id=1628384, FirstName="Bam",       LastName="Adebayo",             FullName="Bam Adebayo",                Team="MIA" },
            new() { Id=203081,  FirstName="Damian",    LastName="Lillard",             FullName="Damian Lillard",             Team="MIL" },
            new() { Id=1627742, FirstName="Jaylen",    LastName="Brown",               FullName="Jaylen Brown",               Team="BOS" },
            new() { Id=1628960, FirstName="Trae",      LastName="Young",               FullName="Trae Young",                 Team="ATL" },
            new() { Id=1627783, FirstName="Pascal",    LastName="Siakam",              FullName="Pascal Siakam",              Team="IND" },
            new() { Id=1627875, FirstName="Devin",     LastName="Booker",              FullName="Devin Booker",               Team="PHX" },
            new() { Id=201935,  FirstName="James",     LastName="Harden",              FullName="James Harden",               Team="LAC" },
            new() { Id=203076,  FirstName="Anthony",   LastName="Davis",               FullName="Anthony Davis",              Team="LAL" },
            new() { Id=1628992, FirstName="De'Aaron",  LastName="Fox",                 FullName="De'Aaron Fox",               Team="SAC" },
            new() { Id=1629166, FirstName="Zach",      LastName="LaVine",              FullName="Zach LaVine",                Team="CHI" },
            new() { Id=1628378, FirstName="Lauri",     LastName="Markkanen",           FullName="Lauri Markkanen",            Team="UTA" },
            new() { Id=1629636, FirstName="Darius",    LastName="Garland",             FullName="Darius Garland",             Team="CLE" },
            new() { Id=203500,  FirstName="Rudy",      LastName="Gobert",              FullName="Rudy Gobert",                Team="MIN" },
            new() { Id=1629651, FirstName="RJ",        LastName="Barrett",             FullName="RJ Barrett",                 Team="TOR" },
            new() { Id=1629630, FirstName="Zion",      LastName="Williamson",          FullName="Zion Williamson",            Team="NOP" },
            new() { Id=1629627, FirstName="Ja",        LastName="Morant",              FullName="Ja Morant",                  Team="MEM" },
            new() { Id=203468,  FirstName="Bradley",   LastName="Beal",                FullName="Bradley Beal",               Team="PHX" },
            new() { Id=1628403, FirstName="OG",        LastName="Anunoby",             FullName="OG Anunoby",                 Team="NYK" },
            new() { Id=1629231, FirstName="Tyler",     LastName="Herro",               FullName="Tyler Herro",                Team="MIA" },
            new() { Id=1628981, FirstName="Jaren",     LastName="Jackson Jr.",         FullName="Jaren Jackson Jr.",          Team="MEM" },
            new() { Id=203897,  FirstName="Nikola",    LastName="Vucevic",             FullName="Nikola Vucevic",             Team="CHI" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=1631094, FirstName="Evan",      LastName="Mobley",              FullName="Evan Mobley",                Team="CLE" },
            new() { Id=1629632, FirstName="Scottie",   LastName="Barnes",              FullName="Scottie Barnes",             Team="TOR" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=203110,  FirstName="Draymond",  LastName="Green",               FullName="Draymond Green",             Team="GSW" },
            new() { Id=1628464, FirstName="Bogdan",    LastName="Bogdanovic",          FullName="Bogan Bogdanovic",           Team="ATL" },
            new() { Id=1629029, FirstName="Kyrie",     LastName="Irving",              FullName="Kyrie Irving",               Team="DAL" },
            new() { Id=202695,  FirstName="Kawhi",     LastName="Leonard",             FullName="Kawhi Leonard",              Team="LAC" },
            new() { Id=203944,  FirstName="Julius",    LastName="Randle",              FullName="Julius Randle",              Team="MIN" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=203932,  FirstName="Andre",     LastName="Drummond",            FullName="Andre Drummond",             Team="CHI" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=201566,  FirstName="Paul",      LastName="George",              FullName="Paul George",                Team="PHI" },
            new() { Id=203476,  FirstName="Michael",   LastName="Porter Jr.",          FullName="Michael Porter Jr.",         Team="DEN" },
            new() { Id=1629029, FirstName="Luka",      LastName="Doncic",              FullName="Luka Doncic",                Team="DAL" },
            new() { Id=1630162, FirstName="Anthony",   LastName="Edwards",             FullName="Anthony Edwards",            Team="MIN" },
            new() { Id=1629029, FirstName="Karl-Anthony",LastName="Towns",             FullName="Karl-Anthony Towns",         Team="NYK" },
            new() { Id=1629029, FirstName="Jalen",     LastName="Brunson",             FullName="Jalen Brunson",              Team="NYK" },
            new() { Id=1628384, FirstName="Mikal",     LastName="Bridges",             FullName="Mikal Bridges",              Team="NYK" },
            new() { Id=1629029, FirstName="Josh",      LastName="Hart",                FullName="Josh Hart",                  Team="NYK" },
            new() { Id=1630573, FirstName="Cade",      LastName="Cunningham",          FullName="Cade Cunningham",            Team="DET" },
            new() { Id=1631096, FirstName="Franz",     LastName="Wagner",              FullName="Franz Wagner",               Team="ORL" },
            new() { Id=1630228, FirstName="LaMelo",    LastName="Ball",                FullName="LaMelo Ball",                Team="CHA" },
        };

        /// <summary>
        /// Loads the full list of active NBA players from NBA.com and caches it.
        /// The cache refreshes every 24 hours.
        /// </summary>
        private async Task<List<NbaPlayer>> GetOrLoadPlayerCacheAsync()
        {
            // Return cache if fresh (less than 24 hours old)
            if (_playerCache != null && (DateTime.UtcNow - _cacheLoaded).TotalHours < 24)
                return _playerCache;

            await _cacheLock.WaitAsync();
            try
            {
                // Double-check after acquiring lock
                if (_playerCache != null && (DateTime.UtcNow - _cacheLoaded).TotalHours < 24)
                    return _playerCache;

                var season = GetCurrentSeasonString();
                var url = $"{StatsBase}/commonallplayers?LeagueID=00&Season={season}&IsOnlyCurrentSeason=1";

                _logger.LogInformation("Loading NBA player list from: {Url}", url);

                using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var response = await _http.GetAsync(url, cts1.Token);

                _logger.LogInformation("NBA.com commonallplayers status: {Status}", (int)response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("NBA.com commonallplayers {Status}: {Body}", (int)response.StatusCode, errBody[..Math.Min(300, errBody.Length)]);
                    return _playerCache ?? new List<NbaPlayer>();
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("NBA.com commonallplayers response ({Chars} chars): {Preview}",
                    json.Length, json[..Math.Min(300, json.Length)]);
                var doc = JsonDocument.Parse(json);

                // NBA.com stats API returns:
                // { "resultSets": [{ "headers": ["PERSON_ID","DISPLAY_LAST_COMMA_FIRST",...], "rowSet": [[id, name, ...], ...] }] }
                var resultSet = doc.RootElement
                    .GetProperty("resultSets")[0];

                var headers = resultSet.GetProperty("headers")
                    .EnumerateArray()
                    .Select(h => h.GetString() ?? "")
                    .ToList();

                var idIdx = headers.IndexOf("PERSON_ID");
                var firstIdx = headers.IndexOf("DISPLAY_FIRST_LAST");    // "LeBron James"
                var lastCommaIdx = headers.IndexOf("DISPLAY_LAST_COMMA_FIRST"); // "James, LeBron"
                var teamIdx = headers.IndexOf("TEAM_ABBREVIATION");

                // Fallback column names
                if (firstIdx < 0) firstIdx = headers.IndexOf("PLAYER_LAST_NAME");

                var players = new List<NbaPlayer>();

                foreach (var row in resultSet.GetProperty("rowSet").EnumerateArray())
                {
                    var cols = row.EnumerateArray().ToList();
                    if (cols.Count <= idIdx) continue;

                    var id = cols[idIdx].GetInt32();
                    var fullName = firstIdx >= 0 && firstIdx < cols.Count
                        ? cols[firstIdx].GetString() ?? ""
                        : "";

                    // If DISPLAY_FIRST_LAST not available, parse from DISPLAY_LAST_COMMA_FIRST
                    if (string.IsNullOrWhiteSpace(fullName) && lastCommaIdx >= 0 && lastCommaIdx < cols.Count)
                    {
                        var lastComma = cols[lastCommaIdx].GetString() ?? "";
                        var parts = lastComma.Split(',', 2);
                        fullName = parts.Length == 2
                            ? $"{parts[1].Trim()} {parts[0].Trim()}"
                            : lastComma;
                    }

                    if (id <= 0 || string.IsNullOrWhiteSpace(fullName)) continue;

                    var team = teamIdx >= 0 && teamIdx < cols.Count
                        ? cols[teamIdx].GetString() ?? ""
                        : "";

                    var nameParts = fullName.Split(' ', 2);

                    players.Add(new NbaPlayer
                    {
                        Id = id,
                        FullName = fullName,
                        FirstName = nameParts[0],
                        LastName = nameParts.Length > 1 ? nameParts[1] : "",
                        Team = team
                    });
                }

                _logger.LogInformation("Loaded {Count} active NBA players into cache", players.Count);
                _playerCache = players;
                _cacheLoaded = DateTime.UtcNow;

                return players;
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        // ── Last 5 Games ──────────────────────────────────────────────────

        /// <summary>
        /// Returns the last 5 game stat lines for a player using the NBA.com game log endpoint.
        /// </summary>
        public async Task<List<NbaGameLog>> GetLastFiveGamesAsync(int nbaPlayerId)
        {
            try
            {
                var season = GetCurrentSeasonString();
                var url = $"{StatsBase}/playergamelog?PlayerID={nbaPlayerId}&Season={season}&SeasonType=Regular+Season";

                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var response = await _http.GetAsync(url, cts2.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("NBA.com playergamelog returned {Status} for player {Id}",
                        (int)response.StatusCode, nbaPlayerId);
                    return new List<NbaGameLog>();
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                var resultSet = doc.RootElement.GetProperty("resultSets")[0];
                var headers = resultSet.GetProperty("headers")
                    .EnumerateArray()
                    .Select(h => h.GetString() ?? "")
                    .ToList();

                int Idx(string col) => headers.IndexOf(col);

                var gameDateIdx = Idx("GAME_DATE");
                var matchupIdx = Idx("MATCHUP");
                var ptsIdx = Idx("PTS");
                var astIdx = Idx("AST");
                var rebIdx = Idx("REB");
                var fg3mIdx = Idx("FG3M");
                var minIdx = Idx("MIN");

                var logs = new List<NbaGameLog>();

                foreach (var row in resultSet.GetProperty("rowSet").EnumerateArray())
                {
                    var cols = row.EnumerateArray().ToList();

                    var minStr = minIdx >= 0 ? cols[minIdx].GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(minStr) || minStr == "0:00") continue;

                    logs.Add(new NbaGameLog
                    {
                        Date = gameDateIdx >= 0 ? cols[gameDateIdx].GetString() ?? "" : "",
                        Matchup = matchupIdx >= 0 ? cols[matchupIdx].GetString() ?? "" : "",
                        PTS = ptsIdx >= 0 ? SafeGetInt(cols[ptsIdx]) : 0,
                        AST = astIdx >= 0 ? SafeGetInt(cols[astIdx]) : 0,
                        REB = rebIdx >= 0 ? SafeGetInt(cols[rebIdx]) : 0,
                        THREEPM = fg3mIdx >= 0 ? SafeGetInt(cols[fg3mIdx]) : 0,
                        Min = minStr
                    });

                    if (logs.Count >= 5) break; // NBA.com returns newest first
                }

                return logs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLastFiveGamesAsync failed for player {Id}", nbaPlayerId);
                return new List<NbaGameLog>();
            }
        }

        // ── Auto-Check Pending Props ──────────────────────────────────────

        /// <summary>
        /// Checks a pending prop against the NBA.com game log.
        /// If GameDate is set, finds the stat line for that specific date.
        /// Otherwise uses the most recent game.
        /// </summary>
        public async Task<Prop.PropResult> CheckPropResultAsync(Prop prop)
        {
            if (prop.EspnPlayerId <= 0)
            {
                _logger.LogDebug("Prop {Id}: no player ID set, skipping", prop.PropId);
                return Prop.PropResult.Pending;
            }

            try
            {
                List<NbaGameLog> logs;

                try
                {
                    logs = await GetLastFiveGamesAsync(prop.EspnPlayerId);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Prop {Id}: NBA.com timed out — API may be blocking server requests", prop.PropId);
                    return Prop.PropResult.Pending;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("Prop {Id}: NBA.com request failed ({Msg}) — API may be blocking server requests", prop.PropId, ex.Message);
                    return Prop.PropResult.Pending;
                }

                if (!logs.Any())
                {
                    _logger.LogDebug("Prop {Id}: no game logs found", prop.PropId);
                    return Prop.PropResult.Pending;
                }

                NbaGameLog? targetLog;

                if (prop.GameDate.HasValue)
                {
                    // NBA.com date format: "OCT 24, 2024" — match by date components
                    var target = prop.GameDate.Value.ToUniversalTime();
                    targetLog = logs.FirstOrDefault(g =>
                        DateTime.TryParse(g.Date, out var d) &&
                        d.Month == target.Month && d.Day == target.Day && d.Year == target.Year);

                    if (targetLog == null)
                    {
                        _logger.LogDebug("Prop {Id}: no game found on {Date}", prop.PropId, target.ToString("yyyy-MM-dd"));
                        return Prop.PropResult.Pending;
                    }
                }
                else
                {
                    targetLog = logs.First(); // most recent game
                }

                double actual = ComputeStatValue(prop.PropType, targetLog);
                var result = EvaluateResult(actual, prop.PropValue, prop.OverUnder);

                _logger.LogInformation(
                    "Prop {Id} ({Player} {Type} {OU} {Line}): actual={Actual} → {Result}",
                    prop.PropId,
                    $"{prop.PlayerFirstName} {prop.PlayerLastName}",
                    prop.PropType, prop.OverUnder, prop.PropValue,
                    actual, result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckPropResultAsync failed for prop {Id}", prop.PropId);
                return Prop.PropResult.Pending;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static double ComputeStatValue(Prop.BetType type, NbaGameLog log)
        {
            return type switch
            {
                Prop.BetType.PTS => log.PTS,
                Prop.BetType.AST => log.AST,
                Prop.BetType.REB => log.REB,
                Prop.BetType.PA => log.PTS + log.AST,
                Prop.BetType.PR => log.PTS + log.REB,
                Prop.BetType.AR => log.AST + log.REB,
                Prop.BetType.PRA => log.PTS + log.AST + log.REB,
                Prop.BetType.THREEMADE => log.THREEPM,
                Prop.BetType.DD => new[] { log.PTS, log.AST, log.REB }.Count(v => v >= 10) >= 2 ? 1 : 0,
                Prop.BetType.TD => new[] { log.PTS, log.AST, log.REB }.Count(v => v >= 10) >= 3 ? 1 : 0,
                _ => 0
            };
        }

        private static Prop.PropResult EvaluateResult(double actual, double line, Prop.OverUnderType side)
        {
            return side == Prop.OverUnderType.Over
                ? (actual > line ? Prop.PropResult.Hit : Prop.PropResult.Miss)
                : (actual < line ? Prop.PropResult.Hit : Prop.PropResult.Miss);
        }

        private static int SafeGetInt(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.GetInt32(),
                JsonValueKind.String => int.TryParse(el.GetString(), out var v) ? v : 0,
                _ => 0
            };
        }

        /// <summary>Returns "2024-25" style season string for the current NBA season.</summary>
        private static string GetCurrentSeasonString()
        {
            var now = DateTime.UtcNow;
            var startYear = now.Month >= 10 ? now.Year : now.Year - 1;
            var endYear = (startYear + 1) % 100;
            return $"{startYear}-{endYear:D2}";
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    public class NbaPlayer
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Team { get; set; } = "";
    }

    public class NbaGameLog
    {
        public string Date { get; set; } = "";
        public string Matchup { get; set; } = "";
        public int PTS { get; set; }
        public int AST { get; set; }
        public int REB { get; set; }
        public int THREEPM { get; set; }
        public string Min { get; set; } = "";
    }
}