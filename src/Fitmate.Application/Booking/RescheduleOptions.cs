using System;

namespace Fitmate.Application.Booking;

public sealed class RescheduleOptions
{
    public const string SectionName = "Reschedule";

    public TimeSpan MinLeadTimeBeforeOldSlot { get; set; } = TimeSpan.FromHours(2);
    public TimeSpan MinLeadTimeForNewSlot { get; set; } = TimeSpan.FromHours(2);
    public int MaxAdvanceDays { get; set; } = 30;
    public TimeSpan DefaultRequestTimeout { get; set; } = TimeSpan.FromHours(24);
}
