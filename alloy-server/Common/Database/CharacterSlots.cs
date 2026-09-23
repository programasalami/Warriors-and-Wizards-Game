using System.Collections.Generic;
using System.Linq;
using Common.Database.Models;

namespace Common.Database;

// How many character slots an account has in use (2026-09-21): only characters that still appear in the character list count - a deleted
// character (DELETE in the book) or a dead one frees its slot. CreateCharacterAsync used to count every character ever made, so an account
// that deleted both of its characters could never create another one ("kicked back to the book" on every try).
public static class CharacterSlots {
    public static int Used(IEnumerable<Character> characters) =>
        characters?.Count(c => c != null && !c.IsDeleted && !c.IsDead) ?? 0;

    public static bool HasFree(IEnumerable<Character> characters, int maxChars) => Used(characters) < maxChars;
}
