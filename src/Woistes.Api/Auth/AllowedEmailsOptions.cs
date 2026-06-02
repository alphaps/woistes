namespace Woistes.Api;

public class AllowedEmailsOptions
{
    public List<string> Emails { get; set; } = new();
    public string? EmailsCsv { get; set; }

    public IEnumerable<string> GetAllEmails() =>
        Emails.Concat(
            (EmailsCsv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
