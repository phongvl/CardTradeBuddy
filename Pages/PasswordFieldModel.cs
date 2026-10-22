namespace CardTradeBuddy.Pages;

/// <summary>What a password field needs: see Shared/_PasswordField.cshtml.</summary>
/// <param name="Id">The input's id (the label points at it).</param>
/// <param name="Name">The form field name the page model binds to.</param>
/// <param name="Label">The visible label.</param>
/// <param name="Autocomplete">"current-password" when logging in, "new-password" when choosing one.</param>
/// <param name="Placeholder">Optional grey text inside the empty field.</param>
/// <param name="Hint">Optional small text under the field, for example the length rule.</param>
public record PasswordFieldModel(string Id, string Name, string Label, string Autocomplete, string? Placeholder = null, string? Hint = null);