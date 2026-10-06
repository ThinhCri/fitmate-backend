using Fitmate.Domain.Common;
using Fitmate.Domain.Workout;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitmate.Infrastructure.Persistence.Configurations;

public sealed class ExerciseCatalogConfiguration : IEntityTypeConfiguration<ExerciseCatalogEntry>
{
    private static readonly DateTime SeededAt = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
    private const string Source = "free-exercise-db";
    private const string SourceUrl = "https://github.com/yuhonas/free-exercise-db";
    private const string License = "Unlicense";

    public void Configure(EntityTypeBuilder<ExerciseCatalogEntry> entry)
    {
        entry.ToTable("exercise_catalog");
        entry.HasKey(item => item.Id);
        entry.Property(item => item.Id).HasColumnName("id");
        entry.Property(item => item.Slug).HasMaxLength(160).IsRequired();
        entry.Property(item => item.NameVi).HasMaxLength(200).IsRequired();
        entry.Property(item => item.NameEn).HasMaxLength(200).IsRequired();
        entry.Property(item => item.Type).HasConversion<int>();
        entry.Property(item => item.Difficulty).HasMaxLength(32).IsRequired();
        entry.Property(item => item.Equipment).HasMaxLength(80);
        entry.Property(item => item.PrimaryMuscles).HasColumnType("text[]");
        entry.Property(item => item.SecondaryMuscles).HasColumnType("text[]");
        entry.Property(item => item.InstructionsVi).HasColumnType("text[]");
        entry.Property(item => item.ImageStartUrl).HasMaxLength(1000);
        entry.Property(item => item.ImageEndUrl).HasMaxLength(1000);
        entry.Property(item => item.Source).HasMaxLength(80).IsRequired();
        entry.Property(item => item.SourceUrl).HasMaxLength(500).IsRequired();
        entry.Property(item => item.SourceLicense).HasMaxLength(80).IsRequired();
        entry.HasIndex(item => item.Slug).IsUnique();
        entry.HasIndex(item => new { item.IsActive, item.NameVi });

        entry.HasData(
            Seed("00000000-0000-4000-8000-000000000001", "Pushups", "Hít đất", "Push-ups", ExerciseType.Reps, "beginner", "body only", new[] { "chest" }, new[] { "triceps", "shoulders" }, "Giữ thân người thành một đường thẳng.", "Hạ ngực có kiểm soát rồi đẩy người lên."),
            Seed("00000000-0000-4000-8000-000000000002", "Bodyweight_Squat", "Squat không tạ", "Bodyweight Squat", ExerciseType.Reps, "beginner", "body only", new[] { "quadriceps" }, new[] { "glutes", "hamstrings" }, "Đứng chân rộng bằng vai, mũi chân hơi hướng ra ngoài.", "Đẩy hông ra sau, giữ lưng trung lập và đứng lên bằng cả bàn chân."),
            Seed("00000000-0000-4000-8000-000000000003", "Plank", "Plank", "Plank", ExerciseType.Time, "beginner", "body only", new[] { "abdominals" }, new[] { "lower back" }, "Chống khuỷu tay dưới vai và duỗi thẳng chân.", "Siết bụng, không võng lưng và thở đều."),
            Seed("00000000-0000-4000-8000-000000000004", "Bodyweight_Walking_Lunge", "Chùng chân bước tới", "Bodyweight Walking Lunge", ExerciseType.Reps, "beginner", "body only", new[] { "quadriceps" }, new[] { "glutes", "hamstrings" }, "Bước một chân về trước đủ dài.", "Hạ gối sau gần sàn rồi đẩy người tiến lên."),
            Seed("00000000-0000-4000-8000-000000000005", "Mountain_Climbers", "Leo núi tại chỗ", "Mountain Climbers", ExerciseType.Time, "beginner", "body only", new[] { "abdominals" }, new[] { "quadriceps", "shoulders" }, "Bắt đầu ở tư thế hít đất cao.", "Luân phiên kéo gối về ngực, giữ hông ổn định."),
            Seed("00000000-0000-4000-8000-000000000006", "Single_Leg_Glute_Bridge", "Nâng hông một chân", "Single Leg Glute Bridge", ExerciseType.Reps, "beginner", "body only", new[] { "glutes" }, new[] { "hamstrings" }, "Nằm ngửa, co một gối và duỗi chân còn lại.", "Siết mông nâng hông, giữ khung chậu cân bằng rồi hạ chậm."),
            Seed("00000000-0000-4000-8000-000000000007", "Crunches", "Gập bụng", "Crunches", ExerciseType.Reps, "beginner", "body only", new[] { "abdominals" }, Array.Empty<string>(), "Nằm ngửa, co gối và giữ lưng dưới trên sàn.", "Dùng cơ bụng nâng vai, tránh kéo cổ."),
            Seed("00000000-0000-4000-8000-000000000008", "Rope_Jumping", "Nhảy dây", "Rope Jumping", ExerciseType.Time, "intermediate", "other", new[] { "quadriceps" }, new[] { "calves", "hamstrings" }, "Đứng thẳng, giữ tay cầm dây hai bên hông.", "Xoay dây bằng cổ tay và bật nhảy nhẹ bằng mũi chân."),
            Seed("00000000-0000-4000-8000-000000000009", "Dumbbell_Bench_Press", "Đẩy ngực với tạ đơn", "Dumbbell Bench Press", ExerciseType.Reps, "intermediate", "dumbbell", new[] { "chest" }, new[] { "triceps", "shoulders" }, "Nằm chắc trên ghế, giữ tạ hai bên ngực.", "Đẩy tạ lên có kiểm soát và không khóa khuỷu tay quá mức."),
            Seed("00000000-0000-4000-8000-000000000010", "Bent_Over_Two-Dumbbell_Row", "Kéo tạ đơn cúi người", "Bent Over Dumbbell Row", ExerciseType.Reps, "intermediate", "dumbbell", new[] { "middle back" }, new[] { "biceps", "lats" }, "Gập hông, giữ lưng trung lập và hai tay cầm tạ.", "Kéo khuỷu tay về sau, ép bả vai rồi hạ tạ chậm."),
            Seed("00000000-0000-4000-8000-000000000011", "Dumbbell_Shoulder_Press", "Đẩy vai với tạ đơn", "Dumbbell Shoulder Press", ExerciseType.Reps, "intermediate", "dumbbell", new[] { "shoulders" }, new[] { "triceps" }, "Giữ tạ ngang vai, cổ tay thẳng.", "Đẩy tạ lên trên đầu và hạ về vị trí ban đầu có kiểm soát."),
            Seed("00000000-0000-4000-8000-000000000012", "Dumbbell_Bicep_Curl", "Cuốn tạ đơn", "Dumbbell Bicep Curl", ExerciseType.Reps, "beginner", "dumbbell", new[] { "biceps" }, new[] { "forearms" }, "Giữ khuỷu tay sát thân người và cổ tay trung lập.", "Cuốn tạ bằng cẳng tay, không dùng đà từ lưng."),
            Seed("00000000-0000-4000-8000-000000000013", "Triceps_Pushdown", "Kéo cáp tay sau", "Triceps Pushdown", ExerciseType.Reps, "intermediate", "cable", new[] { "triceps" }, Array.Empty<string>(), "Giữ khuỷu tay sát thân và thân người ổn định.", "Duỗi khuỷu tay kéo tay cầm xuống rồi trở lại chậm."),
            Seed("00000000-0000-4000-8000-000000000014", "Wide-Grip_Lat_Pulldown", "Kéo xô tay rộng", "Wide-Grip Lat Pulldown", ExerciseType.Reps, "beginner", "cable", new[] { "lats" }, new[] { "biceps", "middle back", "shoulders" }, "Ngồi chắc, nắm thanh rộng hơn vai.", "Kéo thanh về phần trên ngực bằng cơ lưng, không giật người."),
            Seed("00000000-0000-4000-8000-000000000015", "Barbell_Deadlift", "Deadlift với thanh đòn", "Barbell Deadlift", ExerciseType.Reps, "advanced", "barbell", new[] { "hamstrings" }, new[] { "glutes", "lower back", "quadriceps" }, "Đứng sát thanh đòn, gập hông và giữ cột sống trung lập.", "Đạp sàn để đứng lên, giữ thanh gần thân người."),
            Seed("00000000-0000-4000-8000-000000000016", "Barbell_Squat", "Squat với thanh đòn", "Barbell Squat", ExerciseType.Reps, "advanced", "barbell", new[] { "quadriceps" }, new[] { "glutes", "hamstrings" }, "Đặt thanh chắc trên lưng trên và siết thân giữa.", "Hạ người có kiểm soát, đầu gối cùng hướng mũi chân."),
            Seed("00000000-0000-4000-8000-000000000017", "Push_Up_to_Side_Plank", "Hít đất chuyển plank nghiêng", "Push Up to Side Plank", ExerciseType.Reps, "beginner", "body only", new[] { "chest" }, new[] { "abdominals", "shoulders", "triceps" }, "Thực hiện một lần hít đất với thân người ổn định.", "Xoay người mở một tay lên trần, trở về giữa rồi đổi bên."),
            Seed("00000000-0000-4000-8000-000000000018", "Side_Bridge", "Plank nghiêng", "Side Bridge", ExerciseType.Time, "beginner", "body only", new[] { "abdominals" }, new[] { "shoulders" }, "Chống một khuỷu tay dưới vai và xếp hai chân.", "Nâng hông, giữ thân người thành đường thẳng và thở đều.")
            ,Seed("00000000-0000-4000-8000-000000000019", "Incline_Push-Up", "Hít đất dốc lên", "Incline Push-Up", ExerciseType.Reps, "beginner", "body only", new[] { "chest" }, new[] { "shoulders", "triceps" }, "Chống tay lên bề mặt chắc chắn cao hơn sàn.", "Giữ thân thẳng, hạ ngực rồi đẩy người lên có kiểm soát.")
            ,Seed("00000000-0000-4000-8000-000000000020", "Chin-Up", "Hít xà tay ngửa", "Chin-Up", ExerciseType.Reps, "beginner", "body only", new[] { "lats" }, new[] { "biceps", "forearms", "middle back" }, "Nắm xà với lòng bàn tay hướng về người.", "Kéo ngực về phía xà, tránh đung đưa thân người.")
            ,Seed("00000000-0000-4000-8000-000000000021", "Russian_Twist", "Xoay bụng kiểu Nga", "Russian Twist", ExerciseType.Reps, "intermediate", "body only", new[] { "abdominals" }, new[] { "lower back" }, "Ngồi hơi ngả thân, giữ cột sống trung lập.", "Xoay thân sang hai bên bằng cơ bụng, không giật vai.")
            ,Seed("00000000-0000-4000-8000-000000000022", "Flat_Bench_Lying_Leg_Raise", "Nâng chân nằm ghế", "Flat Bench Lying Leg Raise", ExerciseType.Reps, "beginner", "body only", new[] { "abdominals" }, Array.Empty<string>(), "Nằm chắc trên ghế, hai chân duỗi thẳng.", "Nâng chân bằng cơ bụng rồi hạ chậm, không cong lưng dưới.")
            ,Seed("00000000-0000-4000-8000-000000000023", "Barbell_Hip_Thrust", "Hip thrust với thanh đòn", "Barbell Hip Thrust", ExerciseType.Reps, "intermediate", "barbell", new[] { "glutes" }, new[] { "calves", "hamstrings" }, "Tựa lưng trên vào ghế và đặt thanh đòn chắc trên hông.", "Đẩy hông lên bằng cơ mông rồi hạ xuống có kiểm soát.")
            ,Seed("00000000-0000-4000-8000-000000000024", "Barbell_Seated_Calf_Raise", "Nhón bắp chân ngồi với thanh đòn", "Barbell Seated Calf Raise", ExerciseType.Reps, "beginner", "barbell", new[] { "calves" }, Array.Empty<string>(), "Ngồi chắc, đặt thanh đòn có đệm trên đùi.", "Nhón gót hết biên độ rồi hạ chậm.")
            ,Seed("00000000-0000-4000-8000-000000000025", "Lateral_Raise_-_With_Bands", "Dang vai với dây kháng lực", "Band Lateral Raise", ExerciseType.Reps, "beginner", "bands", new[] { "shoulders" }, Array.Empty<string>(), "Đứng giữa dây, giữ tay cầm hai bên.", "Dang tay đến ngang vai, giữ khuỷu hơi cong rồi hạ chậm.")
            ,Seed("00000000-0000-4000-8000-000000000026", "Scapular_Pull-Up", "Kéo xà bằng bả vai", "Scapular Pull-Up", ExerciseType.Reps, "beginner", null, new[] { "traps" }, new[] { "lats", "middle back" }, "Treo người trên xà với tay duỗi.", "Hạ và kéo bả vai để nâng thân nhẹ mà không gập khuỷu tay.")
            ,Seed("00000000-0000-4000-8000-000000000027", "Cable_Russian_Twists", "Xoay bụng với cáp", "Cable Russian Twists", ExerciseType.Reps, "beginner", "cable", new[] { "abdominals" }, Array.Empty<string>(), "Đứng ngang máy cáp và giữ tay cầm bằng hai tay.", "Xoay thân khỏi máy bằng cơ bụng, giữ hông ổn định.")
            ,Seed("00000000-0000-4000-8000-000000000028", "Seated_Calf_Raise", "Nhón bắp chân với máy ngồi", "Seated Calf Raise", ExerciseType.Reps, "beginner", "machine", new[] { "calves" }, Array.Empty<string>(), "Điều chỉnh đệm máy nằm chắc trên đùi.", "Nhón gót hết biên độ, dừng ngắn rồi hạ có kiểm soát.")
            ,Seed("00000000-0000-4000-8000-000000000029", "Band_Assisted_Pull-Up", "Hít xà có dây hỗ trợ", "Band Assisted Pull-Up", ExerciseType.Reps, "beginner", "other", new[] { "lats" }, new[] { "abdominals", "forearms", "middle back" }, "Cố định dây hỗ trợ vào xà và đặt chân hoặc gối vào dây.", "Kéo ngực lên xà, giữ thân ổn định rồi hạ chậm.")
            ,Seed("00000000-0000-4000-8000-000000000030", "Hanging_Leg_Raise", "Nâng chân treo xà", "Hanging Leg Raise", ExerciseType.Reps, "advanced", "body only", new[] { "abdominals" }, Array.Empty<string>(), "Treo người chắc trên xà, siết vai và thân giữa.", "Nâng chân bằng cơ bụng, tránh đung đưa và hạ chậm.")
        );
    }

    private static object Seed(
        string id, string slug, string nameVi, string nameEn, ExerciseType type,
        string difficulty, string? equipment, string[] primary, string[] secondary,
        params string[] instructions)
    {
        var root = $"storage://exercise-catalog/{slug}";
        return new
        {
            Id = Guid.Parse(id),
            Slug = slug,
            NameVi = nameVi,
            NameEn = nameEn,
            Type = type,
            Difficulty = difficulty,
            Equipment = equipment,
            PrimaryMuscles = primary.ToList(),
            SecondaryMuscles = secondary.ToList(),
            InstructionsVi = instructions.ToList(),
            ImageStartUrl = $"{root}/0.jpg",
            ImageEndUrl = $"{root}/1.jpg",
            Source,
            SourceUrl,
            SourceLicense = License,
            IsActive = true,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt,
            DeletedAt = (DateTime?)null
        };
    }
}
