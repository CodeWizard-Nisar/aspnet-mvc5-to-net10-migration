using Characterization.Harness;
using Xunit;

namespace Characterization.Tests;

/// <summary>
/// Added in Phase 3 (a new file; no Phase 2 test was edited). Phase 1 stored admin passwords as reversible AES
/// ciphertext, and the seed still does, deliberately. After Phase 3 those users must still log in, and the first
/// successful login must silently replace the stored value with a BCrypt hash.
/// </summary>
[Collection("app")]
public class PasswordUpgradeTests
{
    private readonly AppFixture _app;
    public PasswordUpgradeTests(AppFixture app) { _app = app; _app.Reset(); }

    private static string Stored(string email) => Db.Scalar<string>("SELECT PasswordHash FROM AdminUsers WHERE Email=@e", ("@e", email))!;

    [Theory]
    [InlineData("admin@confreg.example", "ConfReg-Demo-1!")]
    [InlineData("staff@confreg.example", "ConfReg-Demo-2!")]
    public async Task First_login_upgrades_a_legacy_password_to_bcrypt_and_it_keeps_working(string email, string password)
    {
        Assert.False(Stored(email).StartsWith("$2"), "precondition: the seed holds legacy ciphertext");

        using (var first = new ConfRegClient())
            Assert.Equal(302, (await first.Post("/Account/Login", ("Email", email), ("Password", password))).Code);

        string after = Stored(email);
        Assert.StartsWith("$2", after);
        Assert.DoesNotContain(password, after);

        using (var second = new ConfRegClient())
        {
            Assert.Equal(302, (await second.Post("/Account/Login", ("Email", email), ("Password", password))).Code);
            Assert.Equal(200, (await second.Post("/Account/Login", ("Email", email), ("Password", "wrong"))).Code);
        }
        Assert.Equal(after, Stored(email)); // a later login does not rewrite it again
    }

    [Fact]
    public async Task A_wrong_password_neither_logs_in_nor_upgrades_the_row()
    {
        string before = Stored("admin@confreg.example");
        using var c = new ConfRegClient();
        var r = await c.Post("/Account/Login", ("Email", "admin@confreg.example"), ("Password", "not it"));
        Assert.Equal(200, r.Code);
        Assert.Equal(before, Stored("admin@confreg.example"));
    }
}
