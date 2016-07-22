//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host

let Run(input: TimerInfo, log: TraceWriter ) =
    log.Verbose("C# Timer trigger function executed.");
