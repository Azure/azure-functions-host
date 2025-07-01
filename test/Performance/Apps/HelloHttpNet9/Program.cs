using Microsoft.Data.SqlClient;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var connectionString = args[0];
        Console.WriteLine("Starting the Azure Functions Benchmarks SQL Writer...");
        Console.WriteLine($"Connection String: {connectionString}");

            await WriteResultsToSql(DateTime.UtcNow,
                connectionString,
                "YourSessionC#AdoTask",
                "YourScenario",
                "YourDescription",
                "YourDocument");

            await WriteColdStartToSql(DateTime.UtcNow, connectionString, "foo", "bar", "{}");
       
    }


    private static async Task WriteResultsToSql(
            DateTime utcNow,
            string connectionString,
            string session,
            string scenario,
            string description,
            string document
            )
    {

        var insertCmd =
            $$"""
                INSERT INTO [dbo].HttpBenchmarks
                           ([DateTimeUtc]
                           ,[Session]
                           ,[Scenario]
                           ,[Description]
                           ,[Document])
                     VALUES
                           (@DateTimeUtc
                           ,@Session
                           ,@Scenario
                           ,@Description
                           ,@Document)
                """;

        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var transaction = connection.BeginTransaction();

            try
            {
                var command = new SqlCommand(insertCmd, connection, transaction);
                var p = command.Parameters;
                p.AddWithValue("@DateTimeUtc", utcNow);
                p.AddWithValue("@Session", session);
                p.AddWithValue("@Scenario", scenario ?? "");
                p.AddWithValue("@Description", description ?? "");
                p.AddWithValue("@Document", document);

                await command.ExecuteNonQueryAsync();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                transaction.Dispose();
            }
        }
    }

    private static async Task WriteColdStartToSql(
        DateTime utcNow,
        string connectionString,
        string os,
        string description,
        string document)
    {
        var insertCmd = $$"""
            INSERT INTO ColdStart
                (DateTimeUtc, OS, Description, Document)
            VALUES
                (@DateTimeUtc, @OS, @Description, @Document)
            """;

        using (var connection = new SqlConnection(connectionString))
        {
            await connection.OpenAsync();
            var transaction = connection.BeginTransaction();
            try
            {
                var command = new SqlCommand(insertCmd, connection, transaction);
                var p = command.Parameters;
                p.AddWithValue("@DateTimeUtc", utcNow);
                p.AddWithValue("@OS", os);
                p.AddWithValue("@Description", description);
                p.AddWithValue("@Document", document);

                await command.ExecuteNonQueryAsync();
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                transaction.Dispose();
            }
        }
    }
}