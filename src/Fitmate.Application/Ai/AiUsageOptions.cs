namespace Fitmate.Application.Ai;

public sealed class AiUsageOptions
{
    public const string SectionName = "AiUsage";

    // Keep off until Play purchase verification and client quota UX are released together.
    public bool Enabled { get; set; }
    public int FreeChatPerDay { get; set; } = 10;
    public int FreeScansPerDay { get; set; } = 2;
    public int PlusChatPerDay { get; set; } = 100;
    public int PlusScansPerDay { get; set; } = 20;
}
