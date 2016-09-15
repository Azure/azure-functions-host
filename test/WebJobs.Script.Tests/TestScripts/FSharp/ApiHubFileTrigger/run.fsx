//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "System.Net.Http"

open System
open Microsoft.Azure.WebJobs.Host

let Run(input: string, log: TraceWriter) =
    log.Info "F# ApiHub trigger function processed a file..."
    input
