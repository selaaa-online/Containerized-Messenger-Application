using ChatServer.Auth;
using Xunit;

namespace ChatServer.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ProducesDifferentSalts_ForSamePassword()
    {
        var (hash1, salt1) = PasswordHasher.Hash("secret");
        var (hash2, salt2) = PasswordHasher.Hash("secret");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var (hash, salt) = PasswordHasher.Hash("correct horse");

        Assert.True(PasswordHasher.Verify("correct horse", hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var (hash, salt) = PasswordHasher.Hash("correct horse");

        Assert.False(PasswordHasher.Verify("wrong horse", hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForTamperedHash()
    {
        var (hash, salt) = PasswordHasher.Hash("secret");
        var tampered = Convert.ToBase64String(new byte[32]); // all-zero key

        Assert.False(PasswordHasher.Verify("secret", tampered, salt));
    }
}
