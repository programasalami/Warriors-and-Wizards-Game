using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Messaging;

// Minimal shared-secret gate performed on the raw (already TLS-authenticated)
// stream before any JsonRpc surface is exposed - an unauthenticated peer never
// even sees the RPC method schema, just an immediately-closed connection.
public static class RpcHandshake {
    private const int MaxSecretBytes = 4096;

    public static async Task SendSecretAsync(Stream stream, string secret, CancellationToken ct = default) {
        var bytes = Encoding.UTF8.GetBytes(secret);
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);

        var response = new byte[1];
        await ReadExactAsync(stream, response, ct);
        if (response[0] != 1)
            throw new UnauthorizedAccessException("RPC handshake rejected - shared secret mismatch.");
    }

    public static async Task<bool> ValidateSecretAsync(Stream stream, string expectedSecret, CancellationToken ct = default) {
        var lengthBytes = new byte[4];
        await ReadExactAsync(stream, lengthBytes, ct);
        var length = BitConverter.ToInt32(lengthBytes);

        if (length < 0 || length > MaxSecretBytes) {
            await stream.WriteAsync(new byte[] { 0 }, ct);
            await stream.FlushAsync(ct);
            return false;
        }

        var secretBytes = new byte[length];
        await ReadExactAsync(stream, secretBytes, ct);
        var ok = Encoding.UTF8.GetString(secretBytes) == expectedSecret;

        await stream.WriteAsync(new byte[] { (byte)(ok ? 1 : 0) }, ct);
        await stream.FlushAsync(ct);
        return ok;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct) {
        var offset = 0;
        while (offset < buffer.Length) {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
                throw new IOException("Connection closed during RPC handshake.");
            offset += read;
        }
    }
}
