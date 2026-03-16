using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace PropTracker.Models
{
    public class PropContext(DbContextOptions<PropContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<Prop> Props { get; set; } = null!;
        public DbSet<Parlay> Parlays { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // IdentityDbContext needs this called first
            base.OnModelCreating(modelBuilder);

            // ── Prop ─────────────────────────────────────────────────────

            modelBuilder.Entity<Prop>(entity =>
            {
                // Store enums as their string names so the DB is readable
                entity.Property(p => p.PropType)
                      .HasConversion<string>();

                entity.Property(p => p.OverUnder)
                      .HasConversion<string>();

                entity.Property(p => p.Result)
                      .HasConversion<string>();

                // GameDate: store as UTC ISO string, read back as UTC DateTime
                entity.Property(p => p.GameDate)
                      .HasConversion(
                          v => v.HasValue ? v.Value.ToUniversalTime().ToString("O") : null,
                          v => v != null ? DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind) : (DateTime?)null
                      );
            });

            // ── Parlay ───────────────────────────────────────────────────

            modelBuilder.Entity<Parlay>(entity =>
            {
                entity.Property(p => p.Result)
                      .HasConversion<string>();

                // HitAt: store as UTC ISO string
                entity.Property(p => p.HitAt)
                      .HasConversion(
                          v => v.HasValue ? v.Value.ToUniversalTime().ToString("O") : null,
                          v => v != null ? DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind) : (DateTime?)null
                      );

                // List<int> cannot be stored natively — serialize as a JSON array string
                // e.g. [1, 3, 7] is stored as "[1,3,7]" in the DB column
                entity.Property(p => p.PropId)
                      .HasConversion(
                          v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                          v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
                      );
            });
        }
    }
}