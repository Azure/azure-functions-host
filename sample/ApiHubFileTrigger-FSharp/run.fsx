//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the body of the function 

open System
open Microsoft.Azure.WebJobs.Host

let Run (input: string, log: TraceWriter) =
    log.Verbose(sprintf "F# ApiHub trigger function processed a file...");
    input

