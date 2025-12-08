namespace Email.Server.Configuration;

public class BillingSettings
{
    public const string SectionName = "Billing";

    public int GracePeriodDays { get; set; } = 7;
    public int UsageReportingIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Number of free emails allowed per day for users without a subscription
    /// </summary>
    public int FreeTierDailyLimit { get; set; } = 10;
}
