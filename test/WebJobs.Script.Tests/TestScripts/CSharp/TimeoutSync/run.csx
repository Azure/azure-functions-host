using System.Threading;
using Microsoft.Azure.WebJobs.Host;

public static void Run(string inputData, TraceWriter log)
{
    log.Info(inputData);

    int count = 0;
    while (count < 10)
    {
        count++;
        Thread.Sleep(1000);
    }
}