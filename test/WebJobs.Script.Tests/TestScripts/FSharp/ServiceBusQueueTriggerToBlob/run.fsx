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
#r "Microsoft.ServiceBus"
#r "System.Runtime.Serialization"

open System
open System.IO
open Newtonsoft.Json
open Microsoft.ServiceBus.Messaging

type Message = 
    { id: string; count : int }

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

(*
type Message() = 
    member val id : string = "" with get,set
    member val count : int = 0 with get,set

type Message2 = 
    { id: string; count : int }

[<CLIMutable>]
type Message3 = 
    { id: string; count : int }

[<CLIMutable>]
type Message4 = 
    { mutable id: string; mutable count : int }

let obj = JsonConvert.DeserializeObject<Message>("{id: 'abc', count: 1}")
let obj2 = JsonConvert.DeserializeObject<Message2>("{id: 'abc', count: 1}")
let obj3 = JsonConvert.DeserializeObject<Message3>("{id: 'abc', count: 1}")
let obj4 = JsonConvert.DeserializeObject<Message4>("{id: 'abc', count: 1}")

obj.id
obj.count

JsonConvert.SerializeObject(obj)
JsonConvert.SerializeObject(obj2)
JsonConvert.SerializeObject(obj3)
JsonConvert.SerializeObject(obj4) // Note this gives "{"id@":"abc","count@":1,"id":"abc","count":1}" which is not the right JSON

 *)
