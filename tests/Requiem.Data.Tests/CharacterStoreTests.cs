using Microsoft.Extensions.DependencyInjection;
using Requiem.Data.Dynamo;
using Requiem.Data.Entities;
using Requiem.Data.Stores;
using Requiem.Shared.Characters;
using Requiem.Shared.Rules;

namespace Requiem.Data.Tests;

public class CharacterStoreTests : IDisposable
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly DynamoTestTable _table = new();
    private readonly Guid _campaign = Guid.NewGuid();

    public CharacterStoreTests()
    {
        using var scope = _table.Scope();
        DynamoCatalog.SeedAsync(scope.ServiceProvider.GetRequiredService<DynamoWriter>(), None).GetAwaiter().GetResult();
    }

    public void Dispose() => _table.Dispose();

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _table.Scope();
        return await work(scope.ServiceProvider);
    }

    private Task<Character?> LoadAsync(Guid id) => InScopeAsync(s => s.GetRequiredService<ICharacterStore>().GetAsync(id, None));

    private async Task<Character> AddAsync(Character character)
    {
        character.CampaignId ??= _campaign;
        await InScopeAsync(async s => { await s.GetRequiredService<ICharacterStore>().AddAsync(character, None); return 0; });
        return character;
    }

    [Fact]
    public async Task Full_sheet_round_trips_with_its_parts_and_catalog_entries()
    {
        var catalog = await InScopeAsync(s => s.GetRequiredService<ICatalogStore>().GetAsync(None));
        var auspex = catalog.PowerGroups.Single(g => g.Name == "Auspex");
        var ventrue = catalog.Bloodlines.Single(b => b.Name == "Ventrue");
        var added = await AddAsync(new Character
        {
            Name = "Marguerite Vance",
            Tier = CharacterTier.FullSheet,
            Kind = CharacterKind.Vampire,
            BloodlineId = ventrue.Id,
            BloodPotency = 2,
            Humanity = 6,
            Attributes = [new() { Attribute = CharacterAttribute.Wits, Dots = 3 }],
            Skills = [new() { Skill = Skill.Empathy, Dots = 2 }],
            Specialties = [new() { Skill = Skill.Empathy, Name = "Lies" }],
            Disciplines = [new() { PowerGroupId = auspex.Id, Dots = 2 }],
            Wounds = [new() { Description = "knife", Amount = 3, Type = DamageType.Lethal }],
            Scripts = [new() { Slot = 1, Kind = ScriptKind.Humanity, Text = "Protect my sister" }],
        });

        var loaded = (await LoadAsync(added.Id))!;
        Assert.Equal(CharacterAttribute.Wits, Assert.Single(loaded.Attributes).Attribute);
        Assert.Equal("Lies", Assert.Single(loaded.Specialties).Name);
        Assert.Equal(DamageType.Lethal, Assert.Single(loaded.Wounds).Type);
        Assert.Equal("Protect my sister", Assert.Single(loaded.Scripts).Text);
        // Catalog entries are filled in on load, as EF's navigations were.
        Assert.Equal("Auspex", Assert.Single(loaded.Disciplines).PowerGroup.Name);
        Assert.Equal("Ventrue", loaded.Bloodline!.Name);
    }

    [Fact]
    public async Task Saving_stamps_id_timestamps_and_version()
    {
        var character = await AddAsync(new Character { Name = "Mook", Tier = CharacterTier.SupportingCast, Physical = 2, Mental = 1, Social = 1 });
        Assert.NotEqual(Guid.Empty, character.Id);
        Assert.Equal(_table.Time.GetUtcNow(), character.CreatedAt);
        var firstVersion = character.Version;
        Assert.NotEqual(Guid.Empty, firstVersion);

        _table.Time.Advance(TimeSpan.FromMinutes(5));
        var loaded = (await LoadAsync(character.Id))!;
        loaded.Notes = "Bouncer at Elysium";
        Assert.True(await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().TrySaveAsync(loaded, null, None)));

        var reloaded = (await LoadAsync(character.Id))!;
        Assert.Equal("Bouncer at Elysium", reloaded.Notes);
        Assert.Equal(_table.Time.GetUtcNow(), reloaded.UpdatedAt);
        Assert.NotEqual(reloaded.CreatedAt, reloaded.UpdatedAt);
        Assert.NotEqual(firstVersion, reloaded.Version);
    }

    [Fact]
    public async Task Concurrent_edits_are_detected_and_the_loser_writes_nothing()
    {
        var id = (await AddAsync(new Character { Name = "Contested" })).Id;
        var a = (await LoadAsync(id))!;
        var b = (await LoadAsync(id))!;

        a.Notes = "GM tablet";
        Assert.True(await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().TrySaveAsync(a, null, None)));
        b.Notes = "GM phone";
        var bVersion = b.Version;
        Assert.False(await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().TrySaveAsync(b, null, None)));

        Assert.Equal("GM tablet", (await LoadAsync(id))!.Notes);
        Assert.Equal(bVersion, b.Version); // left as loaded, so the caller can report the conflict
    }

    [Fact]
    public async Task New_list_items_get_the_next_free_id_in_their_list()
    {
        var character = await AddAsync(new Character
        {
            Name = "Battered",
            Wounds = [new() { Description = "fist", Amount = 1 }, new() { Description = "bat", Amount = 2 }],
            XpLedger = [new() { Amount = 4, Reason = "Scripts" }],
        });
        Assert.Equal([1, 2], character.Wounds.Select(w => w.Id));
        Assert.Equal(1, Assert.Single(character.XpLedger).Id);

        var loaded = (await LoadAsync(character.Id))!;
        loaded.Wounds.RemoveAll(w => w.Id == 1);
        loaded.Wounds.Add(new CharacterWound { Description = "knife", Amount = 3 });
        await InScopeAsync(async s => { await s.GetRequiredService<ICharacterStore>().SaveAsync(loaded, None); return 0; });
        Assert.Equal([2, 3], (await LoadAsync(character.Id))!.Wounds.Select(w => w.Id));
    }

    [Fact]
    public async Task Audit_entries_save_with_the_change_and_see_new_ids()
    {
        var id = (await AddAsync(new Character { Name = "Spender" })).Id;
        await using (var scope = _table.Scope())
        {
            var character = (await scope.ServiceProvider.GetRequiredService<ICharacterStore>().GetAsync(id, None))!;
            var entry = new XpEntry { Amount = 3, Reason = "Story" };
            character.XpLedger.Add(entry);
            scope.ServiceProvider.GetRequiredService<AuditBatch>().Add(
                new AuditEntry { CharacterId = id, CampaignId = _campaign, UserId = "u1", Action = "xp.award", Summary = "+3 XP", At = _table.Time.GetUtcNow() },
                () => $"xp:{entry.Id}");
            await scope.ServiceProvider.GetRequiredService<ICharacterStore>().SaveAsync(character, None);
        }

        var history = await InScopeAsync(s => s.GetRequiredService<IAuditStore>().ForCharacterAsync(id, 10, None));
        var saved = Assert.Single(history);
        Assert.Equal("xp:1", saved.UndoData);
        Assert.Equal(saved.Id, (await InScopeAsync(s => s.GetRequiredService<IAuditStore>().GetAsync(saved.Id, None)))!.Id);
    }

    [Fact]
    public async Task Deleting_a_character_clears_links_to_it_and_keeps_others()
    {
        var sire = await AddAsync(new Character { Name = "Sire" });
        var childe = await AddAsync(new Character { Name = "Childe", SireId = sire.Id });
        var ghoul = await AddAsync(new Character { Name = "Ghoul", Kind = CharacterKind.Ghoul, DomitorId = sire.Id });
        var rival = await AddAsync(new Character
        {
            Name = "Rival",
            Relationships =
            [
                new() { ToCharacterId = sire.Id, Kind = RelationshipKind.Rival },
                new() { ToName = "Detective Ruiz", Kind = RelationshipKind.Contact },
            ],
        });

        Assert.True(await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().DeleteAsync(sire.Id, None)));

        Assert.Null(await LoadAsync(sire.Id));
        Assert.Null((await LoadAsync(childe.Id))!.SireId);
        Assert.Null((await LoadAsync(ghoul.Id))!.DomitorId);
        // The rival's link to someone outside the app survives.
        Assert.Equal("Detective Ruiz", Assert.Single((await LoadAsync(rival.Id))!.Relationships).ToName);
        Assert.Equal(3, await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().CountByCampaignAsync(_campaign, None)));
    }

    [Fact]
    public async Task Deleting_a_missing_character_returns_false() =>
        Assert.False(await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().DeleteAsync(Guid.NewGuid(), None)));

    [Fact]
    public async Task Moving_a_character_cuts_its_links_both_ways()
    {
        var sire = await AddAsync(new Character { Name = "Sire" });
        var childe = await AddAsync(new Character { Name = "Childe", SireId = sire.Id });
        var elsewhere = Guid.NewGuid();

        await InScopeAsync(async s =>
        {
            var store = s.GetRequiredService<ICharacterStore>();
            await store.MoveAsync((await store.GetAsync(childe.Id, None))!, elsewhere, None);
            return 0;
        });

        var moved = (await LoadAsync(childe.Id))!;
        Assert.Equal(elsewhere, moved.CampaignId);
        Assert.Null(moved.SireId);
        Assert.Equal([sire.Id], (await InScopeAsync(s => s.GetRequiredService<ICharacterStore>().ListByCampaignAsync(_campaign, None))).Select(c => c.Id));
    }
}
