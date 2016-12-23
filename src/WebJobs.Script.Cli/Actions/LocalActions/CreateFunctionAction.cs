using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.LocalActions
{
    [Action(Name = "create", Context = Context.Function, HelpText = "Create a new Function from a template, using the Yeoman generator")]
    [Action(Name = "new", Context = Context.Function, HelpText = "Create a new Function from a template, using the Yeoman generator")]
    [Action(Name = "new")]
    class CreateFunctionAction : BaseAction
    {
        private readonly ITemplatesManager _templatesManager;

        public CreateFunctionAction(ITemplatesManager templatesManager)
        {
            _templatesManager = templatesManager;
        }

        public async override Task RunAsync()
        {
            var templates = await _templatesManager.Templates;

            ColoredConsole.Write("Select a language: ");
            var language = DisplaySelectionWizard(templates.Select(t => t.Metadata.Language).Distinct());
            ColoredConsole.WriteLine(TitleColor(language));

            ColoredConsole.Write("Select a template: ");
            var name = DisplaySelectionWizard(templates.Where(t => t.Metadata.Language == language).Select(t => t.Metadata.Name).Distinct());
            ColoredConsole.WriteLine(TitleColor(name));

            var template = templates.First(t => t.Metadata.Name == name && t.Metadata.Language == language);

            ColoredConsole.Write($"Function name: [{template.Metadata.DefaultFunctionName}] ");
            var functionName = Console.ReadLine();
            functionName = string.IsNullOrEmpty(functionName) ? template.Metadata.DefaultFunctionName : functionName;

            await _templatesManager.Deploy(functionName, template);
        }

        private static T DisplaySelectionWizard<T>(IEnumerable<T> options)
        {
            var current = 0;
            var next = current;
            var leftPos = Console.CursorLeft;
            var topPos = Console.CursorTop;
            var optionsArray = options.ToArray();

            ColoredConsole.WriteLine();
            for (var i = 0; i < optionsArray.Length; i++)
            {
                if (i == current)
                {
                    ColoredConsole.WriteLine(TitleColor(optionsArray[i].ToString()));
                }
                else
                {
                    ColoredConsole.WriteLine(optionsArray[i].ToString());
                }
            }

            Console.CursorVisible = false;
            while (true)
            {
                if (current != next)
                {
                    for (var i = 0; i < optionsArray.Length; i++)
                    {
                        if (i == current)
                        {
                            Console.SetCursorPosition(0, topPos + i + 1);
                            ColoredConsole.WriteLine($"\r{optionsArray[i].ToString()}");
                        }
                        else if (i == next)
                        {
                            Console.SetCursorPosition(0, topPos + i + 1);
                            ColoredConsole.WriteLine($"\r{TitleColor(optionsArray[i].ToString())}");
                        }
                    }
                    current = next;
                }
                Console.SetCursorPosition(0, topPos + current - 1);
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow)
                {
                    next = current == 0 ? optionsArray.Length - 1 : current - 1;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    next = current == optionsArray.Length - 1 ? 0 : current + 1;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    ClearConsole(topPos + 1, optionsArray.Length);
                    Console.SetCursorPosition(leftPos, topPos);
                    Console.CursorVisible = true;
                    return optionsArray[current];
                }
            }
        }

        private static void ClearConsole(int topPos, int length)
        {
            Console.SetCursorPosition(0, topPos);
            for (var i = 0; i < length * 2; i++)
            {
                ColoredConsole.WriteLine(new string(' ', Console.BufferWidth - 1));
            }
        }
    }
}
