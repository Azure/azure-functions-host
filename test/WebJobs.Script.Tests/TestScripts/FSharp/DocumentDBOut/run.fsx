//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
open System.Runtime.InteropServices
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

open System

[<CLIMutable>]
type OutputData = { id: string; text: string }

let Run(input: string , [<Out>] item: byref<obj>) =
    item <- { id = input; text = "Hello from C#!" }
