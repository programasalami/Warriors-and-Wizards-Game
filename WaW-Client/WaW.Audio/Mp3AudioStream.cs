using NLayer;

namespace WaW.Audio;

internal sealed class Mp3AudioStream : IAudioStream {
    // Interleaved float count per decode chunk - matches roughly the chunk size the vorbis
    // decoder already streams at.
    private const int SamplesPerBuffer = 4096 * 2;

    private readonly MpegFile _mpeg;
    private readonly float[] _floatBuffer = new float[SamplesPerBuffer];

    public int Channels { get; }
    public int SampleRate { get; }
    public short[] SongBuffer { get; } = new short[SamplesPerBuffer];
    public int Decoded { get; private set; }

    public Mp3AudioStream(Stream stream) {
        _mpeg = new MpegFile(stream);
        Channels = _mpeg.Channels;
        SampleRate = _mpeg.SampleRate;
    }

    public void SubmitBuffer() {
        var read = _mpeg.ReadSamples(_floatBuffer, 0, _floatBuffer.Length);

        for (var i = 0; i < read; i++) {
            SongBuffer[i] = (short)(Math.Clamp(_floatBuffer[i], -1f, 1f) * short.MaxValue);
        }

        // Vorbis' "Decoded" counts sample-frames (samples per single channel), not raw
        // interleaved values - StreamTrack multiplies it by Channels*sizeof(short) itself.
        Decoded = read / Channels;
    }

    public void Restart() => _mpeg.Position = 0;

    public void Dispose() => _mpeg.Dispose();

    /// <summary>Fully decodes an in-memory MP3 to interleaved 16-bit PCM, for one-shot (static)
    /// playback - the streaming path above is for long-running tracks like music instead.</summary>
    public static short[] DecodeFully(byte[] fileData, out int sampleRate, out int channels) {
        using var mpeg = new MpegFile(new MemoryStream(fileData));
        sampleRate = mpeg.SampleRate;
        channels = mpeg.Channels;

        var floatBuffer = new float[4096 * channels];
        var samples = new List<short>();

        int read;
        while ((read = mpeg.ReadSamples(floatBuffer, 0, floatBuffer.Length)) > 0) {
            for (var i = 0; i < read; i++) {
                samples.Add((short)(Math.Clamp(floatBuffer[i], -1f, 1f) * short.MaxValue));
            }
        }

        return samples.ToArray();
    }
}
