using Requiem.Data.Entities;
using Requiem.Data.Stores;

namespace Requiem.Web.Auth;

/// <summary>
/// Records who changed what (PHASE2.md §8). Entries wait in the request's <see cref="AuditBatch"/> and are
/// saved, or not, together with the next store write: the change they describe.
/// </summary>
public sealed class AuditLog(AuditBatch batch, CampaignAccess access, TimeProvider time)
{
    /// <param name="undoData">Worked out when the entry is saved, after new items have their ids.</param>
    public void Record(Guid? campaignId, Guid? characterId, string action, string summary, Func<string?>? undoData = null) =>
        batch.Add(New(campaignId, characterId, access.UserId, action, summary), undoData);

    /// <summary>For changes by someone not signed in yet (joining through an invite link).</summary>
    public void RecordFor(string userId, Guid? campaignId, string action, string summary) =>
        batch.Add(New(campaignId, null, userId, action, summary));

    public void MarkUndone(AuditEntry entry)
    {
        entry.UndoneAt = time.GetUtcNow();
        batch.MarkUndone(entry);
    }

    private AuditEntry New(Guid? campaignId, Guid? characterId, string userId, string action, string summary) => new()
    {
        CampaignId = campaignId,
        CharacterId = characterId,
        UserId = userId,
        Action = action,
        Summary = summary.Length > 500 ? summary[..500] : summary,
        At = time.GetUtcNow(),
    };
}
