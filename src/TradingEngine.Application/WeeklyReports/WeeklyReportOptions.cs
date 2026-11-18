using System.Net.Mail;
using System.Text.RegularExpressions;
using NodaTime;
using NodaTime.Text;

namespace TradingEngine.Application.WeeklyReports;

public sealed class WeeklyReportOptions
{
    public const string SectionName = "WeeklyReports";

    public bool Enabled { get; set; }
    public string DeploymentId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = "operator";
    public string TimeZone { get; set; } = "Europe/London";
    public int IsoDayOfWeek { get; set; } = 7;
    public string LocalTime { get; set; } = "18:00";
    public string BaseUrl { get; set; } = string.Empty;
    public string[] RecipientAddresses { get; set; } = [];
    public int PollIntervalSeconds { get; set; } = 30;
    public int GenerationLeaseSeconds { get; set; } = 600;
    public int ClaimLeaseSeconds { get; set; } = 120;
    public int MaxDispatchAttempts { get; set; } = 5;
    public int MaxReportsPerRun { get; set; } = 10;
    public int MaxSnapshotRows { get; set; } = 10000;
    public int MaxEmailRows { get; set; } = 100;
    public int MaxEmailBytes { get; set; } = 128000;

    public string SubscriptionKey => $"weekly:{DeploymentId}:{SubscriptionId}";

    public void Validate()
    {
        if (DateTimeZoneProviders.Tzdb.GetZoneOrNull(TimeZone) is null || IsoDayOfWeek is < 1 or > 7
            || LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(LocalTime).Success is false
            || PollIntervalSeconds is < 1 or > 3600 || GenerationLeaseSeconds is < 30 or > 3600
            || ClaimLeaseSeconds is < 30 or > 3600 || MaxDispatchAttempts is < 1 or > 20
            || MaxReportsPerRun is < 1 or > 100 || MaxSnapshotRows is < 1 or > 100000
            || MaxEmailRows is < 1 or > 1000 || MaxEmailBytes is < 4096 or > 1000000)
        {
            throw new InvalidOperationException("WeeklyReports has invalid schedule or bounded-work settings.");
        }

        if (ValidIdentity(SubscriptionId) is false || (DeploymentId.Length > 0 && ValidIdentity(DeploymentId) is false)
            || (Enabled && DeploymentId.Length == 0))
        {
            throw new InvalidOperationException("WeeklyReports requires stable deployment/subscription identities of 1–64 letters, digits, underscores or hyphens.");
        }

        if ((Enabled || BaseUrl.Length > 0) && (Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? url) is false
            || url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length > 0 || url.Query.Length > 0
            || url.Fragment.Length > 0 || url.AbsolutePath != "/" || BaseUrl.Length > 2048))
        {
            throw new InvalidOperationException("WeeklyReports:BaseUrl must be the HTTPS application origin.");
        }

        if ((Enabled && RecipientAddresses.Length == 0) || RecipientAddresses.Length > 50
            || RecipientAddresses.Any(address => MailAddress.TryCreate(address, out MailAddress? parsed) is false
                || parsed.Address != address || address.Length > 254)
            || RecipientAddresses.Distinct(StringComparer.OrdinalIgnoreCase).Count() != RecipientAddresses.Length)
        {
            throw new InvalidOperationException("WeeklyReports requires a bounded, unique recipient set when enabled.");
        }
    }

    private static bool ValidIdentity(string value)
    {
        return Regex.IsMatch(value, "^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);
    }
}
