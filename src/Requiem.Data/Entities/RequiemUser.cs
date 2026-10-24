using Microsoft.AspNetCore.Identity;

namespace Requiem.Data.Entities;

/// <summary>A person who signs in. Every account is alike: Storyteller or player is a role per campaign.</summary>
public class RequiemUser : IdentityUser
{
    public required string DisplayName { get; set; }
}
