using Microsoft.Data.SqlClient;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("Starting the Azure Functions Benchmarks SQL Writer...");
        try
        {
            await WriteResultsToSql(DateTime.UtcNow,
                @"Server=tcp:azure-functions-benchmarks-dbs1.database.windows.net,1433;Initial Catalog=azure-functions-benchmarks-db;Persist Security Info=False;Authentication=Active Directory Default; User Id=Azure-Functions-Host-Performance-CI-MI; MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;", 
                "YourSession", 
                "YourScenario", 
                "YourDescription", 
                "YourDocument");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            //Environment.Exit(1);
        }
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
}