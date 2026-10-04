namespace Fitmate.Application.Ai;

public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "qwen/qwen3.8-27b:free";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxOutputTokens { get; set; } = 500;
    public int MaxHistoryMessages { get; set; } = 10;
    public string? AppUrl { get; set; }
    public string AppName { get; set; } = "Fitmate";
}
