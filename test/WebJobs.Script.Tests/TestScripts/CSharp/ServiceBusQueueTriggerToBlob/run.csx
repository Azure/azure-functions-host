#r "Newtonsoft.Json"
#r "Microsoft.ServiceBus"

using System;
using System.IO;
using Newtonsoft.Json;
using Microsoft.ServiceBus.Messaging;

public static void Run(BrokeredMessage input, out string message, out string completed)
{
    message = null;
    completed = null;

    Stream stream = input.GetBody<Stream>();
    string json = string.Empty;
    using (StreamReader reader = new StreamReader(stream))
    {
        json = reader.ReadToEnd();
    }

    dynamic inputObject = JsonConvert.DeserializeObject(json);

    if (inputObject.count < 2)
    {
        inputObject.count += 1;
        message = inputObject.ToString();
    }
    else
    {
        completed = inputObject.id;
    }
}