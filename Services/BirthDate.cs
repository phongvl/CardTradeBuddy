using System.Globalization;

namespace CardTradeBuddy.Services;

public enum BirthDateProblem { None, Missing, Invalid, TooYoung }

/// <summary>Checks the birthday from the sign-up form (an <c>&lt;input type="date"&gt;</c> posts it as yyyy-MM-dd).</summary>
public static class BirthDate
{
    /// <summary>Youngest age that may create a user when <c>Account:MinimumAge</c> is not set (13 is the usual limit for a child's own account in Denmark).</summary>
    public const int DefaultMinimumAge = 13;

    private const int OldestPlausibleAge = 120;

    public static BirthDateProblem Check(string? text, DateOnly today, int minimumAge, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return BirthDateProblem.Missing;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return BirthDateProblem.Invalid;
        if (date > today || date < today.AddYears(-OldestPlausibleAge)) return BirthDateProblem.Invalid;
        return date > today.AddYears(-minimumAge) ? BirthDateProblem.TooYoung : BirthDateProblem.None;
    }
}
