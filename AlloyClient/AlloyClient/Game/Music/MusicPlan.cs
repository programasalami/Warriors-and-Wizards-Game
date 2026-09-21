using System;
using AlloyClient.Data;

namespace AlloyClient.Game.Music;

// What the speakers should do right now, given what the server last said. Pure, so the timing (the part that decides whether there is ever silence) is tested.
//
// The server's timeline has no overlap: the next track starts the instant the current one ends. To blend the two smoothly the client starts the next track
// CrossfadeMs BEFORE the end (fading the old one out while the new one fades in), so at the moment the server moves on, the new song is already fully there.
public readonly record struct MusicStep(string PlayId, bool Early, long PollInMs) {
    public static readonly MusicStep Nothing = new(null, false, -1);
}

public static class MusicPlan {

    public const int CrossfadeMs = 3000;

    // How much earlier than the crossfade window an answer may still count as "the server is about to move on" (network delay, the clocks not agreeing exactly).
    public const int EarlyMarginMs = 1500;

    // heardAtMs = the local clock when `state` arrived; nowMs = the local clock now; playingId = the track on the speakers ("" or null = none of ours yet).
    public static MusicStep Decide(MusicNowData state, long heardAtMs, long nowMs, string playingId) {
        var current = state.Find(state.CurrentId);
        if (current == null) {
            return MusicStep.Nothing;
        }

        var remaining = current.LengthMs - (state.PositionMs + (nowMs - heardAtMs));

        if (playingId != state.CurrentId) {
            // We started the next song ahead of the server's clock (the crossfade below) and this answer was worked out just before the server moved on:
            // being on "next" is expected, not a mistake. Jumping back to the old song here would be an audible glitch in the middle of every crossfade.
            if (playingId == state.NextId && remaining <= CrossfadeMs + EarlyMarginMs) {
                return MusicStep.Nothing;
            }

            // Someone changed the song (or we just arrived): go to what the server says is playing.
            return new MusicStep(state.CurrentId, false, -1);
        }

        // Playing the right song: when its end is near, start the next one early so they overlap. (One-song library: the engine just loops it.)
        if (remaining <= CrossfadeMs && state.NextId != state.CurrentId) {
            return new MusicStep(state.NextId, true, Math.Max(remaining, 0) + 700);   // ask the server again just after it has moved on
        }

        return MusicStep.Nothing;
    }
}
