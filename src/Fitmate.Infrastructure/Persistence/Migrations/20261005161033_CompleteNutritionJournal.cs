using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteNutritionJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Grams",
                table: "meal_logs",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MealSlot",
                table: "meal_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "meal_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ServingQuantity",
                table: "meal_logs",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServingUnit",
                table: "meal_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Grams",
                table: "meal_logs");

            migrationBuilder.DropColumn(
                name: "MealSlot",
                table: "meal_logs");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "meal_logs");

            migrationBuilder.DropColumn(
                name: "ServingQuantity",
                table: "meal_logs");

            migrationBuilder.DropColumn(
                name: "ServingUnit",
                table: "meal_logs");
        }
    }
}
