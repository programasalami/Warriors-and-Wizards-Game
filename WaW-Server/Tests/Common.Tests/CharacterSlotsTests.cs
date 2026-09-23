using Common.Database;
using Common.Database.Models;

namespace Common.Tests;

// Deleted (and dead) characters must not take up a slot: an account with MaxChars = 2 that deleted both characters can create again.
public class CharacterSlotsTests {

    private static Character Chr(int id, bool deleted = false, bool dead = false) => new() { CharId = id, ObjectType = 0x0300, IsDeleted = deleted, IsDead = dead };

    [Fact]
    public void LivingCharactersUseSlots() {
        Assert.Equal(2, CharacterSlots.Used([Chr(1), Chr(2)]));
        Assert.False(CharacterSlots.HasFree([Chr(1), Chr(2)], 2));
        Assert.True(CharacterSlots.HasFree([Chr(1)], 2));
    }

    [Fact]
    public void DeletedCharactersFreeTheirSlot() {
        var chars = new List<Character> { Chr(1, deleted: true), Chr(2, deleted: true) };
        Assert.Equal(0, CharacterSlots.Used(chars));
        Assert.True(CharacterSlots.HasFree(chars, 2));
        Assert.True(CharacterSlots.HasFree([Chr(1, deleted: true), Chr(2), Chr(3, deleted: true)], 2));
    }

    [Fact]
    public void DeadCharactersFreeTheirSlot() {
        Assert.Equal(1, CharacterSlots.Used([Chr(1, dead: true), Chr(2)]));
    }

    [Fact]
    public void NoCharactersMeansNothingUsed() {
        Assert.Equal(0, CharacterSlots.Used(null));
        Assert.Equal(0, CharacterSlots.Used([]));
        Assert.False(CharacterSlots.HasFree([], 0));
    }
}
