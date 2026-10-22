namespace CardTradeBuddy.Services;

public static class EmailAddress
{
    /// <summary>A plain address like name@example.com (no display name, at most 256 characters).</summary>
    public static bool IsValid(string email) =>
        email.Length <= 256
        && System.Net.Mail.MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && email.Contains('.', StringComparison.Ordinal)
        && email.IndexOf('@') < email.LastIndexOf('.');
}
