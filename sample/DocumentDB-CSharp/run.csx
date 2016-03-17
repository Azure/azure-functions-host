using System;

public static void Run(string input, out object newItem)
{
    newItem = new 
    {
        text = "Hello from C#! " + input
    }; 
}