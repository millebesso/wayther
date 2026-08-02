using Microsoft.EntityFrameworkCore;

namespace Wayther.Infrastructure;

public class WaytherDbContext(DbContextOptions<WaytherDbContext> options) : DbContext(options)
{
    public DbSet<ForecastCache> ForecastCache => Set<ForecastCache>();
    public DbSet<SharedRoute> SharedRoutes => Set<SharedRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var forecast = modelBuilder.Entity<ForecastCache>();
        forecast.ToTable("forecast_cache");
        forecast.HasKey(x => new { x.Lat4, x.Lon4 });
        forecast.Property(x => x.Lat4).HasColumnName("lat4");
        forecast.Property(x => x.Lon4).HasColumnName("lon4");
        forecast.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        forecast.Property(x => x.FetchedAt).HasColumnName("fetched_at");

        var share = modelBuilder.Entity<SharedRoute>();
        share.ToTable("shared_route");
        share.HasKey(x => x.Id);
        share.Property(x => x.Id).HasColumnName("id");
        share.Property(x => x.Waypoints).HasColumnName("waypoints").HasColumnType("jsonb");
        share.Property(x => x.DepartureTime).HasColumnName("departure_time");
        share.Property(x => x.IntervalMinutes).HasColumnName("interval_minutes");
        share.Property(x => x.CreatedAt).HasColumnName("created_at");
    }
}
