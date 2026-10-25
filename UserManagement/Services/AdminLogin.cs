using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace UserManagement.Services;

/// <summary>
/// Bound from the "Admin" section. Set the password in user secrets or an environment variable, never in a committed file:
///   dotnet user-secrets set "Admin:Password" "a long passphrase"
///   (environment variable form: Admin__Password)
/// With no password configured nobody can sign in, so the admin pages stay locked.
/// </summary>
public class AdminOptions
{
    public string Username { get; set; } = "admin";
    public string? Password { get; set; }

    public bool Configured => !string.IsNullOrEmpty(Password);
}

public interface IAdminLogin
{
    bool Configured { get; }

    /// <summary>Returns true when the credentials are right. Counts failures per client and locks out repeated guessing.</summary>
    LoginResult TryLogin(string? username, string? password, string client);
}

public enum LoginResult { Success, Failed, LockedOut, NotConfigured }

public class AdminLogin : IAdminLogin
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockTime = TimeSpan.FromMinutes(15);

    private readonly AdminOptions _options;
    private readonly ConcurrentDictionary<string, (int Failures, DateTime First)> _failures = new();

    public AdminLogin(Microsoft.Extensions.Options.IOptions<AdminOptions> options) => _options = options.Value;

    public bool Configured => _options.Configured;

    public LoginResult TryLogin(string? username, string? password, string client)
    {
        if (!_options.Configured) return LoginResult.NotConfigured;

        if (_failures.TryGetValue(client, out var state))
        {
            if (DateTime.UtcNow - state.First > LockTime) _failures.TryRemove(client, out _);
            else if (state.Failures >= MaxFailures) return LoginResult.LockedOut;
        }

        // Compare both fields every time so timing does not reveal which one was wrong.
        var userOk = SafeEquals(username ?? "", _options.Username);
        var passOk = SafeEquals(password ?? "", _options.Password!);
        if (userOk && passOk)
        {
            _failures.TryRemove(client, out _);
            return LoginResult.Success;
        }

        _failures.AddOrUpdate(client,
            _ => (1, DateTime.UtcNow),
            (_, old) => (old.Failures + 1, old.First));
        return LoginResult.Failed;
    }

    /// <summary>Constant-time comparison (hashing first makes the inputs the same length).</summary>
    private static bool SafeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
