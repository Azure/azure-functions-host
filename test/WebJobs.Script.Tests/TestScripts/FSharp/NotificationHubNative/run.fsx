//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System;
let Run(input: string, wnsToastPayload: byref<string>) =
    wnsToastPayload <- "<toast><visual><binding template=\"ToastText01\"><text id=\"1\">Test message from C#</text></binding></visual></toast>";
