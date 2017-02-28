open System
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

let Run(timerInfo: TimerInfo, log: TraceWriter ) =
    log.Verbose("F# Timer trigger function executed.");
