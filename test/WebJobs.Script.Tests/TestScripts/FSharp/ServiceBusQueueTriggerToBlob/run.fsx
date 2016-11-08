//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../src/WebJobs.Script.Host/bin/Debug"
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

type Message = 
    { id: string; count : int }

// leaving this byref example on purpose so we have test coverage
// this code could be simplified by using a $return mapping instead
let Run(input: BrokeredMessage, message: byref<string>, completed: byref<string>) =
    let stream = input.GetBody<Stream>();
    use reader = new StreamReader(stream)
    let json = reader.ReadToEnd();
    let obj = JsonConvert.DeserializeObject<Message>(json)

    if (obj.count < 2) then
        let newObj = { obj with count = obj.count + 1 }
        
        message <- JsonConvert.SerializeObject(newObj)
        completed <- null
    else
        message <- null
        completed <- obj.id
