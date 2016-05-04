open System

type Out = { id: string; text: string }

let Run(input: string , item: byref<obj>) =
    item <- { id = input; text = "Hello from C#!" }
