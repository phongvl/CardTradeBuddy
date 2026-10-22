namespace CardTradeBuddy.Services;

public static class PasswordRules
{
    /// <summary>Same rule as the LoginServer. It has the final say and answers "weak_password" if this ever differs.</summary>
    public const int MinLength = 10;
}
