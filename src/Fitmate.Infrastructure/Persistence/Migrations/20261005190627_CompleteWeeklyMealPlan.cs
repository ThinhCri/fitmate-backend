using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteWeeklyMealPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weekly_meal_plan_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    MealSlot = table.Column<int>(type: "integer", nullable: false),
                    FoodName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ServingQuantity = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    ServingUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Grams = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Calories = table.Column<int>(type: "integer", nullable: false),
                    ProteinG = table.Column<int>(type: "integer", nullable: false),
                    CarbsG = table.Column<int>(type: "integer", nullable: false),
                    FatG = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weekly_meal_plan_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "weekly_meal_plan_consumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    MealLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumedDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weekly_meal_plan_consumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weekly_meal_plan_consumptions_meal_logs_MealLogId",
                        column: x => x.MealLogId,
                        principalTable: "meal_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_weekly_meal_plan_consumptions_weekly_meal_plan_items_PlanIt~",
                        column: x => x.PlanItemId,
                        principalTable: "weekly_meal_plan_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_weekly_meal_plan_consumptions_MealLogId",
                table: "weekly_meal_plan_consumptions",
                column: "MealLogId");

            migrationBuilder.CreateIndex(
                name: "IX_weekly_meal_plan_consumptions_PlanItemId_ConsumedDate",
                table: "weekly_meal_plan_consumptions",
                columns: new[] { "PlanItemId", "ConsumedDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_weekly_meal_plan_consumptions_UserId_ConsumedDate",
                table: "weekly_meal_plan_consumptions",
                columns: new[] { "UserId", "ConsumedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_weekly_meal_plan_items_UserId_WeekStart_DayOfWeek_MealSlot",
                table: "weekly_meal_plan_items",
                columns: new[] { "UserId", "WeekStart", "DayOfWeek", "MealSlot" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weekly_meal_plan_consumptions");

            migrationBuilder.DropTable(
                name: "weekly_meal_plan_items");
        }
    }
}
