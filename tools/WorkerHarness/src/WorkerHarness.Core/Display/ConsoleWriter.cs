using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace WorkerHarness.Core
{
    public class ConsoleWriter : IActionWriter
    {
        public IList<MatchingContext> Match { get; } = new List<MatchingContext>();

        public IDictionary<ValidationContext, bool> ValidationResults { get; } = new Dictionary<ValidationContext, bool>();

        private readonly JsonSerializerOptions options;

        public ConsoleWriter()
        {
            options = new JsonSerializerOptions()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            options.Converters.Add(new JsonStringEnumConverter());
        }

        public void WriteActionName(string name)
        {
            Console.Write($"\nAction: {name}");
            Console.Write($"\n{new string('=', 100)}");
        }

        public void WriteMatchedMessage(StreamingMessage message)
        {
            Console.Write($"\nReceiving a {message.ContentCase} message that fulfills the matching criteria ... ");
            WriteConsoleInGreen("Success");

            bool allValidated = true;

            foreach (KeyValuePair<ValidationContext, bool> validation in ValidationResults)
            {
                var validationContext = validation.Key;
                var validated = validation.Value;
                var validationType = string.Equals(validationContext.Type, "string", StringComparison.OrdinalIgnoreCase) ? "string value" : "regex pattern";
                Console.Write($"\nValidating the property {validationContext.Query} matches {validationType} \"{validationContext.Expected}\" ... ");

                if (validated)
                {
                    WriteConsoleInGreen("Success");
                }
                else
                {
                    WriteConsoleInRed("Error");
                    allValidated = false;
                }
            }

            if (!allValidated)
            {
                Console.Write("\nMessage payload:");
                Console.Write($"\n{JsonSerializer.Serialize(message, options)}");
            }

            Match.Clear();
            ValidationResults.Clear();
        }

        public void WriteSentMessage(StreamingMessage message)
        {
            Console.Write($"\nSending a {message.ContentCase} message ... ");
            WriteConsoleInGreen("Success");
        }

        public void WriteUnmatchedMessages(IncomingMessage message)
        {
            Console.Write($"\nReceiving a {message.ContentCase} message that fulfills the matching criteria ... ");
            WriteConsoleInRed("Error");

            Console.Write($"\nThe matching criteria:");
            foreach (var match in message.Match)
            {
                Console.Write($"\n{match.Query}: {match.Expected}");
            }
        }

        public void WriteActionEnding()
        {
            Console.WriteLine($"\n{new string('-', 100)}");
        }

        private void WriteConsoleInGreen(string message)
        {
            ConsoleColor currentForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(message);
            Console.ForegroundColor = currentForeground;
        }

        private void WriteConsoleInRed(string message)
        {
            ConsoleColor currentForeground = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(message);
            Console.ForegroundColor = currentForeground;
        }
    }
}
