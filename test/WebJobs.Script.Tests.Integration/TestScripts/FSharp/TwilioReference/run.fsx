open System
open Microsoft.Azure.WebJobs.Host

let Run(input: string, log: TraceWriter) =   
    log.Info(input)