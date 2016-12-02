open System

type OutputData =
    { id : string
      text : string }

let Run(input: string) = 
    { id = input; text = "Hello from F#!" }
