public static string Run(string input, ILogger log)
{
    log.LogInformation($"C# ApiHub trigger function processed a file...");
    return input;
}