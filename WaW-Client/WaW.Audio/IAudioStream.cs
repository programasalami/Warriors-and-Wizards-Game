namespace WaW.Audio;

/// <summary>
/// Common shape a streamed (chunk-decoded) audio source must expose to <see cref="StreamTrack"/>,
/// regardless of the underlying file format/decoder. <see cref="VorbisAudioStream"/>,
/// <see cref="Mp3AudioStream"/>, and <see cref="WavAudioStream"/> are the implementations.
/// </summary>
internal interface IAudioStream : IDisposable {
    int Channels { get; }
    int SampleRate { get; }

    /// <summary>PCM samples from the most recent <see cref="SubmitBuffer"/> call.</summary>
    short[] SongBuffer { get; }

    /// <summary>Number of samples in <see cref="SongBuffer"/> from the most recent decode.</summary>
    int Decoded { get; }

    /// <summary>Decodes the next chunk into <see cref="SongBuffer"/>, updating <see cref="Decoded"/>.</summary>
    void SubmitBuffer();

    /// <summary>Seeks back to the start of the track, for looping.</summary>
    void Restart();
}
