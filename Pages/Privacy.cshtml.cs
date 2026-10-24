using CardTradeBuddy.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CardTradeBuddy.Pages;

/// <summary>
/// The privacy policy: what personal data the site handles, why, for how long, who gets it, and the user's rights. Open to everybody, also before
/// the cookie dialog has been accepted. The text is in <c>Pages/Shared/_PrivacyEn.cshtml</c> and <c>_PrivacyDa.cshtml</c>: change both, and
/// <see cref="Updated"/>, when what the site does with data changes. Who the operator is and how to reach them come from the settings
/// <c>Site:OperatorName</c> and <c>Site:ContactEmail</c>.
/// </summary>
public class PrivacyModel(IConfiguration configuration) : PageModel
{
    public const string Updated = "2026-10-24";

    /// <summary>Who runs the site (the data controller); empty when not set.</summary>
    public string OperatorName => configuration["Site:OperatorName"] ?? "";

    /// <summary>Where to send questions and requests about personal data; empty when not set.</summary>
    public string ContactEmail => configuration["Site:ContactEmail"] ?? "";

    /// <summary>The youngest age that may have a user (the same setting as on the Create user page).</summary>
    public int MinimumAge => configuration.GetValue("Account:MinimumAge", BirthDate.DefaultMinimumAge);

    public void OnGet()
    {
    }
}
