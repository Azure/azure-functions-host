using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using Microsoft.Azure.WebJobs.Script;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions
{
    class HelpAction : BaseAction
    {
        private readonly string _context;
        private readonly string _subContext;
        private readonly IAction _action;
        private readonly ICommandLineParserResult _parseResult;
        private readonly IEnumerable<ActionType> _actionTypes;

        public HelpAction(IEnumerable<TypeAttributePair> actions, string context = null, string subContext = null)
        {
            _context = context;
            _subContext = subContext;
            _actionTypes = actions
                .Where(a => a.Attribute.ShowInHelp)
                .Select(a => a.Type)
                .Distinct()
                .Select(type =>
                {
                    var attributes = type.GetCustomAttributes<ActionAttribute>();
                    return new ActionType
                    {
                        Type = type,
                        Contexts = attributes.Select(a => a.Context),
                        SubContexts = attributes.Select(a => a.SubContext),
                        Names = attributes.Select(a => a.Name)
                    };
                });
        }

        public HelpAction(IEnumerable<TypeAttributePair> actions, IAction action, ICommandLineParserResult parseResult) : this(actions)
        {
            _action = action;
            _parseResult = parseResult;
        }

        public override Task RunAsync()
        {
            Utilities.PrintLogo();
            if (!string.IsNullOrEmpty(_context) || !string.IsNullOrEmpty(_subContext))
            {
                var context = Context.None;
                var subContext = Context.None;

                if (!string.IsNullOrEmpty(_context) && !Enum.TryParse(_context, true, out context))
                {
                    ColoredConsole.Error.WriteLine($"Error: unknown argument {_context}");
                    return Task.CompletedTask;
                }

                if (!string.IsNullOrEmpty(_subContext) && !Enum.TryParse(_subContext, true, out subContext))
                {
                    ColoredConsole.Error.WriteLine($"Error: unknown argument {_subContext} in {context.ToLowerCaseString()} Context");
                    return Task.CompletedTask;
                }

                DisplayContextHelp(context, subContext);
            }
            else if (_action != null && _parseResult != null)
            {
                DisplayActionHelp();
            }
            else
            {
                DisplayGeneralHelp();
            }
            return Task.CompletedTask;
        }

        private void DisplayContextHelp(Context context, Context subContext)
        {
            if (subContext == Context.None)
            {
                ColoredConsole
                .WriteLine($"Usage: func {context.ToLowerCaseString()} [context] <action> [-/--options]")
                .WriteLine();
                var contexts = _actionTypes
                    .Where(a => a.Contexts.Contains(context))
                    .Select(a => a.SubContexts)
                    .SelectMany(c => c)
                    .Where(c => c != Context.None)
                    .Distinct()
                    .OrderBy(c => c.ToLowerCaseString());
                DisplayContextsHelp(contexts);
            }
            else
            {
                ColoredConsole
                .WriteLine($"Usage: func {context.ToLowerCaseString()} {subContext.ToLowerCaseString()} <action> [-/--options]")
                .WriteLine();
            }

            var actions = _actionTypes
                .Where(a => a.Contexts.Contains(context))
                .Where(a => a.SubContexts.Contains(subContext));
            DisplayActionsHelp(actions);
        }

        private void DisplayActionHelp()
        {
            ColoredConsole.WriteLine(_parseResult.ErrorText);
        }

        private void DisplayGeneralHelp()
        {
            var contexts = _actionTypes
                .Select(a => a.Contexts)
                .SelectMany(c => c)
                .Where(c => c != Context.None)
                .Distinct()
                .OrderBy(c => c.ToLowerCaseString());
            ColoredConsole
                .WriteLine($"Azure Functions Cli ({Constants.CliVersion})")
                .WriteLine($"Function Runtime Version: {ScriptHost.Version}")
                .WriteLine("Usage: func [context] [context] <action> [-/--options]")
                .WriteLine();
            DisplayContextsHelp(contexts);
            var actions = _actionTypes.Where(a => a.Contexts.Contains(Context.None));
            DisplayActionsHelp(actions);
        }

        private static void DisplayContextsHelp(IEnumerable<Context> contexts)
        {
            if (contexts.Any())
            {
                var longestName = contexts.Select(c => c.ToLowerCaseString()).Max(n => n.Length);
                ColoredConsole.WriteLine("Contexts:");
                foreach (var context in contexts)
                {
                    ColoredConsole.WriteLine(string.Format($"{{0, {-longestName}}}  {{1}}", context.ToLowerCaseString(), GetDescriptionOfContext(context)));
                }
                ColoredConsole.WriteLine();
            }
        }

        private static void DisplayActionsHelp(IEnumerable<ActionType> actions)
        {
            if (actions.Any())
            {
                ColoredConsole.WriteLine("Actions: ");
                var longestName = actions.Select(a => a.Names).SelectMany(n => n).Max(n => n.Length);
                foreach (var action in actions)
                {
                    ColoredConsole.WriteLine(GetActionHelp(action, longestName));
                }
                ColoredConsole.WriteLine();
            }
        }

        private static string GetActionHelp(ActionType action, int formattingSpace)
        {
            var name = action.Names.First();
            var aliases = action.Names.Distinct().Count() > 1
                ? action.Names.Distinct().Aggregate((a, b) => string.Join(", ", a, b))
                : string.Empty;
            var description = action.Type.GetCustomAttributes<ActionAttribute>()?.FirstOrDefault()?.HelpText;
            return string.Format($"{{0, {-formattingSpace}}}  {{1}} {(aliases.Any() ? "Aliases:" : "")} {{2}}", name, description, aliases);
        }

        // http://stackoverflow.com/a/1799401
        private static string GetDescriptionOfContext(Context context)
        {
            var memInfo = context.GetType().GetMember(context.ToString()).FirstOrDefault();
            return memInfo?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        }
    }
}
