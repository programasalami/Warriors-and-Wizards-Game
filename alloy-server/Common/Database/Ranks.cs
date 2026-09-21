using System;
using Common.Database.Models;

namespace Common.Database;

// The three ranks of an account. Every new account is a Player. Owners can use every command and every server-side feature; Moderators get the moderation
// commands (kick, mute, ban ...) but nothing that can break the game; Players get the everyday informational commands only.
//
// Stored as Account.Rank (an int, so more ranks can be slotted in between later) plus the older Account.IsAdmin flag, which means "Owner". Always ask
// Ranks.Of(account) - never read Rank or IsAdmin directly - so both spellings of "owner" agree.
public static class Ranks {
    public const int Player = 0;
    public const int Moderator = 80;
    public const int Owner = 100;

    // The rank an account really has. IsAdmin wins (old accounts were made admin that way); an unknown number rounds DOWN to the rank it has reached.
    public static int Of(Account acc) {
        if (acc == null)
            return Player;
        if (acc.IsAdmin || acc.Rank >= Owner)
            return Owner;
        return acc.Rank >= Moderator ? Moderator : Player;
    }

    public static string Name(int rank) => rank >= Owner ? "Owner" : rank >= Moderator ? "Moderator" : "Player";

    public static bool TryParse(string text, out int rank) {
        rank = Player;
        switch ((text ?? string.Empty).Trim().ToLowerInvariant()) {
            case "owner": rank = Owner; return true;
            case "moderator":
            case "mod": rank = Moderator; return true;
            case "player":
            case "user": rank = Player; return true;
            default: return false;
        }
    }

    // Puts an account on a rank (Owner also sets the legacy admin flag; anything lower clears it).
    public static void Set(Account acc, int rank) {
        acc.Rank = rank >= Owner ? Owner : rank >= Moderator ? Moderator : Player;
        acc.IsAdmin = acc.Rank == Owner;
    }

    public static bool IsModerator(Account acc) => Of(acc) >= Moderator;
    public static bool IsOwner(Account acc) => Of(acc) >= Owner;

    // Moderating someone (kick, mute, ban ...): you must be a Moderator or better AND strictly above the target. So moderators cannot touch other moderators
    // or owners, and nobody can moderate an owner. Yourself is never a valid target.
    public static bool CanModerate(Account actor, Account target) {
        if (actor == null || target == null || actor.Id == target.Id)
            return false;
        var mine = Of(actor);
        return mine >= Moderator && mine > Of(target);
    }
}
