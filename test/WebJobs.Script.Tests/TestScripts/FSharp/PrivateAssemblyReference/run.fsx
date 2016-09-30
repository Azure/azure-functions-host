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

#r "TestDependency.dll"
#r "System.Threading.Tasks"
#r "System.Net.Http"

open System
open System.Net.Http
open TestDependency



let Run(req: HttpRequestMessage, log: TraceWriter) =
    req.Properties.["DependencyOutput"] <- (new TestClass()).GetValue();
