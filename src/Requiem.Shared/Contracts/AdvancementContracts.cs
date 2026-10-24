using System.ComponentModel.DataAnnotations;
using Requiem.Shared.Campaigns;

namespace Requiem.Shared.Contracts;

// Advancement for player characters in play (PHASE2.md §6, milestone 2.3): Corruption and Links, Scripts,
// XP awards and spends. Players act and it counts at once; the audit log lets the Storyteller undo.

/// <summary>One vampiric act or Link counting toward unlocking XP.</summary>
public sealed record ProgressEntry(int Id, string Text, DateTimeOffset At);

/// <summary>A line of the character's advancement history, from the audit log.</summary>
/// <param name="CanUndo">The Storyteller can reverse it (and it hasn't been already).</param>
public sealed record AdvancementHistoryEntry(long Id, DateTimeOffset At, string Who, string Summary, bool CanUndo, bool IsUndone);

/// <summary>Everything the Advancement tab shows.</summary>
/// <param name="ProgressName">"Corruption" for vampires, "Links" for mortals and ghouls.</param>
/// <param name="CanAct">The viewer may log progress, mark Scripts and spend: the player or a Storyteller, while it's in play.</param>
public sealed record AdvancementView(
    int Earned,
    int Spent,
    int Available,
    string ProgressName,
    int Progress,
    bool Unlocked,
    IReadOnlyList<ProgressEntry> ProgressEntries,
    IReadOnlyList<ScriptEntry> Scripts,
    IReadOnlyList<AdvancementHistoryEntry> History,
    bool CanAct,
    bool IsStoryteller);

public sealed class CorruptionRequest
{
    [Required(ErrorMessage = "Say what they did."), StringLength(200)]
    public string Act { get; set; } = "";
}

/// <summary>A scene with other player characters (WoD Core p.75).</summary>
public sealed class LinkRequest
{
    public LinkKind Kind { get; set; }
    /// <summary>The other player characters in the scene.</summary>
    public List<Guid> WithCharacterIds { get; set; } = [];
    [StringLength(500)] public string? Note { get; set; }
}

public sealed record ScriptMarkRequest(bool Marked);

/// <summary>The Storyteller awards XP to one character or several (everyone present, say).</summary>
public sealed class XpAwardRequest
{
    [Range(1, 50, ErrorMessage = "Award 1 to 50 XP.")] public int Amount { get; set; } = 1;
    [Required(ErrorMessage = "Say what it's for."), StringLength(200)] public string Reason { get; set; } = "";
    public List<Guid> CharacterIds { get; set; } = [];
}

/// <summary>Why XP is awarded each session (WoD Core p.75): the reasons the Storyteller picks from.</summary>
public static class XpAwardReasons
{
    public static readonly IReadOnlyList<string> Common =
    [
        "Suffered a Flaw",
        "Went along with a Social Compel",
        "Botched roll",
        "Completed a Milestone",
    ];
}
