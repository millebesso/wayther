using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wayther.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedRoute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shared_route",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    waypoints = table.Column<string>(type: "jsonb", nullable: false),
                    departure_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shared_route", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shared_route");
        }
    }
}
