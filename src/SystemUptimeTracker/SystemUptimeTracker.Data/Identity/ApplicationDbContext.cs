using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SystemUptimeTracker.Data.Identity;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(applicationUser => applicationUser.DisplayName)
                .HasMaxLength(256)
                .HasDefaultValue(string.Empty);

            user.Property(applicationUser => applicationUser.IsActive)
                .HasDefaultValue(true);

            user.Property(applicationUser => applicationUser.CreatedAtUtc)
                .HasDefaultValueSql("SYSUTCDATETIME()");
        });
    }
}