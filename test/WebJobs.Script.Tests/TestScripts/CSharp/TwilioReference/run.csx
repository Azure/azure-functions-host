#r "Twilio.Api"

using System;
using Microsoft.Azure.WebJobs.Host;
using Twilio;

public static void Run(string input, TraceWriter log)
{
    log.Info(input);
}
