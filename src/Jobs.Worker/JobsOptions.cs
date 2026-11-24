namespace ConfReg.Jobs;

/// <summary>Schedules, as Quartz cron expressions (second minute hour day-of-month month day-of-week), evaluated in UTC.</summary>
public class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>Every minute. The hold is 30 minutes, so an abandoned registration holds seats for at most about 31.</summary>
    public string ExpireCron { get; set; } = "0 * * * * ?";

    /// <summary>Every 5 minutes.</summary>
    public string ConfirmationEmailCron { get; set; } = "0 0/5 * * * ?";

    /// <summary>02:00 UTC every night.</summary>
    public string CapacityReportCron { get; set; } = "0 0 2 * * ?";
}
