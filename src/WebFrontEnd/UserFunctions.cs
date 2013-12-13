using SimpleBatch;
using System;
using System.IO;

class UserFunctions
{
    public static void CopyXYZ(
        [BlobInput(@"test8\{name}.input.txt")] TextReader input,
        [BlobOutput(@"test8\{name}.output.txt")] TextWriter output,
        string name)
    {
        Console.WriteLine("Hi from Kudu!");
        Console.WriteLine(name);
        string content = input.ReadToEnd();
        output.Write(content);
    }

    // Manually kick this off, should chain to CopyXYZ
    public static void StartIt(
        [BlobOutput(@"test8\bongo.input.txt")] TextWriter output)
    {        
        output.Write("test!");
    }

    [Description("Test function")]
    public static void WriteToMyself()
    {
        Console.WriteLine("Hello");
    }
}