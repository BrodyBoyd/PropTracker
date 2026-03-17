using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropTracker.Models;
using PropTracker.Services;

namespace PropTracker.Controllers
{
    public class PropController(PropContext context, NbaStatsService nbaStats) : Controller
    {
        private readonly PropContext _context = context;
        private readonly NbaStatsService _nbaStats = nbaStats;

        // GET: Prop/Index
        [HttpGet]
        public IActionResult Index()
        {
            var props = _context.Props.ToList();
            return View(props);
        }

        // GET: Prop/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View(new Prop());
        }

        // POST: Prop/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Prop prop)
        {
            ModelState.Remove("PropType");
            ModelState.Remove("OverUnder");

            var propTypeRaw = Request.Form["PropType"].ToString();
            var overUnderRaw = Request.Form["OverUnder"].ToString();

            if (string.IsNullOrWhiteSpace(propTypeRaw))
                ModelState.AddModelError("PropType", "Please select a prop type.");
            else if (!Enum.TryParse<Prop.BetType>(propTypeRaw, out var parsedType))
                ModelState.AddModelError("PropType", "Invalid prop type selected.");
            else
                prop.PropType = parsedType;

            if (string.IsNullOrWhiteSpace(overUnderRaw))
                ModelState.AddModelError("OverUnder", "Please select Over or Under.");
            else if (!Enum.TryParse<Prop.OverUnderType>(overUnderRaw, out var parsedOu))
                ModelState.AddModelError("OverUnder", "Invalid Over/Under value.");
            else
                prop.OverUnder = parsedOu;

            if (prop.ParlayId < 0)
                ModelState.AddModelError("ParlayId", "Parlay ID must be a positive number.");

            if (!ModelState.IsValid)
                return View(prop);

            if (prop.ParlayId <= 0)
                prop.ParlayId = 0;

            prop.Result = Prop.PropResult.Pending;

            _context.Props.Add(prop);
            _context.SaveChanges();

            TempData["SuccessMessage"] = $"Prop for {prop.PlayerFirstName} {prop.PlayerLastName} saved successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Prop/Details/{id}
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var prop = _context.Props.FirstOrDefault(p => p.PropId == id);
            if (prop == null) return NotFound();

            if (prop.BdlPlayerId > 0)
            {
                try { ViewBag.LastFiveGames = await _nbaStats.GetLastFiveGamesAsync(prop.BdlPlayerId); }
                catch { ViewBag.LastFiveGames = null; }
            }

            return View(prop);
        }

