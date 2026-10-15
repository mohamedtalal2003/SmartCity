using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCity.Pothole.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVerificationFrameId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "frame_id",
                table: "observations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_observations_pothole_frame_id",
                table: "observations",
                columns: new[] { "pothole_id", "frame_id" },
                unique: true,
                filter: "frame_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_observations_pothole_frame_id",
                table: "observations");

            migrationBuilder.DropColumn(
                name: "frame_id",
                table: "observations");
        }
    }
}
