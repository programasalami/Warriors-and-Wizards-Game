namespace Alloy.Audio;

/// <summary>
/// Reads PCM straight out of a .wav file's "data" chunk - no decode library needed (unlike
/// <see cref="VorbisAudioStream"/>/<see cref="Mp3AudioStream"/>), since it's already raw samples.
/// Supports the two common WAV sample layouts: 16-bit signed integer PCM and 32-bit float PCM,
/// both converted to interleaved 16-bit signed PCM to match <see cref="IAudioStream"/>.
/// </summary>
internal sealed class WavAudioStream : IAudioStream {
    private const int SamplesPerBuffer = 4096 * 2;

    private readonly Stream _stream;
    private readonly long _dataStart;
    private readonly long _dataLength;
    private readonly int _bytesPerSample;
    private readonly bool _isFloat;
    private readonly byte[] _rawBuffer;

    public int Channels { get; }
    public int SampleRate { get; }
    public short[] SongBuffer { get; } = new short[SamplesPerBuffer];
    public int Decoded { get; private set; }

    public WavAudioStream(Stream stream) {
        _stream = stream;
        ParseHeader(stream, out _dataStart, out _dataLength, out var channels, out var sampleRate, out var bitsPerSample, out _isFloat);
        Channels = channels;
        SampleRate = sampleRate;
        _bytesPerSample = bitsPerSample / 8;
        _rawBuffer = new byte[SamplesPerBuffer * _bytesPerSample];
        stream.Position = _dataStart;
    }

    public void SubmitBuffer() {
        var remainingBytes = _dataStart + _dataLength - _stream.Position;
        var maxSamples = (int)Math.Min(SamplesPerBuffer, remainingBytes / _bytesPerSample);
        var bytesToRead = maxSamples * _bytesPerSample;

        var read = ReadUpTo(_stream, _rawBuffer, bytesToRead);
        var sampleCount = read / _bytesPerSample;

        ConvertToInt16(_rawBuffer, sampleCount, _bytesPerSample, _isFloat, SongBuffer);

        // Vorbis/Mp3's "Decoded" counts sample-frames (samples per single channel), not raw
        // interleaved values - StreamTrack multiplies it by Channels*sizeof(short) itself.
        Decoded = sampleCount / Channels;
    }

    public void Restart() => _stream.Position = _dataStart;

    public void Dispose() => _stream.Dispose();

    /// <summary>Fully decodes an in-memory WAV to interleaved 16-bit PCM, for one-shot (static)
    /// playback - the streaming path above is for long-running tracks like music instead.</summary>
    public static short[] DecodeFully(byte[] fileData, out int sampleRate, out int channels) {
        using var stream = new MemoryStream(fileData);
        ParseHeader(stream, out var dataStart, out var dataLength, out channels, out sampleRate, out var bitsPerSample, out var isFloat);

        var bytesPerSample = bitsPerSample / 8;
        stream.Position = dataStart;
        var raw = new byte[dataLength];
        ReadUpTo(stream, raw, raw.Length);

        var output = new short[raw.Length / bytesPerSample];
        ConvertToInt16(raw, output.Length, bytesPerSample, isFloat, output);
        return output;
    }

    private static int ReadUpTo(Stream stream, byte[] buffer, int count) {
        var offset = 0;
        while (offset < count) {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0) break;
            offset += read;
        }
        return offset;
    }

    private static void ParseHeader(Stream stream, out long dataStart, out long dataLength, out int channels, out int sampleRate, out int bitsPerSample, out bool isFloat) {
        var riffHeader = new byte[12];
        ReadUpTo(stream, riffHeader, riffHeader.Length);

        channels = 0;
        sampleRate = 0;
        bitsPerSample = 0;
        isFloat = false;

        var chunkHeader = new byte[8];
        while (true) {
            if (ReadUpTo(stream, chunkHeader, chunkHeader.Length) < chunkHeader.Length) {
                throw new EndOfStreamException("WAV file has no 'data' chunk");
            }

            var chunkId = System.Text.Encoding.ASCII.GetString(chunkHeader, 0, 4);
            var chunkSize = BitConverter.ToUInt32(chunkHeader, 4);

            if (chunkId == "fmt ") {
                var fmt = new byte[chunkSize];
                ReadUpTo(stream, fmt, fmt.Length);
                var audioFormat = BitConverter.ToUInt16(fmt, 0);
                channels = BitConverter.ToUInt16(fmt, 2);
                sampleRate = (int)BitConverter.ToUInt32(fmt, 4);
                bitsPerSample = BitConverter.ToUInt16(fmt, 14);
                isFloat = audioFormat == 3;
            } else if (chunkId == "data") {
                dataStart = stream.Position;
                dataLength = chunkSize;
                return;
            } else {
                stream.Seek(chunkSize + (chunkSize % 2), SeekOrigin.Current);
            }
        }
    }

    private static void ConvertToInt16(byte[] raw, int sampleCount, int bytesPerSample, bool isFloat, short[] output) {
        if (isFloat && bytesPerSample == 4) {
            for (var i = 0; i < sampleCount; i++) {
                var f = BitConverter.ToSingle(raw, i * 4);
                output[i] = (short)(Math.Clamp(f, -1f, 1f) * short.MaxValue);
            }
        } else if (bytesPerSample == 2) {
            for (var i = 0; i < sampleCount; i++) {
                output[i] = BitConverter.ToInt16(raw, i * 2);
            }
        } else {
            throw new NotSupportedException($"Unsupported WAV sample format: {(isFloat ? "float" : "int")}{bytesPerSample * 8}-bit (only 16-bit PCM and 32-bit float are supported)");
        }
    }
}
