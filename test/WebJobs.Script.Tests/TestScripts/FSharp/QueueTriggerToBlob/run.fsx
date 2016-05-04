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

type WorkItem() =
    member val Id = "" with get, set

let Run(input: WorkItem, [<System.Runtime.InteropServices.OutAttribute>] output: byref<string>, log: TraceWriter) = 
    let json = String.Format("{{ \"id\": \"{0}\" }}", input.Id)    
    log.Info(sprintf "F# script processed queue message '%s'" json)
    output <- json