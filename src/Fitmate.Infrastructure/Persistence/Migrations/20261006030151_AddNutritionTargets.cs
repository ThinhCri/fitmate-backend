using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNutritionTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "target_calories",
                table: "user_preferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_carbs_g",
                table: "user_preferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_fat_g",
                table: "user_preferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "target_protein_g",
                table: "user_preferences",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "target_calories",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "target_carbs_g",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "target_fat_g",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "target_protein_g",
                table: "user_preferences");
        }
    }
}
