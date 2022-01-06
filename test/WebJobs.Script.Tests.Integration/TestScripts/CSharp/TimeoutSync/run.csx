using System.Threading;

public static void Run(string inputData, ILogger log)
{
    log.LogInformation(inputData);

    int count = 0;
    while (count < 10)
    {
        count++;
        Thread.Sleep(1000);
    }
}