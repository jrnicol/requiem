namespace Requiem.Shared.Characters;

/// <summary>
/// The factions a character can belong to (VtR Factions chapter), stored by these names. A ghoul serves one too,
/// and an ordinary mortal has no known affiliation.
/// </summary>
public static class Factions
{
    public const string Anarch = "Anarch";
    public const string Camarilla = "Camarilla";
    public const string CarthianMovement = "Carthian Movement";
    public const string Sabbat = "Sabbat";
    public const string NoKnownAffiliation = "No Known Affiliation";

    /// <summary>In the order the editors list them.</summary>
    public static readonly IReadOnlyList<string> All = [Anarch, Camarilla, CarthianMovement, Sabbat, NoKnownAffiliation];
}
