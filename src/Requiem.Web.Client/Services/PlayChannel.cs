using Requiem.Shared.Contracts;
using Requiem.Shared.Rules;

namespace Requiem.Web.Client.Services;

/// <summary>
/// Table actions for one character. Each shows its predicted result at once (<see cref="PlayPrediction"/>) and goes to
/// the server in the order it was made, one at a time, since the server keeps the last save. When nothing is left in
/// flight the screen settles on the server's answer, which undoes a prediction the server refused.
/// </summary>
/// <param name="current">The character as shown now.</param>
/// <param name="show">Shows a new state of the character.</param>
/// <param name="warn">Says why an action didn't go through.</param>
public sealed class PlayChannel(RequiemApi api, Guid id, Func<PlayCharacter> current, Func<PlayCharacter, Task> show, Action<string> warn)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _inFlight;
    private PlayCharacter? _confirmed;
    private bool _refused;

    /// <summary>An action only the server can apply (adding a wound, compel or condition) is on its way.</summary>
    public bool Waiting { get; private set; }

    /// <returns>True when the action was applied (or, predicted, is on its way); false when it was refused.</returns>
    public async Task<bool> SendAsync(PlayAction action)
    {
        var (next, error) = PlayPrediction.Apply(current(), action);
        if (error is not null)
        {
            warn(error);
            return false;
        }
        if (next is not null)
        {
            await show(next);
            _ = SaveAsync(action);
            return true;
        }

        Waiting = true;
        try
        {
            return await SaveAsync(action);
        }
        finally
        {
            Waiting = false;
        }
    }

    private async Task<bool> SaveAsync(PlayAction action)
    {
        _inFlight++;
        var saved = false;
        await _gate.WaitAsync();
        try
        {
            var result = await api.PlayAsync(id, action);
            if (result.Saved is { } character)
            {
                _confirmed = character;
                saved = true;
            }
            else
            {
                warn(result.Message ?? "That didn't save.");
                _refused = true;
            }
        }
        catch (HttpRequestException)
        {
            warn("Couldn't reach the server; that didn't save.");
            _refused = true;
        }
        finally
        {
            _gate.Release();
            _inFlight--;
        }

        if (_inFlight == 0) await SettleAsync();
        return saved;
    }

    /// <summary>Shows the server's latest answer; after a refusal with none to show, reads it afresh.</summary>
    private async Task SettleAsync()
    {
        var settled = _confirmed;
        if (_refused && settled is null)
        {
            try
            {
                settled = await api.GetPlayAsync(id);
            }
            catch (HttpRequestException)
            {
                // Still offline: keep what's shown, and the next action tries again.
            }
        }
        _confirmed = null;
        _refused = false;
        if (settled is not null && _inFlight == 0) await show(settled);
    }
}
