using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading;
using AlloyClient.Networking.Packets;

namespace AlloyClient.Networking;

// Outgoing bytes: packets are written into _writeBuffer during the frame, then the two buffers are swapped and _sendBuffer goes to
// the socket. The write buffer grows on demand up to MaxBufferBytes; a packet that would not fit even then is dropped (and counted)
// instead of throwing out of QueuePacket on the main thread - which is what used to freeze the client when a burst of hit packets
// overflowed the old fixed 64 KB buffer (2026-09-21 audit).
public class SocketSendState : IDisposable
{
    public const int InitialBufferBytes = 0x10000;   // 64 KB
    public const int MaxBufferBytes = 0x100000;      // 1 MB - far beyond any honest frame

    private byte[] _writeBuffer;
    private byte[] _sendBuffer;
    private int _writeLength;
    private int _sendLength;
    private int _sendOffset;
    private bool _pending;

    public int PendingBytes => _writeLength;
    public int Capacity => _writeBuffer.Length;
    public long DroppedPackets { get; private set; }

    public SocketSendState()
    {
        _sendBuffer = ArrayPool<byte>.Shared.Rent(InitialBufferBytes);
        Array.Clear(_sendBuffer);
        _writeBuffer = ArrayPool<byte>.Shared.Rent(InitialBufferBytes);
        Array.Clear(_writeBuffer);
    }

    public void Reset()
    {
        _writeLength = 0;
        _sendLength = 0;
        _sendOffset = 0;
        _pending = false;
    }

    // True if the packet was written, false if it was dropped because the buffer is at its hard limit.
    public bool WritePacket(IOutgoingPacket pkt, byte pktId)
    {
        lock (this)
        {
            while (true)
            {
                var start = _writeLength;
                var bodyStart = start + 5;
                var writer = new SpanWriter(_writeBuffer.AsSpan());
                writer.Position = bodyStart;

                try
                {
                    pkt.Write(ref writer);
                }
                catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
                {
                    if (!TryGrow())
                    {
                        DroppedPackets++;
                        return false;
                    }
                    continue;   // retry into the bigger buffer
                }

                var totalLen = writer.Position - start;
                writer.Position = start;
                writer.Write(totalLen);
                writer.Write(pktId);
                _writeLength += totalLen;
                return true;
            }
        }
    }

    private bool TryGrow()
    {
        if (_writeBuffer.Length >= MaxBufferBytes)
            return false;

        var bigger = ArrayPool<byte>.Shared.Rent(Math.Min(_writeBuffer.Length * 2, MaxBufferBytes));
        Buffer.BlockCopy(_writeBuffer, 0, bigger, 0, _writeLength);
        ArrayPool<byte>.Shared.Return(_writeBuffer);
        _writeBuffer = bigger;
        return true;
    }

    public bool TryBeginSend(SocketAsyncEventArgs args) {
        lock (this) {
            if (_pending)
                return false;

            if (_writeLength == 0)
            {
                _pending = false;
                return false;
            }

            _pending = true;
            var tmp = _sendBuffer;
            _sendBuffer = _writeBuffer;
            _writeBuffer = tmp;

            _sendLength = _writeLength;
            _writeLength = 0;
            _sendOffset = 0;

            args.SetBuffer(_sendBuffer, 0, _sendLength);
        }
        return true;
    }

    public bool OnDataSent(SocketAsyncEventArgs args) // If all bytes were transferred, lastvalidindex will be 0 again
    {
        lock (this)
        {
            _sendOffset += args.BytesTransferred;

            if (_sendOffset < _sendLength)
            {
                // continue sending remaining bytes
                args.SetBuffer(_sendBuffer, _sendOffset, _sendLength - _sendOffset);
                return true; // continue send
            }

            // done
            _sendLength = 0;
            _sendOffset = 0;
            _pending = false;
            return false;
        }
    }

    public void Dispose()
    {
        // Return the buffer to the pool for other connections to use
        var buf = Interlocked.Exchange(ref _sendBuffer, null);
        if (buf != null)
            ArrayPool<byte>.Shared.Return(buf);
        buf = Interlocked.Exchange(ref _writeBuffer, null);
        if (buf != null)
            ArrayPool<byte>.Shared.Return(buf);
    }
}
