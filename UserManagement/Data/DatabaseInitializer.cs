using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace UserManagement.Data;

/// <summary>
/// Runs database/CreateDatabase.sql at startup so the app works with just Visual Studio + LocalDB,
/// without SQL Server Management Studio. The script is idempotent, so running it on every start is safe.
/// Turn off with "Database:AutoCreate": false in appsettings.json.
/// </summary>
public static class DatabaseInitializer
{
    private const string ScriptPath = "Database/CreateDatabase.sql";

    public static async Task InitializeAsync(IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetValue("Database:AutoCreate", true)) return;

        var connectionString = configuration.GetConnectionString("UserManagement")
            ?? throw new InvalidOperationException("Connection string 'UserManagement' is missing from appsettings.json.");

        var scriptFile = Path.Combine(AppContext.BaseDirectory, ScriptPath);
        if (!File.Exists(scriptFile))
        {
            logger.LogWarning("Database script not found at {Path}; skipping automatic database setup.", scriptFile);
            return;
        }

        // The script creates the database itself, so connect to master first.
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };

        // Split into batches on lines containing only "GO" (GO is a tool command, not T-SQL).
        var batches = Regex.Split(await File.ReadAllTextAsync(scriptFile), @"^\s*GO\s*;?\s*$",
                                  RegexOptions.Multiline | RegexOptions.IgnoreCase)
                           .Where(b => !string.IsNullOrWhiteSpace(b));

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            foreach (var batch in batches)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync();
            }
        }
        catch (SqlException ex)
        {
            logger.LogCritical(ex,
                "Could not set up the database on '{Server}'. Check ConnectionStrings:UserManagement in appsettings.json " +
                "(for LocalDB, make sure 'SQL Server Express LocalDB' is installed via the Visual Studio Installer).",
                builder.DataSource);
            throw;
        }

        logger.LogInformation("Database is ready.");
    }
}
