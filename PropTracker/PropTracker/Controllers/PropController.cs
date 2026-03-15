using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropTracker.Models;

namespace PropTracker.Controllers
{
    public class PropController(PropContext context) : Controller
    {
        private readonly PropContext _context = context;

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

            var propTypeRaw = Request.Form["PropType"].ToString();

            if (string.IsNullOrWhiteSpace(propTypeRaw))
                ModelState.AddModelError("PropType", "Please select a prop type.");
            else if (!Enum.TryParse<Prop.BetType>(propTypeRaw, out var parsedType))
                ModelState.AddModelError("PropType", "Invalid prop type selected.");
            else
                prop.PropType = parsedType;

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
        public IActionResult Details(int id)
        {
            var prop = _context.Props.FirstOrDefault(p => p.PropId == id);
            if (prop == null) return NotFound();
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

            var propTypeRaw = Request.Form["PropType"].ToString();

            if (string.IsNullOrWhiteSpace(propTypeRaw))
                ModelState.AddModelError("PropType", "Please select a prop type.");
            else if (!Enum.TryParse<Prop.BetType>(propTypeRaw, out var parsedType))
                ModelState.AddModelError("PropType", "Invalid prop type selected.");
            else
                prop.PropType = parsedType;

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

        // POST: Prop/SetResult/{id}?result=Hit&returnUrl=/Parlay/Details/3
        // Sets the result on a single prop leg and auto-recalculates the parent parlay
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

            // Auto-recalculate the parent parlay result whenever a leg changes
            if (prop.ParlayId > 0)
            {
                var parlay = _context.Parlays.FirstOrDefault(p => p.ParlayId == prop.ParlayId);
                if (parlay != null)
                    RecalculateParlayResult(parlay);
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Details), new { id });
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

        // ── Helpers ──────────────────────────────────────────────────────────

        // Auto-calculates parlay result from leg results:
        //   All Hit  → Hit  (stamps HitAt once)
        //   Any Miss → Miss (clears HitAt)
        //   Otherwise → Pending
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