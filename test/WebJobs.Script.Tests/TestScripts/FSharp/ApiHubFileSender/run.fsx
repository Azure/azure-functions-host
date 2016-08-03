//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../bin/Binaries/WebJobs.Script.Host"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 


open System
open System.Runtime.InteropServices
open Microsoft.Azure.WebJobs.Host

// Had to add the <Out> attribute here to make the bindings happy, without it, the type validation fails
// at indexing time. Need to see if there's something we can do...
let Run(input: string, [<Out>] item: byref<string>, log: TraceWriter ) =
    log.Info "F# ApiHub trigger function processed a file..."
    item <- input