        // GET: Prop/Edit/{id}
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var prop = _context.Props.FirstOrDefault(p => p.PropId == id);
            if (prop == null) return NotFound();
            return View(prop);
        }

        // POST: Prop/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, Prop prop)
        {
            if (id != prop.PropId) return BadRequest();

            ModelState.Remove("PropType");
            ModelState.Remove("OverUnder");

            var propTypeRaw = Request.Form["PropType"].ToString();
            var overUnderRaw = Request.Form["OverUnder"].ToString();

            if (string.IsNullOrWhiteSpace(propTypeRaw))
                ModelState.AddModelError("PropType", "Please select a prop type.");
            else if (!Enum.TryParse<Prop.BetType>(propTypeRaw, out var parsedType))
                ModelState.AddModelError("PropType", "Invalid prop type selected.");
            else
                prop.PropType = parsedType;

            if (string.IsNullOrWhiteSpace(overUnderRaw))
                ModelState.AddModelError("OverUnder", "Please select Over or Under.");
            else if (!Enum.TryParse<Prop.OverUnderType>(overUnderRaw, out var parsedOu))
                ModelState.AddModelError("OverUnder", "Invalid Over/Under value.");
            else
                prop.OverUnder = parsedOu;

            if (prop.ParlayId < 0)
                ModelState.AddModelError("ParlayId", "Parlay ID must be a positive number.");

            if (!ModelState.IsValid)
                return View(prop);

            if (prop.ParlayId <= 0)
                prop.ParlayId = 0;

            _context.Entry(prop).State = EntityState.Modified;
            _context.SaveChanges();

            TempData["SuccessMessage"] = $"Prop for {prop.PlayerFirstName} {prop.PlayerLastName} updated successfully.";
            return RedirectToAction(nameof(Details), new { id = prop.PropId });
        }

        // POST: Prop/SetResult/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetResult(int id, string result, string returnUrl)
        {
            var prop = _context.Props.FirstOrDefault(p => p.PropId == id);
            if (prop == null) return NotFound();

            if (!Enum.TryParse<Prop.PropResult>(result, out var parsedResult))
                return BadRequest("Invalid result value.");

            prop.Result = parsedResult;
            _context.SaveChanges();

            if (prop.ParlayId > 0)
            {
                var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == prop.ParlayId);
                if (parlay != null) RecalculateParlayResult(parlay);
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Prop/CheckPending
        // Auto-resolves all pending props that have a BdlPlayerId via the BallDontLie API
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckPending()
        {
            // Pull pending props into memory — EF tracks these objects
            var pendingProps = _context.Props
                .Where(p => p.Result == Prop.PropResult.Pending && p.BdlPlayerId > 0)
                .ToList();

            int updated = 0;
            var errors = new List<string>();

            foreach (var prop in pendingProps)
            {
                try
                {
                    var result = await _nbaStats.CheckPropResultAsync(prop);

                    if (result == Prop.PropResult.Pending)
                        continue; // game not played yet — leave as Pending

                    // Mutate the tracked entity directly — EF detects the change automatically
                    prop.Result = result;
                    updated++;
                }
                catch (Exception ex)
                {
                    // Log and continue so one bad prop does not block the rest
                    errors.Add($"Prop #{prop.PropId} ({prop.PlayerFirstName} {prop.PlayerLastName}): {ex.Message}");
                }
            }

            // Save all prop result changes in one transaction
            _context.SaveChanges();

            // Recalculate affected parlays AFTER SaveChanges so the parlay query
            // sees the freshly committed prop results, not the pre-save values
            var affectedParlayIds = pendingProps
                .Where(p => p.ParlayId > 0 && p.Result != Prop.PropResult.Pending)
                .Select(p => p.ParlayId)
                .Distinct()
                .ToList();

            foreach (var parlayId in affectedParlayIds)
            {
                var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == parlayId);
                if (parlay != null)
                    RecalculateParlayResult(parlay);
            }

            // Surface any per-prop errors to the UI
            if (errors.Any())
                TempData["ErrorMessage"] = $"Errors on {errors.Count} prop(s): {string.Join("; ", errors)}";

            TempData["SuccessMessage"] = updated > 0
                ? $"Checked {pendingProps.Count} pending prop(s) — {updated} resolved."
                : pendingProps.Count == 0
                    ? "No pending props with a linked player found."
                    : "All props checked — none resolved yet (games may not have been played).";

            return RedirectToAction(nameof(Index));
        }

        // GET: Prop/SearchPlayers?name=lebron
        // JSON endpoint for the live player search field on Create/Edit
        [HttpGet]
        public async Task<IActionResult> SearchPlayers(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
                return Json(new List<object>());

            try
            {
                var players = await _nbaStats.SearchPlayersAsync(name);
                return Json(players.Select(p => new
                {
                    id = p.Id,
                    firstName = p.FirstName,
                    lastName = p.LastName,
                    team = p.Team,
                    display = $"{p.FirstName} {p.LastName} ({p.Team})"
                }));
            }
            catch { return Json(new List<object>()); }
        }

        // POST: Prop/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var prop = _context.Props.FirstOrDefault(p => p.PropId == id);
            if (prop == null) return NotFound();

            _context.Props.Remove(prop);
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Prop deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private void RecalculateParlayResult(Parlay parlay)
        {
            // Re-query with AsNoTracking so we always get fresh DB values,
            // not potentially stale cached entities from earlier in the request
            var legProps = _context.Props
                .AsNoTracking()
                .Where(p => parlay.PropId.Contains(p.PropId))
                .ToList();

            if (!legProps.Any())
            {
                // No legs found — leave result as-is
                return;
            }

            if (legProps.Any(p => p.Result == Prop.PropResult.Miss))
            {
                parlay.Result = Parlay.ParlayResult.Miss;
                parlay.HitAt = null;
            }
            else if (legProps.All(p => p.Result == Prop.PropResult.Hit))
            {
                parlay.Result = Parlay.ParlayResult.Hit;
                parlay.HitAt ??= DateTime.UtcNow;
            }
            else
            {
                // Some legs still Pending
                parlay.Result = Parlay.ParlayResult.Pending;
                parlay.HitAt = null;
            }

            _context.SaveChanges();
        }
    }
}