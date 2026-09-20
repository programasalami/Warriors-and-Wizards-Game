using AlloyClient.AppEngine;
using Microsoft.Extensions.Logging;

namespace AlloyClient.Networking.Packets.Incoming;

public class Failure : IncomingPacket<Failure> {
    public int ErrorId;
    public string ErrorDescription;

    public override PacketId PacketId => PacketId.Failure;

    public override void Reset() {
        ErrorId = 0;
        ErrorDescription = null;
    }

    public override void Read(ref SpanReader reader) {
        ErrorId = reader.ReadInt32();
        ErrorDescription = reader.ReadUTF();
    }

    public override void Handle() {
        Client.Logger.Log(LogLevel.Information, $"Error: {ErrorId} - {ErrorDescription}");

        // The server wants a different build than this client is: remember which, so the screen we land on can say so.
        if (ErrorId == VersionCheck.IncorrectVersionFailureId) {
            VersionCheck.ReportServerVersion(ErrorDescription);
        }

        Client.Disconnect(ErrorDescription);
    }

    public override string ToString() {
        return $"ErrorId: {ErrorId}, ErrorDescription: {ErrorDescription}";
    }
}