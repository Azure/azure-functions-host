//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "Newtonsoft.Json"

open System
open System.Runtime.InteropServices
open Newtonsoft.Json

type message = { id: string; count : int }

let Run(input: string, [<Out>] message: byref<string>, completed: byref<string>) =
    message <- null
    completed <- null

    let inputObject = JsonConvert.DeserializeObject<message>(input)

    if (inputObject.count < 2) then
        let outputObject = { inputObject with count = inputObject.count + 1 }
        
        message <- JsonConvert.SerializeObject(outputObject)
    else
        completed <- inputObject.id
