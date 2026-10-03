using System.Data;
using Microsoft.Data.SqlClient;
using UserManagement.Models;

namespace UserManagement.Data;

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync();
    Task<User?> GetByIdAsync(int id);
    Task<int> CreateAsync(User user);
    Task<bool> UpdateAsync(User user);
    Task<bool> DeleteAsync(int id);
    Task<int> DeleteManyAsync(IReadOnlyCollection<int> ids);
}

/// <summary>
/// All SQL is written inline and every value goes through a SqlParameter, so user input is never
/// concatenated into a query string.
/// </summary>
public class UserRepository : IUserRepository
{
    // SQL Server allows at most 2100 parameters per command.
    public const int MaxBatchDelete = 2000;

    private const string SelectColumns = @"
        UserId, FirstName, LastName, DateOfBirth, Phone,
        AddressLine1, AddressLine2, City, StateCode, Zip5, Zip4";

    private readonly ISqlConnectionFactory _db;

    public UserRepository(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<User>> GetAllAsync()
    {
        var sql = $"SELECT {SelectColumns} FROM dbo.Users ORDER BY LastName, FirstName, UserId;";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var users = new List<User>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            users.Add(Map(reader));
        return users;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        var sql = $"SELECT {SelectColumns} FROM dbo.Users WHERE UserId = @UserId;";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = id;

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<int> CreateAsync(User user)
    {
        const string sql = @"
            INSERT INTO dbo.Users
                (FirstName, LastName, DateOfBirth, Phone, AddressLine1, AddressLine2, City, StateCode, Zip5, Zip4)
            OUTPUT INSERTED.UserId
            VALUES
                (@FirstName, @LastName, @DateOfBirth, @Phone, @AddressLine1, @AddressLine2, @City, @StateCode, @Zip5, @Zip4);";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddUserParameters(cmd, user);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<bool> UpdateAsync(User user)
    {
        const string sql = @"
            UPDATE dbo.Users SET
                FirstName    = @FirstName,
                LastName     = @LastName,
                DateOfBirth  = @DateOfBirth,
                Phone        = @Phone,
                AddressLine1 = @AddressLine1,
                AddressLine2 = @AddressLine2,
                City         = @City,
                StateCode    = @StateCode,
                Zip5         = @Zip5,
                Zip4         = @Zip4,
                UpdatedAt    = SYSUTCDATETIME()
            WHERE UserId = @UserId;";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddUserParameters(cmd, user);
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = user.UserId;
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        const string sql = "DELETE FROM dbo.Users WHERE UserId = @UserId;";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = id;
        return await cmd.ExecuteNonQueryAsync() == 1;
    }

    public async Task<int> DeleteManyAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return 0;
        if (ids.Count > MaxBatchDelete)
            throw new ArgumentException($"Cannot delete more than {MaxBatchDelete} users at once.", nameof(ids));

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();

        // Build "@Id0, @Id1, ..." - only parameter names are generated, never values.
        var names = new List<string>(ids.Count);
        var i = 0;
        foreach (var id in ids)
        {
            var name = "@Id" + i++;
            names.Add(name);
            cmd.Parameters.Add(name, SqlDbType.Int).Value = id;
        }

        cmd.CommandText = $"DELETE FROM dbo.Users WHERE UserId IN ({string.Join(", ", names)});";
        return await cmd.ExecuteNonQueryAsync();
    }

    private static void AddUserParameters(SqlCommand cmd, User u)
    {
        cmd.Parameters.Add("@FirstName", SqlDbType.NVarChar, 50).Value = u.FirstName!;
        cmd.Parameters.Add("@LastName", SqlDbType.NVarChar, 50).Value = u.LastName!;
        cmd.Parameters.Add("@DateOfBirth", SqlDbType.Date).Value = u.DateOfBirth!.Value.Date;
        cmd.Parameters.Add("@Phone", SqlDbType.Char, 10).Value = u.Phone!;
        cmd.Parameters.Add("@AddressLine1", SqlDbType.NVarChar, 100).Value = u.AddressLine1!;
        cmd.Parameters.Add("@AddressLine2", SqlDbType.NVarChar, 100).Value = (object?)u.AddressLine2 ?? DBNull.Value;
        cmd.Parameters.Add("@City", SqlDbType.NVarChar, 50).Value = u.City!;
        cmd.Parameters.Add("@StateCode", SqlDbType.Char, 2).Value = u.StateCode!;
        cmd.Parameters.Add("@Zip5", SqlDbType.Char, 5).Value = u.Zip5!;
        cmd.Parameters.Add("@Zip4", SqlDbType.Char, 4).Value = (object?)u.Zip4 ?? DBNull.Value;
    }

    private static User Map(SqlDataReader r) => new()
    {
        UserId = r.GetInt32(0),
        FirstName = r.GetString(1),
        LastName = r.GetString(2),
        DateOfBirth = r.GetDateTime(3),
        Phone = r.GetString(4),
        AddressLine1 = r.GetString(5),
        AddressLine2 = r.IsDBNull(6) ? null : r.GetString(6),
        City = r.GetString(7),
        StateCode = r.GetString(8),
        Zip5 = r.GetString(9),
        Zip4 = r.IsDBNull(10) ? null : r.GetString(10),
    };
}
