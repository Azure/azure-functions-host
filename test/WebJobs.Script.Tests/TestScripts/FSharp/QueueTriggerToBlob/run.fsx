open System
open System.Linq
open System.Threading.Tasks
open System.IO
open Microsoft.Azure.WebJobs.Host

type WorkItem() =
    member val Id = "" with get, set

let Run(input: WorkItem, output: byref<string>, log: TraceWriter) = 
    let json = String.Format("{{ \"id\": \"{0}\" }}", input.Id)
    let message = sprintf "F# script processed queue message '%s'" json
    output <- json