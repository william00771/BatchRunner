using BatchRunner.Util;
using Microsoft.Data.SqlClient;

namespace BatchRunner.Services;

public class SqlJobExecutor
{
    private readonly string _connectionString;

    public SqlJobExecutor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task ExecuteSqlCommandAsync(string commandText)
    {
        using var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync();
            using var cmd = new SqlCommand(commandText, connection)
            {
                CommandTimeout = 900
            };
            await cmd.ExecuteNonQueryAsync();
        }
        catch (SqlException sqlEx)
        {
            Logger.Log($"SQL ERROR executing: {commandText}");
            foreach (SqlError error in sqlEx.Errors)
                Logger.Log($"SQL ERROR {error.Number}: {error.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Logger.Log($"GENERAL ERROR executing SQL command: {commandText}");
            Logger.Log($"Exception: {ex.Message}");
            throw;
        }
    }
}
