using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkoutGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "monthly_workout_target",
                table: "user_preferences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "target_weight_kg",
                table: "user_preferences",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000001"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ thân người thành một đường thẳng.", "Hạ ngực có kiểm soát rồi đẩy người lên." }, new List<string> { "chest" }, new List<string> { "triceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000002"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng chân rộng bằng vai, mũi chân hơi hướng ra ngoài.", "Đẩy hông ra sau, giữ lưng trung lập và đứng lên bằng cả bàn chân." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000003"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống khuỷu tay dưới vai và duỗi thẳng chân.", "Siết bụng, không võng lưng và thở đều." }, new List<string> { "abdominals" }, new List<string> { "lower back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000004"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000005"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Bắt đầu ở tư thế hít đất cao.", "Luân phiên kéo gối về ngực, giữ hông ổn định." }, new List<string> { "abdominals" }, new List<string> { "quadriceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000006"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm ngửa, co gối và đặt bàn chân trên sàn.", "Siết mông nâng hông, không ưỡn quá mức lưng dưới." }, new List<string> { "glutes" }, new List<string> { "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000007"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm ngửa, co gối và giữ lưng dưới trên sàn.", "Dùng cơ bụng nâng vai, tránh kéo cổ." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000008"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng thẳng, hai tay sát thân người.", "Bật chân sang hai bên đồng thời đưa tay qua đầu." }, new List<string> { "quadriceps" }, new List<string> { "calves", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000009"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm chắc trên ghế, giữ tạ hai bên ngực.", "Đẩy tạ lên có kiểm soát và không khóa khuỷu tay quá mức." }, new List<string> { "chest" }, new List<string> { "triceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000010"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Gập hông, giữ lưng trung lập và hai tay cầm tạ.", "Kéo khuỷu tay về sau, ép bả vai rồi hạ tạ chậm." }, new List<string> { "middle back" }, new List<string> { "biceps", "lats" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000011"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ tạ ngang vai, cổ tay thẳng.", "Đẩy tạ lên trên đầu và hạ về vị trí ban đầu có kiểm soát." }, new List<string> { "shoulders" }, new List<string> { "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000012"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng thẳng, khuỷu tay giữ sát thân người.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng." }, new List<string> { "biceps" }, new List<string> { "forearms" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000013"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ khuỷu tay sát thân và thân người ổn định.", "Duỗi khuỷu tay kéo tay cầm xuống rồi trở lại chậm." }, new List<string> { "triceps" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000014"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người." }, new List<string> { "lats" }, new List<string> { "biceps", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000015"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng sát thanh đòn, gập hông và giữ cột sống trung lập.", "Đạp sàn để đứng lên, giữ thanh gần thân người." }, new List<string> { "hamstrings" }, new List<string> { "glutes", "lower back", "quadriceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000016"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đặt thanh chắc trên lưng trên và siết thân giữa.", "Hạ người có kiểm soát, đầu gối cùng hướng mũi chân." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000017"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Từ tư thế đứng, chống tay xuống sàn và đưa chân ra sau.", "Thực hiện hít đất nếu phù hợp rồi thu chân và bật nhảy." }, new List<string> { "quadriceps" }, new List<string> { "chest", "shoulders", "abdominals" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000018"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều." }, new List<string> { "abdominals" }, new List<string> { "glutes" } });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "monthly_workout_target",
                table: "user_preferences");

            migrationBuilder.DropColumn(
                name: "target_weight_kg",
                table: "user_preferences");

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000001"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ thân người thành một đường thẳng.", "Hạ ngực có kiểm soát rồi đẩy người lên." }, new List<string> { "chest" }, new List<string> { "triceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000002"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng chân rộng bằng vai, mũi chân hơi hướng ra ngoài.", "Đẩy hông ra sau, giữ lưng trung lập và đứng lên bằng cả bàn chân." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000003"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống khuỷu tay dưới vai và duỗi thẳng chân.", "Siết bụng, không võng lưng và thở đều." }, new List<string> { "abdominals" }, new List<string> { "lower back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000004"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000005"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Bắt đầu ở tư thế hít đất cao.", "Luân phiên kéo gối về ngực, giữ hông ổn định." }, new List<string> { "abdominals" }, new List<string> { "quadriceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000006"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm ngửa, co gối và đặt bàn chân trên sàn.", "Siết mông nâng hông, không ưỡn quá mức lưng dưới." }, new List<string> { "glutes" }, new List<string> { "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000007"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm ngửa, co gối và giữ lưng dưới trên sàn.", "Dùng cơ bụng nâng vai, tránh kéo cổ." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000008"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng thẳng, hai tay sát thân người.", "Bật chân sang hai bên đồng thời đưa tay qua đầu." }, new List<string> { "quadriceps" }, new List<string> { "calves", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000009"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm chắc trên ghế, giữ tạ hai bên ngực.", "Đẩy tạ lên có kiểm soát và không khóa khuỷu tay quá mức." }, new List<string> { "chest" }, new List<string> { "triceps", "shoulders" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000010"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Gập hông, giữ lưng trung lập và hai tay cầm tạ.", "Kéo khuỷu tay về sau, ép bả vai rồi hạ tạ chậm." }, new List<string> { "middle back" }, new List<string> { "biceps", "lats" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000011"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ tạ ngang vai, cổ tay thẳng.", "Đẩy tạ lên trên đầu và hạ về vị trí ban đầu có kiểm soát." }, new List<string> { "shoulders" }, new List<string> { "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000012"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng thẳng, khuỷu tay giữ sát thân người.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng." }, new List<string> { "biceps" }, new List<string> { "forearms" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000013"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Giữ khuỷu tay sát thân và thân người ổn định.", "Duỗi khuỷu tay kéo tay cầm xuống rồi trở lại chậm." }, new List<string> { "triceps" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000014"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người." }, new List<string> { "lats" }, new List<string> { "biceps", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000015"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng sát thanh đòn, gập hông và giữ cột sống trung lập.", "Đạp sàn để đứng lên, giữ thanh gần thân người." }, new List<string> { "hamstrings" }, new List<string> { "glutes", "lower back", "quadriceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000016"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đặt thanh chắc trên lưng trên và siết thân giữa.", "Hạ người có kiểm soát, đầu gối cùng hướng mũi chân." }, new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000017"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Từ tư thế đứng, chống tay xuống sàn và đưa chân ra sau.", "Thực hiện hít đất nếu phù hợp rồi thu chân và bật nhảy." }, new List<string> { "quadriceps" }, new List<string> { "chest", "shoulders", "abdominals" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000018"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều." }, new List<string> { "abdominals" }, new List<string> { "glutes" } });
        }
    }
}
