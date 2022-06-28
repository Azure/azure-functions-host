using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace WorkerHarness.Core
{
    public class ConsoleWriter : IActionWriter
    {
        public void WriteSuccess(string message)
        {
            ConsoleColor currentForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ForegroundColor = currentForeground;
        }

        public void WriteError(string message)
        {
            ConsoleColor currentForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = currentForeground;
        }

        public void WriteInformation(string message)
        {
            Console.WriteLine(message);
        }
    }
}
