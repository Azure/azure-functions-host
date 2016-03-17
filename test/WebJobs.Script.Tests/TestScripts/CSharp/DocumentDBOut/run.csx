using System;

public static void Run(string input, out object item)
{
    item = new
    {
        id = input,
        text = "Hello from C#!"
    };
}