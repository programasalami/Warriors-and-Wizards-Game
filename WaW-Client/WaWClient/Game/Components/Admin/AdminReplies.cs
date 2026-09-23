using System;
using System.Collections.Generic;

namespace WaWClient.Game.Components.Admin;

// The last few messages the SERVER itself said to this player in chat (command answers such as "Banned Bob 1 day." or "You are not authorized"), kept so the
// admin dashboard can show what happened to the buttons that were just pressed. Fed by the incoming Text packet.
public static class AdminReplies {
    public const int Capacity = 6;

    private static readonly object Lock = new();
    private static readonly List<string> Lines = [];

    // Goes up on every new reply, so a window can cheaply tell whether it has anything new to draw.
    public static int Version { get; private set; }

    // Server messages have no speaker name (info) or one of the "*Error*" / "*Help*" pseudo names.
    public static bool IsServerMessage(string speaker) => string.IsNullOrEmpty(speaker) || speaker is "*Error*" or "*Help*";

    public static void Add(string speaker, string text) {
        if (!IsServerMessage(speaker) || string.IsNullOrWhiteSpace(text)) {
            return;
        }

        lock (Lock) {
            Lines.Add(speaker == "*Error*" ? "! " + text : text);
            while (Lines.Count > Capacity) {
                Lines.RemoveAt(0);
            }

            Version++;
        }
    }

    public static string[] Recent() {
        lock (Lock) {
            return Lines.ToArray();
        }
    }

    public static void Clear() {
        lock (Lock) {
            Lines.Clear();
            Version++;
        }
    }
}
