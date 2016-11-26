#r "Newtonsoft.Json"
#r "Microsoft.AspNet.WebHooks.Receivers.Azure"

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNet.WebHooks;

public static JObject Run(AzureAlertNotification payload)
{
    payload.Status = "Unresolved";
    return JObject.FromObject(payload);
}