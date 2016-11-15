open System
open Microsoft.Azure.WebJobs.Host

let Run (input: string, log: TraceWriter) =
    """{"message":"Hello from F# ! ","location":"fsharp.org"}"""
