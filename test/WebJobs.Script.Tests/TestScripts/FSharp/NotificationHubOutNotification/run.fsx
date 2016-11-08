//----------------------------------------------------------------------------------------
// This prelude allows scripts to be edited in Visual Studio or another F# editing environment 

#if !COMPILED
#I "../../../../../src/WebJobs.Script.Host/bin/Debug"
#r "Microsoft.Azure.WebJobs.Host.dll"
#r "Microsoft.Azure.WebJobs.Extensions.dll"
#endif

//----------------------------------------------------------------------------------------
// This is the implementation of the function 

#r "Microsoft.Azure.NotificationHubs"

open System
open System.Collections.Generic
open Microsoft.Azure.NotificationHubs

let GetTemplateNotification(message: string) =
    let templateProperties = new Dictionary<string, string>()
    templateProperties.["message"] <- message
    new TemplateNotification(templateProperties)

let Run(input: string) = GetTemplateNotification(input)
