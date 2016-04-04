#r "Newtonsoft.Json"

using System;
using Newtonsoft.Json;

public static void Run(string input, out string message, out string completed)
{
    message = null;
    completed = null;

    dynamic inputObject = JsonConvert.DeserializeObject(input);

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