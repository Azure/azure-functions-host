open System
open System.Linq
open System.Threading.Tasks
open System.IO
open Microsoft.Azure.WebJobs.Host

let Run(input: string, log: TraceWriter) =
    log.Info (sprintf "F# Blob trigger function processed a work item. Item=%s" input)
