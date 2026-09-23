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
        // Taken note of HERE, on the network thread, as the packet is read (2026-09-22): the server closes the socket right after this
        // packet, and the disconnect that follows can clear the incoming queue before Handle ever runs - so a player whose game had just
        // been left behind by an update was bounced to the book with no "Update required" and no idea why, again and again.
        if (ErrorId == VersionCheck.IncorrectVersionFailureId) {
            VersionCheck.ReportServerVersion(ErrorDescription);
        }
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