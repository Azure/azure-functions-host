open System

let Run(input: string, messageProperties: byref<string>) =
    messageProperties <- """{"message":"Hello from F# ! ","location":"Cambridge"}""";
