open System
open System.IO

let inputPath = Environment.GetEnvironmentVariable("input")
let input = File.ReadAllText(inputPath)

let message = sprintf "F# script processed queue message '%s'" input
Console.Out.WriteLine(message)

let output = Environment.GetEnvironmentVariable("output");
File.WriteAllText(output, input)