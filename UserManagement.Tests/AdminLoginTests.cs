using Microsoft.Extensions.Options;
using UserManagement.Services;

namespace UserManagement.Tests;

public class AdminLoginTests
{
    private static AdminLogin Create(string? password = "correct horse", string username = "admin") =>
        new(Options.Create(new AdminOptions { Username = username, Password = password }));

    [Fact]
    public void NoPasswordConfigured_NobodyCanSignIn()
    {
        var login = Create(password: null);
        Assert.False(login.Configured);
        Assert.Equal(LoginResult.NotConfigured, login.TryLogin("admin", "", "1.1.1.1"));
        Assert.Equal(LoginResult.NotConfigured, login.TryLogin("admin", null, "1.1.1.1"));
    }

    [Fact]
    public void CorrectCredentials_Succeed()
    {
        Assert.Equal(LoginResult.Success, Create().TryLogin("admin", "correct horse", "1.1.1.1"));
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("root", "correct horse")]
    [InlineData("", "")]
    [InlineData(null, null)]
    [InlineData("ADMIN", "correct horse")] // username and password are case-sensitive
    public void WrongCredentials_Fail(string? username, string? password)
    {
        Assert.Equal(LoginResult.Failed, Create().TryLogin(username, password, "1.1.1.1"));
    }

    [Fact]
    public void FiveFailures_LockOutThatClient_EvenWithTheRightPassword()
    {
        var login = Create();
        for (var i = 0; i < 5; i++) Assert.Equal(LoginResult.Failed, login.TryLogin("admin", "nope", "9.9.9.9"));

        Assert.Equal(LoginResult.LockedOut, login.TryLogin("admin", "correct horse", "9.9.9.9"));
        // another client is not affected
        Assert.Equal(LoginResult.Success, login.TryLogin("admin", "correct horse", "2.2.2.2"));
    }

    [Fact]
    public void SuccessfulSignIn_ResetsTheFailureCount()
    {
        var login = Create();
        for (var i = 0; i < 4; i++) login.TryLogin("admin", "nope", "3.3.3.3");
        Assert.Equal(LoginResult.Success, login.TryLogin("admin", "correct horse", "3.3.3.3"));

        for (var i = 0; i < 4; i++) Assert.Equal(LoginResult.Failed, login.TryLogin("admin", "nope", "3.3.3.3"));
    }
}
