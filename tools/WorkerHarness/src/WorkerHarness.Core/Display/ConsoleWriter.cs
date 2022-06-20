using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace WorkerHarness.Core
{
    public class ConsoleWriter : IActionWriter
    {
        private readonly string checkedSymbol = "\u2713";
        private readonly string errorSymbol = "X";

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
            Console.WriteLine($"\nAction: {name}");
        }

        public void WriteMatchedMessage(StreamingMessage message)
        {
            Console.WriteLine($"\n- Received a {message.ContentCase} message");

            Console.WriteLine($"\nmatches:");
            Console.WriteLine($"========");
            foreach (MatchingContext match in Match)
            {
                match.TryEvaluate(out string? evaluated);
                Console.WriteLine($"{checkedSymbol} {match.Query}: {evaluated}");
            }

            Console.WriteLine($"\nvalidates:");
            Console.WriteLine($"==========");
            foreach (KeyValuePair<ValidationContext, bool> validation in ValidationResults)
            {
                ConsoleColor currentForeground = Console.ForegroundColor;
                string displayedSymbol;
                if (validation.Value)
                {
                    displayedSymbol = checkedSymbol;

                }
                else
                {
                    displayedSymbol = errorSymbol;
                    Console.ForegroundColor = ConsoleColor.Red;
                }

                ValidationContext validationContext = validation.Key;
                Console.WriteLine($"{displayedSymbol} {validationContext.Query} : {validationContext.Expected}");

                Console.ForegroundColor = currentForeground;
            }

            Console.WriteLine($"\nPayload:");
            Console.WriteLine($"========");
            Console.WriteLine($"{JsonSerializer.Serialize(message, options)}");

            Match.Clear();
            ValidationResults.Clear();
        }

        public void WriteSentMessage(StreamingMessage message)
        {
            Console.WriteLine($"\n- Sent a {message.ContentCase} message");
            Console.WriteLine($"\nPayload:");
            Console.WriteLine($"========");
            Console.WriteLine(JsonSerializer.Serialize(message, options));
        }

        public void WriteUnmatchedMessages(IncomingMessage message)
        {
            ConsoleColor currentForeground = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"-Never received an Grpc message that");

            Console.WriteLine($"\nmatches:");
            Console.WriteLine($"========");
            foreach (var match in message.Match)
            {
                match.TryEvaluate(out string? expected);
                Console.WriteLine($"{errorSymbol} {match.Query}: {expected}");
            }

            Console.WriteLine($"\nvalidates:");
            Console.WriteLine($"==========");
            foreach (var validation in ValidationResults)
            {
                ValidationContext validationContext = validation.Key;
                Console.WriteLine($"{errorSymbol} {validationContext.Query} : {validationContext.Expected}");
            }

            Console.ForegroundColor = currentForeground;
        }

        public void WriteActionEnding()
        {
            Console.WriteLine($"\n{new string('-', 100)}");
        }
    }
}
