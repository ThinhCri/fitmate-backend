using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Fitmate.Infrastructure.Persistence.Migrations;

[DbContext(typeof(FitmateDbContext))]
[Migration("20261006080000_InsertExpandedExerciseCatalog")]
public sealed class InsertExpandedExerciseCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var rows = new[]
        {
            Row(19, "Incline_Push-Up", "Hít đất dốc lên", "Incline Push-Up", 0, "beginner", "body only", new[] { "chest" }, new[] { "shoulders", "triceps" }, "Chống tay lên bề mặt chắc chắn cao hơn sàn.", "Giữ thân thẳng, hạ ngực rồi đẩy người lên có kiểm soát."),
            Row(20, "Chin-Up", "Hít xà tay ngửa", "Chin-Up", 0, "beginner", "body only", new[] { "lats" }, new[] { "biceps", "forearms", "middle back" }, "Nắm xà với lòng bàn tay hướng về người.", "Kéo ngực về phía xà, tránh đung đưa thân người."),
            Row(21, "Russian_Twist", "Xoay bụng kiểu Nga", "Russian Twist", 0, "intermediate", "body only", new[] { "abdominals" }, new[] { "lower back" }, "Ngồi hơi ngả thân, giữ cột sống trung lập.", "Xoay thân sang hai bên bằng cơ bụng, không giật vai."),
            Row(22, "Flat_Bench_Lying_Leg_Raise", "Nâng chân nằm ghế", "Flat Bench Lying Leg Raise", 0, "beginner", "body only", new[] { "abdominals" }, Array.Empty<string>(), "Nằm chắc trên ghế, hai chân duỗi thẳng.", "Nâng chân bằng cơ bụng rồi hạ chậm, không cong lưng dưới."),
            Row(23, "Barbell_Hip_Thrust", "Hip thrust với thanh đòn", "Barbell Hip Thrust", 0, "intermediate", "barbell", new[] { "glutes" }, new[] { "calves", "hamstrings" }, "Tựa lưng trên vào ghế và đặt thanh đòn chắc trên hông.", "Đẩy hông lên bằng cơ mông rồi hạ xuống có kiểm soát."),
            Row(24, "Barbell_Seated_Calf_Raise", "Nhón bắp chân ngồi với thanh đòn", "Barbell Seated Calf Raise", 0, "beginner", "barbell", new[] { "calves" }, Array.Empty<string>(), "Ngồi chắc, đặt thanh đòn có đệm trên đùi.", "Nhón gót hết biên độ rồi hạ chậm."),
            Row(25, "Lateral_Raise_-_With_Bands", "Dang vai với dây kháng lực", "Band Lateral Raise", 0, "beginner", "bands", new[] { "shoulders" }, Array.Empty<string>(), "Đứng giữa dây, giữ tay cầm hai bên.", "Dang tay đến ngang vai, giữ khuỷu hơi cong rồi hạ chậm."),
            Row(26, "Scapular_Pull-Up", "Kéo xà bằng bả vai", "Scapular Pull-Up", 0, "beginner", null, new[] { "traps" }, new[] { "lats", "middle back" }, "Treo người trên xà với tay duỗi.", "Hạ và kéo bả vai để nâng thân nhẹ mà không gập khuỷu tay."),
            Row(27, "Cable_Russian_Twists", "Xoay bụng với cáp", "Cable Russian Twists", 0, "beginner", "cable", new[] { "abdominals" }, Array.Empty<string>(), "Đứng ngang máy cáp và giữ tay cầm bằng hai tay.", "Xoay thân khỏi máy bằng cơ bụng, giữ hông ổn định."),
            Row(28, "Seated_Calf_Raise", "Nhón bắp chân với máy ngồi", "Seated Calf Raise", 0, "beginner", "machine", new[] { "calves" }, Array.Empty<string>(), "Điều chỉnh đệm máy nằm chắc trên đùi.", "Nhón gót hết biên độ, dừng ngắn rồi hạ có kiểm soát."),
            Row(29, "Band_Assisted_Pull-Up", "Hít xà có dây hỗ trợ", "Band Assisted Pull-Up", 0, "beginner", "other", new[] { "lats" }, new[] { "abdominals", "forearms", "middle back" }, "Cố định dây hỗ trợ vào xà và đặt chân hoặc gối vào dây.", "Kéo ngực lên xà, giữ thân ổn định rồi hạ chậm."),
            Row(30, "Hanging_Leg_Raise", "Nâng chân treo xà", "Hanging Leg Raise", 0, "advanced", "body only", new[] { "abdominals" }, Array.Empty<string>(), "Treo người chắc trên xà, siết vai và thân giữa.", "Nâng chân bằng cơ bụng, tránh đung đưa và hạ chậm.")
        };

        foreach (var row in rows)
        {
            var equipment = row.Equipment is null ? "NULL" : $"'{Escape(row.Equipment)}'";
            migrationBuilder.Sql($$"""
                INSERT INTO exercise_catalog
                    (id, "CreatedAt", "DeletedAt", "Difficulty", "Equipment", "ImageEndUrl", "ImageStartUrl", "InstructionsVi", "IsActive", "NameEn", "NameVi", "PrimaryMuscles", "SecondaryMuscles", "Slug", "Source", "SourceLicense", "SourceUrl", "Type", "UpdatedAt")
                VALUES
                    ('{{row.Id}}', TIMESTAMPTZ '2026-10-06 00:00:00+00', NULL,
                     '{{Escape(row.Difficulty)}}', {{equipment}},
                     'storage://exercise-catalog/{{Escape(row.Slug)}}/1.jpg',
                     'storage://exercise-catalog/{{Escape(row.Slug)}}/0.jpg',
                     {{ArrayLiteral(row.Instructions)}}, TRUE,
                     '{{Escape(row.NameEn)}}', '{{Escape(row.NameVi)}}',
                     {{ArrayLiteral(row.Primary)}}, {{ArrayLiteral(row.Secondary)}},
                     '{{Escape(row.Slug)}}', 'free-exercise-db', 'Unlicense',
                     'https://github.com/yuhonas/free-exercise-db', {{row.Type}},
                     TIMESTAMPTZ '2026-10-06 00:00:00+00')
                ON CONFLICT (id) DO NOTHING;
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DELETE FROM exercise_catalog WHERE id >= '00000000-0000-4000-8000-000000000019' AND id <= '00000000-0000-4000-8000-000000000030';");
    }

    private static (Guid Id, string Slug, string NameVi, string NameEn, int Type, string Difficulty, string? Equipment, string[] Primary, string[] Secondary, string[] Instructions) Row(
        int id, string slug, string nameVi, string nameEn, int type, string difficulty, string? equipment, string[] primary, string[] secondary, params string[] instructions) =>
        (Guid.Parse($"00000000-0000-4000-8000-{id:D12}"), slug, nameVi, nameEn, type, difficulty, equipment, primary, secondary, instructions);

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string ArrayLiteral(IEnumerable<string> values) =>
        "ARRAY[" + string.Join(",", values.Select(value => $"'{Escape(value)}'")) + "]::text[]";
}
