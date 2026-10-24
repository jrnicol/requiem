using System.ComponentModel.DataAnnotations;

namespace Requiem.Shared.Accounts;

public static class Roles
{
    public const string GameMaster = "GameMaster";
}

/// <summary>Authorization policy names, registered on both the server and the browser client.</summary>
public static class Policies
{
    /// <summary>Site GMs: the personal NPC roster, scenes, table play and catalog edits.</summary>
    public const string GameMaster = "GameMaster";

    /// <summary>Anyone signed in, players included: e.g. reading the powers catalog.</summary>
    public const string Member = "Member";
}

public static class PasswordRules
{
    /// <summary>Length over complexity rules (NIST SP 800-63B style).</summary>
    public const int MinLength = 12;
    public const int MaxLength = 128;
}

/// <summary>Who is signed in. Anonymous when nobody is.</summary>
public sealed record CurrentUser(bool IsAuthenticated, string? Email, string? DisplayName, IReadOnlyList<string> Roles)
{
    public static CurrentUser Anonymous { get; } = new(false, null, null, []);
}

/// <param name="NeedsSetup">True until the first GM account exists.</param>
/// <param name="RequiresSetupCode">True when the server needs the one-time setup code from its configuration.</param>
public sealed record SetupStatus(bool NeedsSetup, bool RequiresSetupCode);

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    public string Password { get; set; } = "";

    /// <summary>Keep the session across browser restarts (sensible on a personal tablet).</summary>
    public bool RememberMe { get; set; } = true;
}

/// <summary>Creates the first GM account on a fresh install.</summary>
public sealed class SetupRequest
{
    [Required(ErrorMessage = "What should the app call you?"), StringLength(100)]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "Enter your email."), EmailAddress(ErrorMessage = "That doesn't look like an email address."), StringLength(256)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(PasswordRules.MaxLength, MinimumLength = PasswordRules.MinLength,
        ErrorMessage = "Use at least 12 characters. A passphrase of a few words works well.")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Type the password again."), Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>Only needed outside Development, when the server has an <c>Auth:SetupCode</c> configured.</summary>
    public string? SetupCode { get; set; }
}
