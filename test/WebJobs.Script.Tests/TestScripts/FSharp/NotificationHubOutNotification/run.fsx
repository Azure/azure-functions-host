open System
open System.Collections.Generic
open Microsoft.Azure.NotificationHubs


let GetTemplateNotification(message: string) =
    let templateProperties = new Dictionary<string, string>()
    templateProperties.["message"] <- message
    new TemplateNotification(templateProperties)

let Run(input: string, notification: byref<Notification>) =
    notification <- GetTemplateNotification(input)
