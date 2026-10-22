using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using UserManagement.Models;

namespace UserManagement.Data;

public interface IGeoProviderRepository
{
    /// <summary>All providers, keys masked (for the admin list).</summary>
    Task<IReadOnlyList<GeoProvider>> GetAllAsync();
    /// <summary>One provider with its decrypted key (for the app to call the API, not for display).</summary>
    Task<GeoProvider?> GetAsync(int id);
    /// <summary>The active provider with its decrypted key, or null when none is active.</summary>
    Task<GeoProvider?> GetActiveAsync();
    /// <summary>True when the table has no rows at all (first run, before anything was configured).</summary>
    Task<bool> IsEmptyAsync();
    Task<int> CreateAsync(GeoProvider provider);
    /// <summary>Updates a provider. A blank ApiKey keeps the stored key.</summary>
    Task UpdateAsync(GeoProvider provider);
    /// <summary>Makes this provider the only active one, or turns it off.</summary>
    Task SetActiveAsync(int id, bool active);
    Task DeleteAsync(int id);
}

public class GeoProviderRepository : IGeoProviderRepository
{
    private const string Columns = "ProviderId, ProviderType, Name, ApiKey, Region, IsActive, CreatedAt, UpdatedAt";

    private readonly ISqlConnectionFactory _connections;
    private readonly IDataProtector _protector;

    public GeoProviderRepository(ISqlConnectionFactory connections, IDataProtectionProvider protection)
    {
        _connections = connections;
        _protector = protection.CreateProtector("UserManagement.GeoProviders.ApiKey");
    }

    public async Task<IReadOnlyList<GeoProvider>> GetAllAsync()
    {
        await using var connection = await _connections.OpenAsync();
        await using var command = new SqlCommand($"SELECT {Columns} FROM dbo.GeoProviders ORDER BY IsActive DESC, Name", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var list = new List<GeoProvider>();
        while (await reader.ReadAsync())
        {
            var p = Map(reader);
            p.ApiKeyMasked = Mask(p.ApiKey);
            p.ApiKey = null; // the list never carries keys
            list.Add(p);
        }
        return list;
    }

    public async Task<GeoProvider?> GetAsync(int id)
    {
        await using var connection = await _connections.OpenAsync();
        await using var command = new SqlCommand($"SELECT {Columns} FROM dbo.GeoProviders WHERE ProviderId = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<GeoProvider?> GetActiveAsync()
    {
        await using var connection = await _connections.OpenAsync();
        await using var command = new SqlCommand($"SELECT TOP 1 {Columns} FROM dbo.GeoProviders WHERE IsActive = 1", connection);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<bool> IsEmptyAsync()
    {
        await using var connection = await _connections.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.GeoProviders", connection);
        return (int)(await command.ExecuteScalarAsync())! == 0;
    }

    public async Task<int> CreateAsync(GeoProvider p)
    {
        await using var connection = await _connections.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        if (p.IsActive) await DeactivateAllAsync(connection, tx);

        await using var command = new SqlCommand(
            @"INSERT INTO dbo.GeoProviders (ProviderType, Name, ApiKey, Region, IsActive)
              OUTPUT INSERTED.ProviderId
              VALUES (@type, @name, @key, @region, @active)", connection, tx);
        Bind(command, p);
        command.Parameters.AddWithValue("@key", _protector.Protect(p.ApiKey!.Trim()));
        var id = (int)(await command.ExecuteScalarAsync())!;
        await tx.CommitAsync();
        return id;
    }

    public async Task UpdateAsync(GeoProvider p)
    {
        await using var connection = await _connections.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        if (p.IsActive) await DeactivateAllAsync(connection, tx, except: p.ProviderId);

        var setKey = !string.IsNullOrWhiteSpace(p.ApiKey);
        await using var command = new SqlCommand(
            $@"UPDATE dbo.GeoProviders
               SET ProviderType = @type, Name = @name, Region = @region, IsActive = @active, UpdatedAt = SYSUTCDATETIME()
                   {(setKey ? ", ApiKey = @key" : "")}
               WHERE ProviderId = @id", connection, tx);
        Bind(command, p);
        command.Parameters.AddWithValue("@id", p.ProviderId);
        if (setKey) command.Parameters.AddWithValue("@key", _protector.Protect(p.ApiKey!.Trim()));
        await command.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    public async Task SetActiveAsync(int id, bool active)
    {
        await using var connection = await _connections.OpenAsync();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();
        if (active) await DeactivateAllAsync(connection, tx, except: id);

        await using var command = new SqlCommand(
            "UPDATE dbo.GeoProviders SET IsActive = @active, UpdatedAt = SYSUTCDATETIME() WHERE ProviderId = @id", connection, tx);
        command.Parameters.AddWithValue("@active", active);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = await _connections.OpenAsync();
        await using var command = new SqlCommand("DELETE FROM dbo.GeoProviders WHERE ProviderId = @id", connection);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    // ---------------- helpers ----------------

    private static async Task DeactivateAllAsync(SqlConnection connection, SqlTransaction tx, int? except = null)
    {
        await using var command = new SqlCommand(
            "UPDATE dbo.GeoProviders SET IsActive = 0 WHERE IsActive = 1 AND (@except IS NULL OR ProviderId <> @except)", connection, tx);
        command.Parameters.AddWithValue("@except", (object?)except ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static void Bind(SqlCommand command, GeoProvider p)
    {
        command.Parameters.AddWithValue("@type", p.ProviderType);
        command.Parameters.AddWithValue("@name", p.Name.Trim());
        command.Parameters.AddWithValue("@region", string.IsNullOrWhiteSpace(p.Region) ? DBNull.Value : p.Region.Trim());
        command.Parameters.AddWithValue("@active", p.IsActive);
    }

    private GeoProvider Map(SqlDataReader r)
    {
        string? key;
        try { key = _protector.Unprotect(r.GetString(3)); }
        catch (System.Security.Cryptography.CryptographicException) { key = null; } // key ring lost: treat as "no key", admin re-enters it

        return new GeoProvider
        {
            ProviderId = r.GetInt32(0),
            ProviderType = r.GetString(1),
            Name = r.GetString(2),
            ApiKey = key,
            Region = r.IsDBNull(4) ? null : r.GetString(4),
            IsActive = r.GetBoolean(5),
            CreatedAt = r.GetDateTime(6),
            UpdatedAt = r.IsDBNull(7) ? null : r.GetDateTime(7),
        };
    }

    private static string Mask(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "(unreadable - re-enter key)";
        return key.Length <= 8 ? new string('*', key.Length) : $"{key[..4]}******{key[^4..]}";
    }
}
