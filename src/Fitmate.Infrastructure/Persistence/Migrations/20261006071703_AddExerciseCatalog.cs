using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exercise_catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    NameVi = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Equipment = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PrimaryMuscles = table.Column<List<string>>(type: "text[]", nullable: false),
                    SecondaryMuscles = table.Column<List<string>>(type: "text[]", nullable: false),
                    InstructionsVi = table.Column<List<string>>(type: "text[]", nullable: false),
                    ImageStartUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImageEndUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceLicense = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercise_catalog", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "exercise_catalog",
                columns: new[] { "id", "CreatedAt", "DeletedAt", "Difficulty", "Equipment", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "IsActive", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug", "Source", "SourceLicense", "SourceUrl", "Type", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-4000-8000-000000000001"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Pushups/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Pushups/0.jpg", new List<string> { "Giữ thân người thành một đường thẳng.", "Hạ ngực có kiểm soát rồi đẩy người lên." }, true, "Push-ups", "Hít đất", new List<string> { "chest" }, new List<string> { "triceps", "shoulders" }, "Pushups", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000002"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Bodyweight_Squat/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Bodyweight_Squat/0.jpg", new List<string> { "Đứng chân rộng bằng vai, mũi chân hơi hướng ra ngoài.", "Đẩy hông ra sau, giữ lưng trung lập và đứng lên bằng cả bàn chân." }, true, "Bodyweight Squat", "Squat không tạ", new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" }, "Bodyweight_Squat", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000003"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Plank/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Plank/0.jpg", new List<string> { "Chống khuỷu tay dưới vai và duỗi thẳng chân.", "Siết bụng, không võng lưng và thở đều." }, true, "Plank", "Plank", new List<string> { "abdominals" }, new List<string> { "lower back" }, "Plank", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 1, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000004"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Walking_Lunge/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Walking_Lunge/0.jpg", new List<string> { "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên." }, true, "Walking Lunge", "Chùng chân bước tới", new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" }, "Walking_Lunge", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000005"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Mountain_Climbers/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Mountain_Climbers/0.jpg", new List<string> { "Bắt đầu ở tư thế hít đất cao.", "Luân phiên kéo gối về ngực, giữ hông ổn định." }, true, "Mountain Climbers", "Leo núi tại chỗ", new List<string> { "abdominals" }, new List<string> { "quadriceps", "shoulders" }, "Mountain_Climbers", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 1, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000006"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Glute_Bridge/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Glute_Bridge/0.jpg", new List<string> { "Nằm ngửa, co gối và đặt bàn chân trên sàn.", "Siết mông nâng hông, không ưỡn quá mức lưng dưới." }, true, "Glute Bridge", "Nâng hông", new List<string> { "glutes" }, new List<string> { "hamstrings" }, "Glute_Bridge", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000007"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Crunches/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Crunches/0.jpg", new List<string> { "Nằm ngửa, co gối và giữ lưng dưới trên sàn.", "Dùng cơ bụng nâng vai, tránh kéo cổ." }, true, "Crunches", "Gập bụng", new List<string> { "abdominals" }, new List<string>(), "Crunches", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000008"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Jumping_Jacks/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Jumping_Jacks/0.jpg", new List<string> { "Đứng thẳng, hai tay sát thân người.", "Bật chân sang hai bên đồng thời đưa tay qua đầu." }, true, "Jumping Jacks", "Bật nhảy dang tay chân", new List<string> { "quadriceps" }, new List<string> { "calves", "shoulders" }, "Jumping_Jacks", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 1, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000009"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "dumbbell", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Dumbbell_Bench_Press/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Dumbbell_Bench_Press/0.jpg", new List<string> { "Nằm chắc trên ghế, giữ tạ hai bên ngực.", "Đẩy tạ lên có kiểm soát và không khóa khuỷu tay quá mức." }, true, "Dumbbell Bench Press", "Đẩy ngực với tạ đơn", new List<string> { "chest" }, new List<string> { "triceps", "shoulders" }, "Dumbbell_Bench_Press", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000010"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "dumbbell", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Bent_Over_Two-Dumbbell_Row/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Bent_Over_Two-Dumbbell_Row/0.jpg", new List<string> { "Gập hông, giữ lưng trung lập và hai tay cầm tạ.", "Kéo khuỷu tay về sau, ép bả vai rồi hạ tạ chậm." }, true, "Bent Over Dumbbell Row", "Kéo tạ đơn cúi người", new List<string> { "middle back" }, new List<string> { "biceps", "lats" }, "Bent_Over_Two-Dumbbell_Row", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000011"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "dumbbell", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Dumbbell_Shoulder_Press/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Dumbbell_Shoulder_Press/0.jpg", new List<string> { "Giữ tạ ngang vai, cổ tay thẳng.", "Đẩy tạ lên trên đầu và hạ về vị trí ban đầu có kiểm soát." }, true, "Dumbbell Shoulder Press", "Đẩy vai với tạ đơn", new List<string> { "shoulders" }, new List<string> { "triceps" }, "Dumbbell_Shoulder_Press", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000012"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "beginner", "dumbbell", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Standing_Dumbbell_Curl/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Standing_Dumbbell_Curl/0.jpg", new List<string> { "Đứng thẳng, khuỷu tay giữ sát thân người.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng." }, true, "Standing Dumbbell Curl", "Cuốn tạ đơn đứng", new List<string> { "biceps" }, new List<string> { "forearms" }, "Standing_Dumbbell_Curl", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000013"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "cable", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Triceps_Pushdown/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Triceps_Pushdown/0.jpg", new List<string> { "Giữ khuỷu tay sát thân và thân người ổn định.", "Duỗi khuỷu tay kéo tay cầm xuống rồi trở lại chậm." }, true, "Triceps Pushdown", "Kéo cáp tay sau", new List<string> { "triceps" }, new List<string>(), "Triceps_Pushdown", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000014"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "cable", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Lat_Pulldown/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Lat_Pulldown/0.jpg", new List<string> { "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người." }, true, "Lat Pulldown", "Kéo xô trước ngực", new List<string> { "lats" }, new List<string> { "biceps", "middle back" }, "Lat_Pulldown", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000015"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "advanced", "barbell", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Barbell_Deadlift/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Barbell_Deadlift/0.jpg", new List<string> { "Đứng sát thanh đòn, gập hông và giữ cột sống trung lập.", "Đạp sàn để đứng lên, giữ thanh gần thân người." }, true, "Barbell Deadlift", "Deadlift với thanh đòn", new List<string> { "hamstrings" }, new List<string> { "glutes", "lower back", "quadriceps" }, "Barbell_Deadlift", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000016"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "advanced", "barbell", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Barbell_Squat/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Barbell_Squat/0.jpg", new List<string> { "Đặt thanh chắc trên lưng trên và siết thân giữa.", "Hạ người có kiểm soát, đầu gối cùng hướng mũi chân." }, true, "Barbell Squat", "Squat với thanh đòn", new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" }, "Barbell_Squat", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000017"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Burpee/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Burpee/0.jpg", new List<string> { "Từ tư thế đứng, chống tay xuống sàn và đưa chân ra sau.", "Thực hiện hít đất nếu phù hợp rồi thu chân và bật nhảy." }, true, "Burpee", "Burpee", new List<string> { "quadriceps" }, new List<string> { "chest", "shoulders", "abdominals" }, "Burpee", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 0, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("00000000-0000-4000-8000-000000000018"), new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc), null, "intermediate", "body only", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Side_Plank/1.jpg", "https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/Side_Plank/0.jpg", new List<string> { "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều." }, true, "Side Plank", "Plank nghiêng", new List<string> { "abdominals" }, new List<string> { "glutes" }, "Side_Plank", "free-exercise-db", "Unlicense", "https://github.com/yuhonas/free-exercise-db", 1, new DateTime(2026, 10, 6, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_catalog_IsActive_NameVi",
                table: "exercise_catalog",
                columns: new[] { "IsActive", "NameVi" });

            migrationBuilder.CreateIndex(
                name: "IX_exercise_catalog_Slug",
                table: "exercise_catalog",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_catalog");
        }
    }
}
