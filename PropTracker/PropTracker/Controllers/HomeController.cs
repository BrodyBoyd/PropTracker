using Microsoft.AspNetCore.Mvc;
using PropTracker.Models;

namespace PropTracker.Controllers
{
    public class HomeController(PropContext context) : Controller
    {
        private readonly PropContext _context = context;

        // GET: /
        // GET: Home/Index
        [HttpGet]
        public IActionResult Index()
        {
            // --- Replace the placeholders below with your data retrieval logic ---
             var allProps   = _context.Props.ToList();
             var allParlays = _context.Parlays.ToList();

            // Total counts for the stats bar
            ViewBag.PropCount = allProps.Count;
            ViewBag.ParlayCount = allParlays.Count;

            // Highest multiplier across all parlays (null-safe — shows "—" if no parlays)
            ViewBag.TopMulti = allParlays.Any()
                ? allParlays.Max(p => p.Multi)
                : (double?)null;

            // Five most recently added props (highest PropId = most recent insert)
            ViewBag.RecentProps = allProps
                .OrderByDescending(p => p.PropId)
                .Take(5)
                .ToList();

            // Top three parlays by multiplier for the sidebar
            ViewBag.TopParlays = allParlays
                .OrderByDescending(p => p.Multi)
                .Take(3)
                .ToList();

            return View();
        }
    }
}
