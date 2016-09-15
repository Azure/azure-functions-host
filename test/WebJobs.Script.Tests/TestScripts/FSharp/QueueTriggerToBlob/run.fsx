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
open Microsoft.Azure.WebJobs.Host

[<CLIMutable>]
type WorkItem = { Id : string }

let Run(input: WorkItem, log: TraceWriter) = 
    let json = String.Format("{{ \"id\": \"{0}\" }}", input.Id)    
    log.Info(sprintf "F# script processed queue message '%s'" json)
    json