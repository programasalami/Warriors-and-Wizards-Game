using StbVorbisSharp;

namespace Alloy.Audio;

internal sealed class VorbisAudioStream(Vorbis vorbis) : IAudioStream {
    public int Channels => vorbis.Channels;
    public int SampleRate => vorbis.SampleRate;
    public short[] SongBuffer => vorbis.SongBuffer;
    public int Decoded => vorbis.Decoded;

    public void SubmitBuffer() => vorbis.SubmitBuffer();
    public void Restart() => vorbis.Restart();
    public void Dispose() => vorbis.Dispose();
}
