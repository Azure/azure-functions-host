#r "Newtonsoft.Json"

open System
open Newtonsoft.Json

type message = { id: string; count : int }

let Run(input: string, message: byref<string>, completed: byref<string>) =
    message <- null
    completed <- null

    let inputObject = JsonConvert.DeserializeObject<message>(input)

    if (inputObject.count < 2) then
        let outputObject = { inputObject with count = inputObject.count + 1 }
        
        message <- JsonConvert.SerializeObject(outputObject)
    else
        completed <- inputObject.id
