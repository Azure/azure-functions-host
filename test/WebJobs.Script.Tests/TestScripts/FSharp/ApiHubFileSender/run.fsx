
open System

let Run(input: string, item: byref<string>, log: TraceWriter ) =
    log.Info "F# ApiHub trigger function processed a file..."

    item <- input

