//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open System.Linq
open System.Threading.Tasks
open System.IO
open Microsoft.Azure.WebJobs.Host

let Run(input: string, log: TraceWriter) =
    log.Info (sprintf "F# Blob trigger function processed a work item. Item=%s" input)
