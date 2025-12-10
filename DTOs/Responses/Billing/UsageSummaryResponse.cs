namespace Email.Server.DTOs.Responses.Billing;

public class UsageSummaryResponse
{
    public Guid PeriodId { get; set; }
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public long EmailsSent { get; set; }
    public long EmailsIncluded { get; set; }
    public long EmailsRemaining { get; set; }
    public long OverageEmails { get; set; }
    public decimal UsagePercentage { get; set; }
    public int EstimatedOverageCostCents { get; set; }
    public int DaysRemainingInPeriod { get; set; }
    public bool IsCurrentPeriod { get; set; }
}
