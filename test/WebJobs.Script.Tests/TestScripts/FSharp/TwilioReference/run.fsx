//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../src/WebJobs.Script.Host/bin/Debug"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

#r "Twilio.Api"

open Twilio

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System
open Microsoft.Azure.WebJobs.Host

let Run(input: string, log: TraceWriter) =   
    log.Info(input)