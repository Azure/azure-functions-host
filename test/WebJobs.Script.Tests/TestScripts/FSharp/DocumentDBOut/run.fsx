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

//type OutputData() = 
//    member val id: string = "" with get,set
//    member val text: string  = "" with get,set

type OuputData =
    { id : string
      text : string }

let Run(input: string , [<Out>] item: byref<obj>) =
    item <- { id = input; text = "Hello from F#!" } 
    //item <- OutputData(id = input, text = "Hello from F#!")
