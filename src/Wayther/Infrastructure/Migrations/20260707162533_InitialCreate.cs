using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wayther.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_cache",
                columns: table => new
                {
                    lat4 = table.Column<double>(type: "double precision", nullable: false),
                    lon4 = table.Column<double>(type: "double precision", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_forecast_cache", x => new { x.lat4, x.lon4 });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_cache");
        }
    }
}
