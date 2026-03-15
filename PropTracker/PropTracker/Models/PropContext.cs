using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PropTracker.Models
{
    public class PropContext(DbContextOptions<PropContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Prop> Props { get; set; } = null!;
        public DbSet<Parlay> Parlays { get; set; } = null!;

    }
}
