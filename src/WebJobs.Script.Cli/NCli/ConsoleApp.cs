using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Colors.Net;
using static WebJobs.Script.Cli.Common.OutputTheme;

    namespace NCli
{
    public class ConsoleApp
    {
        private readonly IDependencyResolver _dependencyResolver;
        private readonly string[] _args;
        private readonly IEnumerable<VerbType> _verbTypes;
        private readonly string _cliName;
        private bool _isFaulted = false;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static async Task RunAsync<T>(string[] args, IDependencyResolver dependencyResolver = null)
        {
            var app = new ConsoleApp(args, typeof(T).Assembly, dependencyResolver);
            var verb = app.Parse();
            try
            {
                if (verb is IVerbPreRun)
                {
                    var @continue = await (verb as IVerbPreRun).PreRunVerbAsync();
                    if (!@continue)
                    {
                        return;
                    }
                }
                await verb.RunAsync();
                if (verb is IVerbPostRun)
                {
                    await (verb as IVerbPostRun).PostRunVerbAsync(failed: false);
                }
            }
            catch (Exception exception)
            {
                if (verb is IVerbError)
                {
                    await (verb as IVerbError).OnErrorAsync(exception);
                }

                if (verb is IVerbPostRun)
                {
                    await (verb as IVerbPostRun).PostRunVerbAsync(failed: true);
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static void Run<T>(string[] args, IDependencyResolver dependencyResolver = null)
        {
            Task.Run(() => RunAsync<T>(args, dependencyResolver)).Wait();
        }

        internal ConsoleApp(string[] args, Assembly assembly, IDependencyResolver dependencyResolver)
        {
            _args = args;
            _dependencyResolver = dependencyResolver;
            _cliName = Process.GetCurrentProcess().ProcessName.ToLowerInvariant();
            _verbTypes = assembly
                .GetTypes()
                .Where(t => typeof(IVerb).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(DefaultHelp))
                .Select(ConsoleAppUtilities.TypeToVerbType);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        internal IVerb Parse()
        {
#if DEBUG
            ConsoleAppUtilities.ValidateVerbs(_verbTypes);
#endif
            try
            {
                var verbType = ConsoleAppUtilities.GetVerbType(_args, _verbTypes);
                var verb = InstantiateType(verbType.Type);
                _dependencyResolver.RegisterService<IVerb>(verb);

                if (_args == null || _args.Length == 1)
                {
                    return verb;
                }

                var stack = new Stack<string>(_args.Skip(1).Reverse());

                foreach (var option in verbType.Options)
                {
                    if (option.Attribute.DefaultValue != null)
                    {
                        option.PropertyInfo.SetValue(verb, option.Attribute.DefaultValue);
                    }
                }

                var orderedOptions = new Stack<PropertyInfo>(verbType.Options.Where(o => o.Attribute._order != -1).OrderBy(o => o.Attribute._order).Select(o => o.PropertyInfo).Reverse().ToArray());


                if (verbType.Metadata.Scope != null)
                {
                    stack.Pop();
                }

                object value;
                while (stack.Any() && orderedOptions.Any())
                {
                    var orderedOption = orderedOptions.Pop();
                    if (ConsoleAppUtilities.TryParseOption(orderedOption, stack, out value))
                    {
                        orderedOption.SetValue(verb, value);
                    }
                    else
                    {
                        throw new ParseException($"Unable to parse option {orderedOption.Name}");
                    }
                }

                while (stack.Any())
                {
                    if (!stack.Any()) break;
                    var arg = stack.Pop();
                    PropertyInfo option = null;
                    if (arg.StartsWith("--", StringComparison.OrdinalIgnoreCase))
                    {
                        option = verbType.Options.SingleOrDefault(o => o.Attribute._longName.Equals(arg.Substring(2), StringComparison.OrdinalIgnoreCase))?.PropertyInfo;
                    }
                    else if (arg.StartsWith("-", StringComparison.OrdinalIgnoreCase) && arg.Length == 2)
                    {
                        option = verbType.Options.SingleOrDefault(o => o.Attribute._shortName.ToString().Equals(arg.Substring(1), StringComparison.OrdinalIgnoreCase))?.PropertyInfo;
                    }

                    if (option == null)
                    {
                        throw new ParseException($"Unable to find option {arg} on {_args[0]}");
                    }

                    if (ConsoleAppUtilities.TryParseOption(option, stack, out value))
                    {
                        option.SetValue(verb, value);
                    }
                    else
                    {
                        throw new ParseException($"Unable to parse option {option.Name}");
                    }
                }
                return verb;
            }
            catch (Exception e)
            {
                _isFaulted = true;
                // There was an error. Maybe report it if user allowed reports, and display friendly error code for lookup.
                // Maybe something like: search for ##### on github issues for updates.
                ColoredConsole
                    .Error
                    .WriteLine(ErrorColor(e.Message))
                    .WriteLine();
                return InstantiateType(ConsoleAppUtilities.GetVerbType(Array.Empty<string>(), _verbTypes).Type);
            }
        }

        internal IVerb InstantiateType(Type type)
        {
            var ctor = type?.GetConstructors()?.SingleOrDefault();
            var args = ctor?.GetParameters()?.Select(p => ResolveType(p.ParameterType)).ToArray();
            IVerb verb = args == null || args.Length == 0
                ? (IVerb)Activator.CreateInstance(type)
                : (IVerb)Activator.CreateInstance(type, args);
            verb.OriginalVerb = _args.FirstOrDefault();
            verb.DependencyResolver = _dependencyResolver;
            return verb;
        }

        internal object ResolveType(Type type)
        {
            if (type == typeof(HelpTextCollection))
            {
                return new HelpTextCollection(ConsoleAppUtilities.BuildHelp(_args, _verbTypes, _cliName, _isFaulted));
            }
            else
            {
                return _dependencyResolver?.GetService(type);
            }
        }
    }
}
