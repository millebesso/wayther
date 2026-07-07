using Microsoft.EntityFrameworkCore;

namespace Wayther.Infrastructure;

public class WaytherDbContext(DbContextOptions<WaytherDbContext> options) : DbContext(options)
{
    public DbSet<ForecastCache> ForecastCache => Set<ForecastCache>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var forecast = modelBuilder.Entity<ForecastCache>();
        forecast.ToTable("forecast_cache");
        forecast.HasKey(x => new { x.Lat4, x.Lon4 });
        forecast.Property(x => x.Lat4).HasColumnName("lat4");
        forecast.Property(x => x.Lon4).HasColumnName("lon4");
        forecast.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        forecast.Property(x => x.FetchedAt).HasColumnName("fetched_at");
    }
}
