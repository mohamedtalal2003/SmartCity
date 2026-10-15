using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartCity.WorkOrder.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceDistrictEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "District",
                table: "work_orders");

            migrationBuilder.AddColumn<int>(
                name: "DistrictId",
                table: "work_orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistrictName",
                table: "work_orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistrictId",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "DistrictName",
                table: "work_orders");

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "work_orders",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
