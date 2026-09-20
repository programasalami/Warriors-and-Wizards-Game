// Browser stand-in for the desktop Alloy.Audio engine (OpenAL): same public surface the client uses. Music/sfx play through the
// page's Web Audio / <audio> (wwwroot/audio.js); files come from the in-memory Content folder the page downloaded.
using System.Runtime.InteropServices.JavaScript;
using Microsoft.Extensions.Logging;

namespace Alloy.Audio;

internal static partial class WebAudio {
    [JSImport("playMusic", "audio")] internal static partial void PlayMusic(string path, float fadeSeconds);
    [JSImport("setMusicVolume", "audio")] internal static partial void SetMusicVolume(float volume);
    [JSImport("setMasterVolume", "audio")] internal static partial void SetMasterVolume(float volume);
    [JSImport("playSfx", "audio")] internal static partial void PlaySfx(string path, float volume);
}

public abstract class AudioChannel {
    internal float Volume = 1f;
    public double MiniumRepeatDelayMs { get; set; } = 1000d / 15;
    public virtual void SetVolume(float volume) => Volume = Math.Clamp(volume, 0f, 1f);
}

public class SingleTrackChannel : AudioChannel {
    public override void SetVolume(float volume) { base.SetVolume(volume); WebAudio.SetMusicVolume(Volume); }
    public void Play(string name) => FadeTo(name, 0f);
    public void FadeTo(string name, float durationSeconds) => WebAudio.PlayMusic(name, durationSeconds);
    public void FadeTo(string name, float fadeOutDurationSeconds, float fadeInDurationSeconds) => WebAudio.PlayMusic(name, fadeInDurationSeconds);
}

public class SfxChannel : AudioChannel {
    public void Play(string name) => WebAudio.PlaySfx(name, Volume);
}

public sealed class AudioEngine {
    public AudioEngine(ILoggerFactory logFactory, string localPath) { }
    public SingleTrackChannel CreateSingleTrackChannel() => new();
    public SfxChannel CreateSfxChannel() => new();
    public void Start() { }
    public void StopAndDispose() { }
    public void SetMasterVolume(float volume) => WebAudio.SetMasterVolume(volume);
}
