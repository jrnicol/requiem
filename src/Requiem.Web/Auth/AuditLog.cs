using Requiem.Data;
using Requiem.Data.Entities;

namespace Requiem.Web.Auth;

/// <summary>
/// Records who changed what (PHASE2.md §8). Entries are added to the request's DbContext, so they are
/// saved, or not, together with the change they describe.
/// </summary>
public sealed class AuditLog(RequiemDbContext db, CampaignAccess access, TimeProvider time)
{
    public void Record(Guid? campaignId, Guid? characterId, string action, string summary, string? undoData = null) =>
        db.AuditEntries.Add(new AuditEntry
        {
            CampaignId = campaignId,
            CharacterId = characterId,
            UserId = access.UserId,
            Action = action,
            Summary = summary.Length > 500 ? summary[..500] : summary,
            UndoData = undoData,
            At = time.GetUtcNow(),
        });
}
