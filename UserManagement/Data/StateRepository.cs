using System.Data;
using UserManagement.Models;

namespace UserManagement.Data;

public interface IStateRepository
{
    Task<IReadOnlyList<State>> GetAllAsync();
    Task<bool> ExistsAsync(string stateCode);
}

public class StateRepository : IStateRepository
{
    private readonly ISqlConnectionFactory _db;

    public StateRepository(ISqlConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<State>> GetAllAsync()
    {
        const string sql = "SELECT StateCode, StateName FROM dbo.States ORDER BY StateName;";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var states = new List<State>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            states.Add(new State(reader.GetString(0), reader.GetString(1)));
        return states;
    }

    public async Task<bool> ExistsAsync(string stateCode)
    {
        const string sql = "SELECT COUNT(1) FROM dbo.States WHERE StateCode = @StateCode;";

        await using var conn = await _db.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add("@StateCode", SqlDbType.Char, 2).Value = stateCode;
        return (int)(await cmd.ExecuteScalarAsync())! > 0;
    }
}
