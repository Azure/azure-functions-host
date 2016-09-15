using System;

public static object Run(string input)
{
    return new
    {
        id = input,
        text = "Hello from C#!"
    };
}