using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Colors.Net;
using WebJobs.Script.Cli.Actions;
using WebJobs.Script.Cli.Common;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli
{
    class ConsoleApp
    {
        private readonly IContainer _container;
        private readonly string[] _args;
        private readonly IEnumerable<TypeAttributePair> _actionAttributes;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static void Run<T>(string[] args, IContainer container)
        {
            Task.Run(() => RunAsync<T>(args, container)).Wait();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static async Task RunAsync<T>(string[] args, IContainer container)
        {
            var app = new ConsoleApp(args, typeof(T).Assembly, container);
            var action = app.Parse();
            if (action != null)
            {
                try
                {
                    await action.RunAsync();
                }
                catch (Exception ex)
                {
                    ColoredConsole.Error.WriteLine(ex.ToString());
                }
            }
        }

        public static bool RelaunchSelfElevated(IAction action, out string errors)
        {
            errors = string.Empty;
            var attribute = action.GetType().GetCustomAttribute<ActionAttribute>();
            if (attribute != null)
            {
                Func<Context, string> getContext = c => c == Context.None ? string.Empty : c.ToString();
                var context = getContext(attribute.Context);
                var subContext = getContext(attribute.Context);
                var name = attribute.Name;
                var args = action
                    .ParseArgs(Array.Empty<string>())
                    .UnMatchedOptions
                    .Select(o => new { Name = o.Description, ParamName = o.HasLongName ? $"--{o.LongName}" : $"-{o.ShortName}" })
                    .Select(n =>
                    {
                        var property = action.GetType().GetProperty(n.Name);
                        return $"{n.ParamName} {property.GetValue(action).ToString()}";
                    })
                    .Aggregate((a, b) => string.Join(" ", a, b));

                var command = $"{context} {subContext} {name} {args}";

                var logFile = Path.GetTempFileName();
                var exeName = Process.GetCurrentProcess().MainModule.FileName;
                command = $"/c \"{exeName}\" {command} >> {logFile}";


                var startInfo = new ProcessStartInfo("cmd")
                {
                    Verb = "runas",
                    Arguments = command,
                    WorkingDirectory = Environment.CurrentDirectory,
                    CreateNoWindow = false,
                    UseShellExecute = true
                };

                var process = Process.Start(startInfo);
                process.WaitForExit();
                errors = File.ReadAllText(logFile);
                return process.ExitCode == ExitCodes.Success;
            }
            else
            {
                throw new ArgumentException($"{nameof(IAction)} type doesn't have {nameof(ActionAttribute)}");
            }
        }

        internal ConsoleApp(string[] args, Assembly assembly, IContainer container)
        {
            _args = args;
            _container = container;
            _actionAttributes = assembly
                .GetTypes()
                .Where(t => typeof(IAction).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(type => type.GetCustomAttributes<ActionAttribute>().Select(a => new TypeAttributePair { Type = type, Attribute = a }))
                .SelectMany(i => i);
        }

        internal IAction Parse()
        {
#if DEBUG
            //ConsoleAppUtilities.ValidateVerbs(_verbTypes);
#endif
            if (_args.Length == 0 ||
                _args.Any(a => a.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("--version", StringComparison.OrdinalIgnoreCase) ||
                               a.Equals("-version", StringComparison.OrdinalIgnoreCase)))
            {
                return new HelpAction(_actionAttributes);
            }

            var argsStack = new Stack<string>(_args.Reverse());
            var contextStr = argsStack.Peek();
            var subContextStr = string.Empty;
            var context = Context.None;
            var subContext = Context.None;
            var actionStr = string.Empty;

            if (Enum.TryParse(contextStr, true, out context))
            {
                argsStack.Pop();
                if (argsStack.Any())
                {
                    subContextStr = argsStack.Peek();
                    if (Enum.TryParse(subContextStr, true, out subContext))
                    {
                        argsStack.Pop();
                    }
                }
            }

            if (argsStack.Any())
            {
                actionStr = argsStack.Pop();
            }

            if (string.IsNullOrEmpty(actionStr))
            {
                return new HelpAction(_actionAttributes, contextStr, subContextStr);
            }

            var actionType = _actionAttributes
                .Where(a => a.Attribute.Name.Equals(actionStr, StringComparison.OrdinalIgnoreCase) &&
                            a.Attribute.Context == context && 
                            a.Attribute.SubContext == subContext)
                .SingleOrDefault();

            if (actionType == null)
            {
                return new HelpAction(_actionAttributes, contextStr, subContextStr);
            }

            var action = CreateAction(actionType);
            var args = argsStack.ToArray();
            try
            {
                var parseResult = action.ParseArgs(args);
                if (parseResult.HasErrors)
                {
                    return new HelpAction(_actionAttributes, action, parseResult);
                }
                else
                {
                    return action;
                }
            }
            catch (ArgumentException ex)
            {
                ColoredConsole.Error.WriteLine(ex.Message);
                return null;
            }
        }

        internal IAction CreateAction(TypeAttributePair actionType)
        {
            var ctor = actionType.Type.GetConstructors()?.SingleOrDefault();
            var args = ctor?.GetParameters()?.Select(p => _container.Resolve(p.ParameterType)).ToArray();
            return args == null || args.Length == 0
                ? (IAction)Activator.CreateInstance(actionType.Type)
                : (IAction)Activator.CreateInstance(actionType.Type, args);
        }
    }
}
