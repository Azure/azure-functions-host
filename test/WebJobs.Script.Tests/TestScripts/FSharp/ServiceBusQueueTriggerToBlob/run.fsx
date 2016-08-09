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

#r "Newtonsoft.Json"
#r "Microsoft.ServiceBus"
#r "System.Runtime.Serialization"

open System
open System.IO
open Newtonsoft.Json
open Microsoft.ServiceBus.Messaging

[<CLIMutable>]
type message = { id: string; count : int }

let Run(input: BrokeredMessage, [<Out>] message: byref<string>, completed: byref<string>) =

    let stream = input.GetBody<Stream>();
    use reader = new StreamReader(stream)
    let json = reader.ReadToEnd();
    let inputObject = JsonConvert.DeserializeObject<message>(json)

    if (inputObject.count < 2) then
        let outputObject = { inputObject with count = inputObject.count + 1 }
        
        message <- JsonConvert.SerializeObject(outputObject)
        completed <- null
    else
        message <- null
        completed <- inputObject.id
