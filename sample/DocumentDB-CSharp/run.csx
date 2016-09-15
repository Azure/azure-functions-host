using System;

public static object Run(string input, out object newItem)
{
    return new 
    {
        text = "Hello from C#! " + input
    }; 
}