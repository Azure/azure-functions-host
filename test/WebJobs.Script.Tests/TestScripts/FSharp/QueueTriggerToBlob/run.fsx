open System
open System.IO

let input = System.Console.In.ReadLine()
let message = sprintf "F# script processed queue message '%s'" input
System.Console.Out.WriteLine(message)

let output = System.Environment.GetEnvironmentVariable("output");
File.WriteAllText(output, input)