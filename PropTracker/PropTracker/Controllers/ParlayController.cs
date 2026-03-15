using Microsoft.AspNetCore.Mvc;
using PropTracker.Models;

namespace PropTracker.Controllers
{
    public class ParlayController(PropContext context) : Controller
    {
        private readonly PropContext _context = context;

        // GET: Parlay/Index
        [HttpGet]
        public IActionResult Index()
        {
            var parlays = _context.Parlays.ToList();
            var allProps = _context.Props.ToList();

            ViewBag.AllProps = allProps;

            return View(parlays);
        }

        // GET: Parlay/Create
        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.AvailableProps = _context.Props.ToList();
            return View(new Parlay { PropId = new List<int>() });
        }

        // POST: Parlay/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Parlay parlay, string PropIdsRaw)
        {
            ModelState.Remove("PropId");
            ModelState.Remove("PropIdsRaw");

            var parsedIds = new List<int>();

            if (string.IsNullOrWhiteSpace(PropIdsRaw))
            {
                ModelState.AddModelError("PropIdsRaw", "Please add at least one prop leg.");
            }
            else
            {
                foreach (var part in PropIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out int id) && id > 0)
                    {
                        if (!parsedIds.Contains(id))
                            parsedIds.Add(id);
                    }
                    else
                    {
                        ModelState.AddModelError("PropIdsRaw", $"'{part.Trim()}' is not a valid Prop ID.");
                        break;
                    }
                }

                if (parsedIds.Count == 0)
                    ModelState.AddModelError("PropIdsRaw", "Please add at least one prop leg.");
            }

            if (parlay.Multi <= 0)
                ModelState.AddModelError("Multi", "Multiplier must be greater than 0.");

            if (!ModelState.IsValid)
            {
                ViewBag.AvailableProps = _context.Props.ToList();
                ViewData["PropIdsRaw"] = PropIdsRaw;
                return View(parlay);
            }

            parlay.PropId = parsedIds;
            parlay.Result = Parlay.ParlayResult.Pending;

            _context.Parlays.Add(parlay);
            _context.SaveChanges();

            foreach (var propId in parlay.PropId)
            {
                var prop = _context.Props.FirstOrDefault(p => p.PropId == propId);
                if (prop != null)
                    prop.ParlayId = parlay.ParlayId;
            }
            _context.SaveChanges();

            TempData["SuccessMessage"] = $"Parlay #{parlay.ParlayId} saved with {parlay.PropId.Count} leg{(parlay.PropId.Count != 1 ? "s" : "")}.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Parlay/Details/{id}
        [HttpGet]
        public IActionResult Details(int id)
        {
            var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == id);
            if (parlay == null) return NotFound();

            ViewBag.LegProps = _context.Props
                .Where(p => parlay.PropId.Contains(p.PropId))
                .ToList();

            return View(parlay);
        }

        // GET: Parlay/Edit/{id}
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == id);
            if (parlay == null) return NotFound();

            ViewBag.AvailableProps = _context.Props.ToList();
            return View(parlay);
        }

        // POST: Parlay/Edit/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, Parlay parlay, string PropIdsRaw)
        {
            if (id != parlay.ParlayId) return BadRequest();

            ModelState.Remove("PropId");
            ModelState.Remove("PropIdsRaw");

            var parsedIds = new List<int>();

            if (string.IsNullOrWhiteSpace(PropIdsRaw))
            {
                ModelState.AddModelError("PropIdsRaw", "Please add at least one prop leg.");
            }
            else
            {
                foreach (var part in PropIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out int pid) && pid > 0)
                    {
                        if (!parsedIds.Contains(pid))
                            parsedIds.Add(pid);
                    }
                    else
                    {
                        ModelState.AddModelError("PropIdsRaw", $"'{part.Trim()}' is not a valid Prop ID.");
                        break;
                    }
                }

                if (parsedIds.Count == 0)
                    ModelState.AddModelError("PropIdsRaw", "Please add at least one prop leg.");
            }

            if (parlay.Multi <= 0)
                ModelState.AddModelError("Multi", "Multiplier must be greater than 0.");

            if (!ModelState.IsValid)
            {
                ViewBag.AvailableProps = _context.Props.ToList();
                ViewData["PropIdsRaw"] = PropIdsRaw;
                return View(parlay);
            }

            var existing = _context.Parlays.FirstOrDefault(p => p.ParlayId == id);
            if (existing == null) return NotFound();

            var removedIds = existing.PropId.Except(parsedIds).ToList();
            foreach (var propId in removedIds)
            {
                var prop = _context.Props.FirstOrDefault(p => p.PropId == propId);
                if (prop != null)
                    prop.ParlayId = 0;
            }

            var addedIds = parsedIds.Except(existing.PropId).ToList();
            foreach (var propId in addedIds)
            {
                var prop = _context.Props.FirstOrDefault(p => p.PropId == propId);
                if (prop != null)
                    prop.ParlayId = id;
            }

            existing.Multi = parlay.Multi;
            existing.PropId = parsedIds;

            // Re-evaluate result after legs change
            RecalculateParlayResult(existing);

            TempData["SuccessMessage"] = $"Parlay #{id} updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Parlay/SetResult/{id}?result=Hit
        // Manually override the parlay result (overrides the auto-calculation)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SetResult(int id, string result)
        {
            var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == id);
            if (parlay == null) return NotFound();

            if (!Enum.TryParse<Parlay.ParlayResult>(result, out var parsedResult))
                return BadRequest("Invalid result value.");

            parlay.Result = parsedResult;
            parlay.HitAt = parsedResult == Parlay.ParlayResult.Hit ? (parlay.HitAt ?? DateTime.UtcNow) : null;

            _context.SaveChanges();

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Parlay/Delete/{id}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == id);
            if (parlay == null) return NotFound();

            var linkedProps = _context.Props.Where(p => p.ParlayId == id).ToList();
            linkedProps.ForEach(p => p.ParlayId = 0);

            _context.Parlays.Remove(parlay);
            _context.SaveChanges();

            TempData["SuccessMessage"] = $"Parlay #{id} deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void RecalculateParlayResult(Parlay parlay)
        {
            var legProps = _context.Props
                .Where(p => parlay.PropId.Contains(p.PropId))
                .ToList();

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
                parlay.Result = Parlay.ParlayResult.Pending;
                parlay.HitAt = null;
            }

            _context.SaveChanges();
        }
    }
}