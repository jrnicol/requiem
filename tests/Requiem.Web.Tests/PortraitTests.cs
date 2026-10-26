using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Requiem.Shared.Characters;
using Requiem.Shared.Contracts;

namespace Requiem.Web.Tests;

public class PortraitTests(RequiemWebFactory factory) : IClassFixture<RequiemWebFactory>
{
    // The smallest valid JPEG and PNG headers are enough: the server checks the type from the first bytes.
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    [Fact]
    public async Task Storytellers_upload_replace_and_remove_an_npc_portrait()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var npc = await CreateNpcAsync(table.Storyteller, table.CampaignId);

        var first = await UploadAsync(table.Storyteller, npc.Id!.Value, Jpeg, "image/jpeg");
        Assert.StartsWith("/media/portraits/", first.PortraitUrl);
        Assert.EndsWith(".jpg", first.PortraitUrl);
        Assert.Equal(Jpeg, await table.Storyteller.GetByteArrayAsync(first.PortraitUrl));

        // The sheet shows it, and the version the upload returned lets the open editor save without a conflict.
        var sheet = (await table.Storyteller.GetFromJsonAsync<SupportingCastForm>($"/api/characters/{npc.Id}/supporting-cast", RequiemJson.Options))!;
        Assert.Equal(first.PortraitUrl, sheet.PortraitUrl);
        Assert.Equal(first.Version, sheet.Version);

        // Replacing stores a new file and removes the old one.
        var second = await UploadAsync(table.Storyteller, npc.Id!.Value, Png, "image/png");
        Assert.NotEqual(first.PortraitUrl, second.PortraitUrl);
        Assert.False(File.Exists(LocalPath(first.PortraitUrl!)));
        Assert.True(File.Exists(LocalPath(second.PortraitUrl!)));

        var removed = await table.Storyteller.DeleteAsync($"/api/characters/{npc.Id}/portrait");
        Assert.Null((await removed.Content.ReadFromJsonAsync<PortraitResult>(RequiemJson.Options))!.PortraitUrl);
        Assert.False(File.Exists(LocalPath(second.PortraitUrl!)));
    }

    [Fact]
    public async Task Only_pictures_are_taken_and_players_cant_touch_npcs()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var npc = await CreateNpcAsync(table.Storyteller, table.CampaignId);

        var text = await PutAsync(table.Storyteller, npc.Id!.Value, "not a picture"u8.ToArray(), "image/jpeg");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, text.StatusCode);
        var huge = await PutAsync(table.Storyteller, npc.Id!.Value, [.. Jpeg, .. new byte[1024 * 1024]], "image/jpeg");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(table.Player, npc.Id!.Value, Jpeg, "image/jpeg")).StatusCode);
    }

    [Fact]
    public async Task Players_set_their_own_characters_portrait_and_deleting_the_npc_removes_its_file()
    {
        var table = await PlayerTable.CreateAsync(factory);
        var pc = await PlayerTable.CreateCharacterAsync(table.Player, table.CampaignId, PlayerTable.CompleteMortal());
        var mine = await UploadAsync(table.Player, pc.Sheet.Id!.Value, Jpeg, "image/jpeg");
        Assert.NotNull(mine.PortraitUrl);

        var npc = await CreateNpcAsync(table.Storyteller, table.CampaignId);
        var portrait = await UploadAsync(table.Storyteller, npc.Id!.Value, Jpeg, "image/jpeg");
        Assert.Equal(HttpStatusCode.NoContent, (await table.Storyteller.DeleteAsync($"/api/characters/{npc.Id}")).StatusCode);
        Assert.False(File.Exists(LocalPath(portrait.PortraitUrl!)));
    }

    private string LocalPath(string url) =>
        Path.Combine(factory.MediaFolder, url["/media/".Length..].Replace('/', Path.DirectorySeparatorChar));

    private static async Task<SupportingCastForm> CreateNpcAsync(HttpClient storyteller, Guid campaignId)
    {
        var response = await storyteller.PostAsJsonAsync("/api/characters/supporting-cast",
            new SupportingCastForm { Name = "Portrait Sitter", CampaignId = campaignId, Kind = CharacterKind.Mortal }, RequiemJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SupportingCastForm>(RequiemJson.Options))!;
    }

    private static async Task<PortraitResult> UploadAsync(HttpClient client, Guid id, byte[] image, string type)
    {
        var response = await PutAsync(client, id, image, type);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PortraitResult>(RequiemJson.Options))!;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, byte[] image, string type)
    {
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return client.PutAsync($"/api/characters/{id}/portrait", content);
    }
}
