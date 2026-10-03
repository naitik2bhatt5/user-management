using Microsoft.Data.SqlClient;

namespace UserManagement.Data;

public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenAsync();
}

public class SqlConnectionFactory : ISqlConnectionFactory
{
    private readonly string _connectionString;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("UserManagement")
            ?? throw new InvalidOperationException("Connection string 'UserManagement' is missing from appsettings.json.");
    }

    public async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
