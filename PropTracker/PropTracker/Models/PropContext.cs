using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace PropTracker.Models
{
    public class PropContext : IdentityDbContext<ApplicationUser>
    {
        // Parameterless constructor — used only by EF Core design-time tooling
        // (Add-Migration, Update-Database). Not called at runtime.
        public PropContext() { }

        // Runtime constructor — called by ASP.NET dependency injection via Program.cs
        public PropContext(DbContextOptions<PropContext> options) : base(options) { }

        public DbSet<Prop> Props { get; set; } = null!;
        public DbSet<Parlay> Parlays { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Only runs when the parameterless constructor is called by EF tooling.
            // At runtime options are already configured via Program.cs so this is skipped.
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer(
                    "Server=(localdb)\\mssqllocaldb;Database=PropTracker;Trusted_Connection=True;");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Prop ─────────────────────────────────────────────────────

            modelBuilder.Entity<Prop>(entity =>
            {
                entity.Property(p => p.PropType)
                      .HasConversion<string>();

                entity.Property(p => p.OverUnder)
                      .HasConversion<string>();

                entity.Property(p => p.Result)
                      .HasConversion<string>();

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

                entity.Property(p => p.HitAt)
                      .HasConversion(
                          v => v.HasValue ? v.Value.ToUniversalTime().ToString("O") : null,
                          v => v != null ? DateTime.Parse(v, null, System.Globalization.DateTimeStyles.RoundtripKind) : (DateTime?)null
                      );

                entity.Property(p => p.PropId)
                      .HasConversion(
                          v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                          v => JsonSerializer.Deserialize<List<int>>(v, (JsonSerializerOptions?)null) ?? new List<int>()
                      );
            });
        }
    }
}