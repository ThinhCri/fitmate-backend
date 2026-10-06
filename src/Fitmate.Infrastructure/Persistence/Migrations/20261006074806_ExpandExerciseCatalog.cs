using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fitmate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandExerciseCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                columns: new[] { "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "storage://exercise-catalog/Bodyweight_Walking_Lunge/1.jpg", "storage://exercise-catalog/Bodyweight_Walking_Lunge/0.jpg", new List<string> { "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên." }, "Bodyweight Walking Lunge", new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" }, "Bodyweight_Walking_Lunge" });

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
                columns: new[] { "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "storage://exercise-catalog/Single_Leg_Glute_Bridge/1.jpg", "storage://exercise-catalog/Single_Leg_Glute_Bridge/0.jpg", new List<string> { "Nằm ngửa, co một gối và duỗi chân còn lại.", "Siết mông nâng hông, giữ khung chậu cân bằng rồi hạ chậm." }, "Single Leg Glute Bridge", "Nâng hông một chân", new List<string> { "glutes" }, new List<string> { "hamstrings" }, "Single_Leg_Glute_Bridge" });

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
                columns: new[] { "Difficulty", "Equipment", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "intermediate", "other", "storage://exercise-catalog/Rope_Jumping/1.jpg", "storage://exercise-catalog/Rope_Jumping/0.jpg", new List<string> { "Đứng thẳng, giữ tay cầm dây hai bên hông.", "Xoay dây bằng cổ tay và bật nhảy nhẹ bằng mũi chân." }, "Rope Jumping", "Nhảy dây", new List<string> { "quadriceps" }, new List<string> { "calves", "hamstrings" }, "Rope_Jumping" });

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
                columns: new[] { "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "storage://exercise-catalog/Dumbbell_Bicep_Curl/1.jpg", "storage://exercise-catalog/Dumbbell_Bicep_Curl/0.jpg", new List<string> { "Giữ khuỷu tay sát thân người và cổ tay trung lập.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng." }, "Dumbbell Bicep Curl", "Cuốn tạ đơn", new List<string> { "biceps" }, new List<string> { "forearms" }, "Dumbbell_Bicep_Curl" });

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
                columns: new[] { "Difficulty", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "beginner", "storage://exercise-catalog/Wide-Grip_Lat_Pulldown/1.jpg", "storage://exercise-catalog/Wide-Grip_Lat_Pulldown/0.jpg", new List<string> { "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người." }, "Wide-Grip Lat Pulldown", "Kéo xô tay rộng", new List<string> { "lats" }, new List<string> { "biceps", "middle back", "shoulders" }, "Wide-Grip_Lat_Pulldown" });

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
                columns: new[] { "Difficulty", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "beginner", "storage://exercise-catalog/Push_Up_to_Side_Plank/1.jpg", "storage://exercise-catalog/Push_Up_to_Side_Plank/0.jpg", new List<string> { "Thực hiện một lần hít đất với thân người ổn định.", "Xoay người mở một tay lên trần, trở về giữa rồi đổi bên." }, "Push Up to Side Plank", "Hít đất chuyển plank nghiêng", new List<string> { "chest" }, new List<string> { "abdominals", "shoulders", "triceps" }, "Push_Up_to_Side_Plank" });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000018"),
                columns: new[] { "Difficulty", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "beginner", "storage://exercise-catalog/Side_Bridge/1.jpg", "storage://exercise-catalog/Side_Bridge/0.jpg", new List<string> { "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều." }, "Side Bridge", new List<string> { "abdominals" }, new List<string> { "shoulders" }, "Side_Bridge" });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000019"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống tay lên bề mặt chắc chắn cao hơn sàn.", "Giữ thân thẳng, hạ ngực rồi đẩy người lên có kiểm soát." }, new List<string> { "chest" }, new List<string> { "shoulders", "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000020"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nắm xà với lòng bàn tay hướng về người.", "Kéo ngực về phía xà, tránh đung đưa thân người." }, new List<string> { "lats" }, new List<string> { "biceps", "forearms", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000021"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi hơi ngả thân, giữ cột sống trung lập.", "Xoay thân sang hai bên bằng cơ bụng, không giật vai." }, new List<string> { "abdominals" }, new List<string> { "lower back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000022"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm chắc trên ghế, hai chân duỗi thẳng.", "Nâng chân bằng cơ bụng rồi hạ chậm, không cong lưng dưới." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000023"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Tựa lưng trên vào ghế và đặt thanh đòn chắc trên hông.", "Đẩy hông lên bằng cơ mông rồi hạ xuống có kiểm soát." }, new List<string> { "glutes" }, new List<string> { "calves", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000024"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi chắc, đặt thanh đòn có đệm trên đùi.", "Nhón gót hết biên độ rồi hạ chậm." }, new List<string> { "calves" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000025"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng giữa dây, giữ tay cầm hai bên.", "Dang tay đến ngang vai, giữ khuỷu hơi cong rồi hạ chậm." }, new List<string> { "shoulders" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000026"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Treo người trên xà với tay duỗi.", "Hạ và kéo bả vai để nâng thân nhẹ mà không gập khuỷu tay." }, new List<string> { "traps" }, new List<string> { "lats", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000027"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng ngang máy cáp và giữ tay cầm bằng hai tay.", "Xoay thân khỏi máy bằng cơ bụng, giữ hông ổn định." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000028"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Điều chỉnh đệm máy nằm chắc trên đùi.", "Nhón gót hết biên độ, dừng ngắn rồi hạ có kiểm soát." }, new List<string> { "calves" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000029"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Cố định dây hỗ trợ vào xà và đặt chân hoặc gối vào dây.", "Kéo ngực lên xà, giữ thân ổn định rồi hạ chậm." }, new List<string> { "lats" }, new List<string> { "abdominals", "forearms", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000030"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Treo người chắc trên xà, siết vai và thân giữa.", "Nâng chân bằng cơ bụng, tránh đung đưa và hạ chậm." }, new List<string> { "abdominals" }, new List<string>() });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                columns: new[] { "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "storage://exercise-catalog/Walking_Lunge/1.jpg", "storage://exercise-catalog/Walking_Lunge/0.jpg", new List<string> { "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên." }, "Walking Lunge", new List<string> { "quadriceps" }, new List<string> { "glutes", "hamstrings" }, "Walking_Lunge" });

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
                columns: new[] { "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "storage://exercise-catalog/Glute_Bridge/1.jpg", "storage://exercise-catalog/Glute_Bridge/0.jpg", new List<string> { "Nằm ngửa, co gối và đặt bàn chân trên sàn.", "Siết mông nâng hông, không ưỡn quá mức lưng dưới." }, "Glute Bridge", "Nâng hông", new List<string> { "glutes" }, new List<string> { "hamstrings" }, "Glute_Bridge" });

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
                columns: new[] { "Difficulty", "Equipment", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "beginner", "body only", "storage://exercise-catalog/Jumping_Jacks/1.jpg", "storage://exercise-catalog/Jumping_Jacks/0.jpg", new List<string> { "Đứng thẳng, hai tay sát thân người.", "Bật chân sang hai bên đồng thời đưa tay qua đầu." }, "Jumping Jacks", "Bật nhảy dang tay chân", new List<string> { "quadriceps" }, new List<string> { "calves", "shoulders" }, "Jumping_Jacks" });

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
                columns: new[] { "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "storage://exercise-catalog/Standing_Dumbbell_Curl/1.jpg", "storage://exercise-catalog/Standing_Dumbbell_Curl/0.jpg", new List<string> { "Đứng thẳng, khuỷu tay giữ sát thân người.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng." }, "Standing Dumbbell Curl", "Cuốn tạ đơn đứng", new List<string> { "biceps" }, new List<string> { "forearms" }, "Standing_Dumbbell_Curl" });

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
                columns: new[] { "Difficulty", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "intermediate", "storage://exercise-catalog/Lat_Pulldown/1.jpg", "storage://exercise-catalog/Lat_Pulldown/0.jpg", new List<string> { "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người." }, "Lat Pulldown", "Kéo xô trước ngực", new List<string> { "lats" }, new List<string> { "biceps", "middle back" }, "Lat_Pulldown" });

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
                columns: new[] { "Difficulty", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "intermediate", "storage://exercise-catalog/Burpee/1.jpg", "storage://exercise-catalog/Burpee/0.jpg", new List<string> { "Từ tư thế đứng, chống tay xuống sàn và đưa chân ra sau.", "Thực hiện hít đất nếu phù hợp rồi thu chân và bật nhảy." }, "Burpee", "Burpee", new List<string> { "quadriceps" }, new List<string> { "chest", "shoulders", "abdominals" }, "Burpee" });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000018"),
                columns: new[] { "Difficulty", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "NameEn", "PrimaryMuscles", "SecondaryMuscles", "Slug" },
                values: new object[] { "intermediate", "storage://exercise-catalog/Side_Plank/1.jpg", "storage://exercise-catalog/Side_Plank/0.jpg", new List<string> { "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều." }, "Side Plank", new List<string> { "abdominals" }, new List<string> { "glutes" }, "Side_Plank" });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000019"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Chống tay lên bề mặt chắc chắn cao hơn sàn.", "Giữ thân thẳng, hạ ngực rồi đẩy người lên có kiểm soát." }, new List<string> { "chest" }, new List<string> { "shoulders", "triceps" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000020"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nắm xà với lòng bàn tay hướng về người.", "Kéo ngực về phía xà, tránh đung đưa thân người." }, new List<string> { "lats" }, new List<string> { "biceps", "forearms", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000021"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi hơi ngả thân, giữ cột sống trung lập.", "Xoay thân sang hai bên bằng cơ bụng, không giật vai." }, new List<string> { "abdominals" }, new List<string> { "lower back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000022"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Nằm chắc trên ghế, hai chân duỗi thẳng.", "Nâng chân bằng cơ bụng rồi hạ chậm, không cong lưng dưới." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000023"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Tựa lưng trên vào ghế và đặt thanh đòn chắc trên hông.", "Đẩy hông lên bằng cơ mông rồi hạ xuống có kiểm soát." }, new List<string> { "glutes" }, new List<string> { "calves", "hamstrings" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000024"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Ngồi chắc, đặt thanh đòn có đệm trên đùi.", "Nhón gót hết biên độ rồi hạ chậm." }, new List<string> { "calves" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000025"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng giữa dây, giữ tay cầm hai bên.", "Dang tay đến ngang vai, giữ khuỷu hơi cong rồi hạ chậm." }, new List<string> { "shoulders" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000026"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Treo người trên xà với tay duỗi.", "Hạ và kéo bả vai để nâng thân nhẹ mà không gập khuỷu tay." }, new List<string> { "traps" }, new List<string> { "lats", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000027"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Đứng ngang máy cáp và giữ tay cầm bằng hai tay.", "Xoay thân khỏi máy bằng cơ bụng, giữ hông ổn định." }, new List<string> { "abdominals" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000028"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Điều chỉnh đệm máy nằm chắc trên đùi.", "Nhón gót hết biên độ, dừng ngắn rồi hạ có kiểm soát." }, new List<string> { "calves" }, new List<string>() });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000029"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Cố định dây hỗ trợ vào xà và đặt chân hoặc gối vào dây.", "Kéo ngực lên xà, giữ thân ổn định rồi hạ chậm." }, new List<string> { "lats" }, new List<string> { "abdominals", "forearms", "middle back" } });

            migrationBuilder.UpdateData(
                table: "exercise_catalog",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-4000-8000-000000000030"),
                columns: new[] { "InstructionsVi", "PrimaryMuscles", "SecondaryMuscles" },
                values: new object[] { new List<string> { "Treo người chắc trên xà, siết vai và thân giữa.", "Nâng chân bằng cơ bụng, tránh đung đưa và hạ chậm." }, new List<string> { "abdominals" }, new List<string>() });
        }
    }
}
