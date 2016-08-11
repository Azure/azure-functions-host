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

type OutputData =
    { id : string
      text : string }

let Run(input: string , item: byref<OutputData>) =
    item <- { id = input; text = "Hello from F#!" } 

// Note: you can also use a POCO object:
//
//type OutputData() = 
//    member val id: string = "" with get,set
//    member val text: string  = "" with get,set

    //item <- OutputData(id = input, text = "Hello from F#!")
