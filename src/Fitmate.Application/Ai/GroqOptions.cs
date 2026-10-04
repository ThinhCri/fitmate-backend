namespace Fitmate.Application.Ai;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "qwen/qwen3.8-27b";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 500;
    public int MaxHistoryMessages { get; set; } = 10;
}
