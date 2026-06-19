using System.Threading;

public static async Task Run(string input, TraceWriter log, CancellationToken token)
{
    switch (input)
    {
        case "useToken":
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
            }
            break;
        case "ignoreToken":
            int count = 0;
            while (count < 15)
            {
                count++;
                await Task.Delay(1000);
            }
            break;
        default:
            throw new InvalidOperationException($"'{input}' is an unknown command.");
    }

    log.Info("Done");
}