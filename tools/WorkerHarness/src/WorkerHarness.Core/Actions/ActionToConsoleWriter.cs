using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public class ActionToConsoleWriter : IActionWriter
    {
        private string checkedSymbol = "\u2713";
        private string errorSymbol = "X";

        public IList<MatchingCriteria> Match { get; } = new List<MatchingCriteria>();

        public IDictionary<ValidationContext, bool> ValidationResults { get; } = new Dictionary<ValidationContext, bool>();

        private JsonSerializerOptions options;

        public ActionToConsoleWriter()
        {
            options = new JsonSerializerOptions()
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
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
            foreach (MatchingCriteria match in Match)
            {
                match.ExpectedExpression!.TryEvaluate(out string? expected);
                Console.WriteLine($"{checkedSymbol} {match.Query}: {expected}");
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
            Console.WriteLine($"{JsonSerializer.Serialize(message, options)}");
        }

        public void WriteUnmatchedMessages(IncomingMessage message)
        {
            ConsoleColor currentForeground = Console.ForegroundColor;

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"-Never received an Grpc message that");

            Console.WriteLine($"\nmatches:");
            Console.WriteLine($"========");
            MatchingCriteria match = message.Match!;
            match.ExpectedExpression!.TryEvaluate(out string? expected);
            Console.WriteLine($"{errorSymbol} {match.Query}: {expected}");

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
            Console.WriteLine($"\n{new String('-', 100)}");
        }
    }
}
