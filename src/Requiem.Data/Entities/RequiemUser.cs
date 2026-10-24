using Microsoft.AspNetCore.Identity;

namespace Requiem.Data.Entities;

/// <summary>A person who signs in: the GM now, players once campaigns arrive (Phase 2).</summary>
public class RequiemUser : IdentityUser
{
    public required string DisplayName { get; set; }
}
