open System
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type WorkItem = { Id : string }

let Run(input: WorkItem, log: TraceWriter) = 
    let json = String.Format("{{ \"id\": \"{0}\" }}", input.Id)    
    log.Info(sprintf "F# script processed queue message '%s'" json)
    json