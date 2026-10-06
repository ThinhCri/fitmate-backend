using Amazon.S3;
using Amazon.S3.Model;

namespace Fitmate.Api.Cli;

public static class ImportExerciseCatalogMediaCommand
{
    private static readonly string[] Slugs =
    {
        "Pushups", "Bodyweight_Squat", "Plank", "Bodyweight_Walking_Lunge", "Mountain_Climbers",
        "Single_Leg_Glute_Bridge", "Crunches", "Rope_Jumping", "Dumbbell_Bench_Press",
        "Bent_Over_Two-Dumbbell_Row", "Dumbbell_Shoulder_Press", "Dumbbell_Bicep_Curl",
        "Triceps_Pushdown", "Wide-Grip_Lat_Pulldown", "Barbell_Deadlift", "Barbell_Squat", "Push_Up_to_Side_Plank",
        "Side_Bridge", "Incline_Push-Up", "Chin-Up", "Russian_Twist",
        "Flat_Bench_Lying_Leg_Raise", "Barbell_Hip_Thrust", "Barbell_Seated_Calf_Raise",
        "Lateral_Raise_-_With_Bands", "Scapular_Pull-Up", "Cable_Russian_Twists",
        "Seated_Calf_Raise", "Band_Assisted_Pull-Up", "Hanging_Leg_Raise"
    };

    public static async Task<int> RunAsync(IConfiguration configuration)
    {
        var accountId = Required(configuration, "R2_ACCOUNT_ID");
        var accessKey = Required(configuration, "R2_ACCESS_KEY_ID");
        var secretKey = Required(configuration, "R2_SECRET_ACCESS_KEY");
        var bucket = Required(configuration, "R2_BUCKET_NAME");

        using var s3 = new AmazonS3Client(accessKey, secretKey, new AmazonS3Config
        {
            ServiceURL = $"https://{accountId}.r2.cloudflarestorage.com",
            ForcePathStyle = true
        });
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FitmateExerciseCatalogImporter/1.0");

        var uploaded = 0;
        foreach (var slug in Slugs)
        {
            for (var index = 0; index < 2; index++)
            {
                var source = $"https://raw.githubusercontent.com/yuhonas/free-exercise-db/main/exercises/{slug}/{index}.jpg";
                var key = $"exercise-catalog/{slug}/{index}.jpg";
                using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > 2_000_000)
                    throw new InvalidOperationException($"Source image is unexpectedly large: {slug}/{index}.jpg");
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 2_000_000)
                    throw new InvalidOperationException($"Source image is unexpectedly large: {slug}/{index}.jpg");
                await using var input = new MemoryStream(bytes, writable: false);
                await s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    InputStream = input,
                    ContentType = "image/jpeg",
                    AutoCloseStream = false,
                    DisablePayloadSigning = true,
                    DisableDefaultChecksumValidation = true,
                    Metadata = { ["source"] = "yuhonas-free-exercise-db", ["license"] = "Unlicense" }
                });
                uploaded++;
                Console.WriteLine($"[exercise-media] Uploaded {uploaded}/{Slugs.Length * 2}: {key}");
            }
        }

        Console.WriteLine($"[exercise-media] Completed: {uploaded} images uploaded to bucket '{bucket}'.");
        return 0;
    }

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{key} must be configured.");
}
