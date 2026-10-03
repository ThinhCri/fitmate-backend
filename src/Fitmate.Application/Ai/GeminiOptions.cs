namespace Fitmate.Application.Ai;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.8-flash";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 1000;
    public int MaxHistoryMessages { get; set; } = 20;
}
