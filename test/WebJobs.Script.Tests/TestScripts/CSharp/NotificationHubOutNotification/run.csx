#r "Microsoft.Azure.NotificationHubs"

using System;
using System.Collections.Generic;
using Microsoft.Azure.NotificationHubs;

public static void Run(string input, out Notification notification)
{
    notification = GetTemplateNotification(input);
}
private static TemplateNotification GetTemplateNotification(string message)
{
    Dictionary<string, string> templateProperties = new Dictionary<string, string>();
    templateProperties["message"] = message;
    return new TemplateNotification(templateProperties);
}