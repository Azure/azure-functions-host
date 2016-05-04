open System
open System.Linq
open System.Net
open System.Net.Http
open System.Threading.Tasks
open System.Diagnostics
open Microsoft.Azure.WebJobs.Host

let Run(input: string, output: byref<string>, log: TraceWriter) =
    log.Info "F# ApiHub trigger function processed a file..."

    output <- input
