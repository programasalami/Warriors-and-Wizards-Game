using System.Security.Cryptography;
using System.Text;
using Common.Database;

namespace Common.Tests;

// Passwords: PBKDF2 for new hashes, the old SHA1(password + salt) still accepted and flagged for a silent upgrade on the next
// successful login (2026-09-21 audit, C7). Nobody's password is reset by the change.
public class PasswordHasherTests {
    private const string Salt = "c2FsdHNhbHRzYWx0c2FsdA==";

    private static string LegacyHash(string password, string salt) =>
        Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(password + salt)));

    [Fact]
    public void NewHashesVerifyAndCarryThePrefix() {
        var hash = PasswordHasher.Hash("correct horse", Salt);
        Assert.StartsWith("pbkdf2$", hash);
        Assert.True(PasswordHasher.Verify("correct horse", Salt, hash));
        Assert.False(PasswordHasher.Verify("correct horsE", Salt, hash));
        Assert.False(PasswordHasher.Verify("correct horse", "othersalt", hash));
        Assert.False(PasswordHasher.NeedsUpgrade(hash));
    }

    [Fact]
    public void TheSamePasswordHashesDifferentlyWithADifferentSalt() {
        Assert.NotEqual(PasswordHasher.Hash("pw123456789", Salt), PasswordHasher.Hash("pw123456789", "different salt"));
    }

    [Fact]
    public void OldSha1HashesStillVerifyAndAreFlaggedForUpgrade() {
        var legacy = LegacyHash("oldpassword1", Salt);
        Assert.True(PasswordHasher.IsLegacy(legacy));
        Assert.True(PasswordHasher.NeedsUpgrade(legacy));
        Assert.True(PasswordHasher.Verify("oldpassword1", Salt, legacy));
        Assert.False(PasswordHasher.Verify("oldpassword2", Salt, legacy));
    }

    [Fact]
    public void GarbageNeverVerifies() {
        Assert.False(PasswordHasher.Verify("x", Salt, null));
        Assert.False(PasswordHasher.Verify("x", Salt, ""));
        Assert.False(PasswordHasher.Verify("x", Salt, "pbkdf2$notanumber$abc"));
        Assert.False(PasswordHasher.Verify("x", Salt, "pbkdf2$1000$not base64!!"));
    }

    [Fact]
    public void FewerIterationsThanCurrentCountsAsNeedingAnUpgrade() {
        Assert.True(PasswordHasher.NeedsUpgrade("pbkdf2$1000$AAAA"));
        Assert.False(PasswordHasher.NeedsUpgrade($"pbkdf2${PasswordHasher.Iterations}$AAAA"));
    }
}
