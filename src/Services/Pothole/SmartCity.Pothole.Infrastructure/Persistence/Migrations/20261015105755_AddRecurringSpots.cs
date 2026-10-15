using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace SmartCity.Pothole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringSpots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_spots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<Point>(type: "geography(Point,4326)", nullable: false),
                    district_id = table.Column<int>(type: "integer", nullable: true),
                    pothole_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    pothole_count = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_spots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_spots_district_id",
                table: "recurring_spots",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_spots_location",
                table: "recurring_spots",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_spots");
        }
    }
}
