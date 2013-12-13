using System;
using System.IO;
using SimpleBatch; // Add reference to SimpleBatch.dll (eventually from Nuget)

// App will run in 64-bit, so ensure build doesn't mandate x86.
namespace ConsoleApplication
{
    public class Program
    {
        static void Main(string[] args)
        {
            CoolDemoFunc(Console.In, "test", Console.Out);
        }

        public static void CoolDemoFunc(
            [BlobInput(@"simplebatch-demo-input\{name}.txt")] TextReader reader,
            string name,
            [BlobOutput(@"simplebatch-demo-output\{name}.txt")] TextWriter output)
        {
            string content = reader.ReadLine();

            // Console output is captured as logging
            Console.WriteLine("Logging to console output.");
            Console.WriteLine(content);

            // Write back to the output blob
            output.WriteLine("Writing content back to a blob:" + content);
            output.WriteLine(name);
        }
    }
}

