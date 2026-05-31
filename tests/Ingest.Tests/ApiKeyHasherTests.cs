using Ingest.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ingest.Tests;

public class ApiKeyHasherTests
{
    private static ApiKeyHasher MakeHasher(string pepper = "test-pepper") =>
        new(Options.Create(new ApiKeyOptions { Pepper = pepper }));

    [Fact]
    public void Generate_then_verify_succeeds()
    {
        var hasher = MakeHasher();
        var key = hasher.Generate();

        Assert.True(hasher.TrySplit(key.Plaintext, out var keyId, out var secret));
        Assert.Equal(key.KeyId, keyId);
        Assert.Equal(key.Secret, secret);

        Assert.True(hasher.Verify(secret, key.Salt, key.Hash));
    }

    [Fact]
    public void Verify_rejects_wrong_secret()
    {
        var hasher = MakeHasher();
        var key = hasher.Generate();
        Assert.False(hasher.Verify("not-the-secret", key.Salt, key.Hash));
    }

    [Fact]
    public void Verify_rejects_when_pepper_changes()
    {
        var h1 = MakeHasher("pepper-one");
        var key = h1.Generate();
        var h2 = MakeHasher("pepper-two");
        Assert.False(h2.Verify(key.Secret, key.Salt, key.Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData(".onlysecret")]
    [InlineData("onlyid.")]
    public void TrySplit_rejects_malformed(string input)
    {
        var hasher = MakeHasher();
        Assert.False(hasher.TrySplit(input, out _, out _));
    }

    [Fact]
    public void Import_then_verify_succeeds()
    {
        var hasher = MakeHasher();
        var imported = hasher.Import("localdev.local-dev-admin-key-change-me");

        Assert.NotNull(imported);
        Assert.Equal("localdev", imported!.KeyId);
        Assert.Equal("local-dev-admin-key-change-me", imported.Secret);
        // The persisted salt+hash must verify against the secret the caller supplied.
        Assert.True(hasher.Verify(imported.Secret, imported.Salt, imported.Hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData("onlyid.")]
    public void Import_rejects_malformed(string input)
    {
        var hasher = MakeHasher();
        Assert.Null(hasher.Import(input));
    }
}
