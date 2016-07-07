using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NCli
{
    public class ConsoleApp
    {
        private readonly IDependencyResolver _dependencyResolver;
        private readonly string[] _args;
        private readonly IEnumerable<TypePair<VerbAttribute>> _verbs;
        private readonly string _cliName;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter")]
        public static async Task RunAsync<T>(string[] args, IDependencyResolver dependencyResolver = null)
        {
            var app = new ConsoleApp(args, typeof(T).Assembly, dependencyResolver);
            var verb = app.Parse();
            try
            {
                await verb.RunAsync();
                if (verb is IVerbPostRun)
                {
                    await (verb as IVerbPostRun).PostRunVerbAsync();
                }
            }
            catch (Exception exception)
            {
                if (verb is IVerbError)
                {
                    await (verb as IVerbError).OnErrorAsync(exception);
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
            _cliName = Process.GetCurrentProcess().ProcessName;
            _verbs = assembly
                .GetTypes()
                .Where(t => typeof(IVerb).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(DefaultHelp))
                .Select(t => new TypePair<VerbAttribute> { Type = t, Attribute = TypeToAttribute(t) });
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        internal IVerb Parse()
        {
            ValidateVerbs();
            try
            {
                var verbType = GetVerbType(_args);
                var verb = InstantiateType(verbType.Type);
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
                object value;
                while (stack.Any() && orderedOptions.Any())
                {
                    var orderedOption = orderedOptions.Pop();
                    if (TryParseOption(orderedOption, stack, out value))
                    {
                        orderedOption.SetValue(verb, value);
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
                        //throw new ParseException($"Unable to find option {arg} on {_args[0]}");
                    }

                    if (TryParseOption(option, stack, out value))
                    {
                        option.SetValue(verb, value);
                    }
                }
                return verb;
            }
            catch (Exception e)
            {
                // There was an error. Maybe report it if user allowed reports, and display friendly error code for lookup.
                // Maybe something like: search for ##### on github issues for updates.
                var verb = InstantiateType(typeof(DefaultHelp));
                verb.OriginalVerb = _args[0];
                verb.DependencyResolver = _dependencyResolver;
                Console.Write(e);
                return verb;
            }
        }

        private void ValidateVerbs()
        {
            var scopesNotEnums = _verbs.Where(v => v.Attribute?.Scope?.GetType()?.IsEnum == false);

            if (scopesNotEnums.Any())
            {
                Console.Error.WriteLine("Scope attribute can only be an Enum");
                Console.Error.WriteLine("Found:");
                foreach (var scope in scopesNotEnums)
                {
                    Console.Error.WriteLine($"Scope on verb '{scope.Type.Name}' is set to '{scope.Attribute.Scope}' of type '{scope.Attribute.Scope.GetType().Name}'");
                }
                throw new ParseException("Scope attribute can only be an Enum.");
            }

            foreach (var verb in _verbs)
            {
                if (verb.Attribute.Scope != null) continue;

                var verbsShareName = _verbs.Where(v => v.Attribute.Names.Intersect(verb.Attribute.Names).Any() && v.Type != verb.Type);
                if (verbsShareName.Any())
                {
                    Console.Error.WriteLine($"Verb '{verb.Type.Name}' shares the same name with other verb(s), but doesn't have Scope defined");
                    Console.Error.WriteLine("Verbs:");
                    foreach(var v in verbsShareName)
                    {
                        Console.Error.WriteLine($"\t{v.Type.Name}");
                    }

                    throw new ParseException("Scope attribute can only be an Enum.");
                }
            }
        }

        private TypePair<VerbAttribute> GetVerbType(IEnumerable<string> args = null)
        {
            var helpVerb = _verbs
                    .FirstOrDefault(p => p.Attribute.Names.Any(n => n.Equals("help", StringComparison.OrdinalIgnoreCase)))
                    ?? new TypePair<VerbAttribute> { Type = typeof(DefaultHelp) };

            if (args == null || !args.Any())
            {
                return helpVerb;
            }

            var userVerb = args.First();

            var verbs = _verbs.Where(p => p.Attribute.Names.Any(n => n.Equals(userVerb, StringComparison.OrdinalIgnoreCase)));

            if (!verbs.Any())
            {
                return helpVerb;
            }
            else if (verbs.Count() == 1 || args.Count() == 1)
            {
                return verbs.First();
            }
            else
            {
                var scopeString = args.Skip(1).First();
                foreach (var verb in verbs)
                {
                    var scopeType = verb.Attribute.Scope.GetType();
                    object scopeEnum;
                    if (TryParseEnum(scopeType, scopeString, out scopeEnum))
                    {
                        return verbs.FirstOrDefault(v => v.Attribute.Scope.ToString() == scopeEnum.ToString()) ?? helpVerb;
                    }
                }
            }

            return helpVerb;
        }

        private static bool TryParseOption(PropertyInfo option, Stack<string> args, out object value)
        {
            value = null;
            try
            {
                if (option.PropertyType.IsGenericEnumerable())
                {
                    var genericType = option.PropertyType.GetEnumerableType();
                    var values = genericType.CreateList();
                    while (args.Any() && !args.Peek().StartsWith("-"))
                    {
                        var arg = args.Pop();
                        object temp;
                        if (TryCast(arg, genericType, out temp))
                        {
                            values.Add(temp);
                        }
                        else
                        {
                            args.Push(arg);
                            break;
                        }
                    }
                    value = values;
                    return values.Count != 0;
                }
                else if (option.PropertyType == typeof(bool))
                {
                    value = true;
                    return true;
                }
                else
                {
                    var arg = args.Pop();
                    object temp;
                    if (!arg.StartsWith("-") && TryCast(arg, option.PropertyType, out temp))
                    {
                        value = temp;
                        return true;
                    }
                    else
                    {
                        args.Push(arg);
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryCast(string arg, Type type, out object obj)
        {
            obj = null;
            try
            {
                type = Nullable.GetUnderlyingType(type) ?? type;

                if (type.GetTypeInfo().IsEnum)
                {
                    obj = Enum.Parse(type, arg, ignoreCase: true);
                }
                else if (type == typeof(string))
                {
                    obj = arg;
                }
                else if (type == typeof(DateTime))
                {
                    obj = DateTime.Parse(arg);
                }
                else if (type == typeof(int))
                {
                    obj = int.Parse(arg);
                }
                else if (type == typeof(long))
                {
                    obj = long.Parse(arg);
                }
                return obj != null;
            }
            catch
            {
                Console.WriteLine($"Unable to parse ({arg}) as {type.Name}");
                return false;
            }
        }

        private static VerbAttribute TypeToAttribute(Type type)
        {
            var attribute = type.GetTypeInfo().GetCustomAttribute<VerbAttribute>();
            var verbIndex = type.Name.LastIndexOf("verb", StringComparison.OrdinalIgnoreCase);
            var verbName = verbIndex == -1 || verbIndex == 0 ? type.Name : type.Name.Substring(0, verbIndex);
            verbName = verbName.ToLowerInvariant();

            if (attribute == null)
            {
                return new VerbAttribute(verbName);
            }
            else if (attribute.Names.Count() == 0)
            {
                return new VerbAttribute(verbName)
                {
                    HelpText = attribute.HelpText,
                    ShowInHelp = attribute.ShowInHelp,
                    Usage = attribute.Usage
                };
            }
            else
            {
                return attribute;
            }
        }

        private IVerb InstantiateType(Type type)
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

        private object ResolveType(Type type)
        {
            if (type == typeof(HelpTextCollection))
            {
                return new HelpTextCollection(BuildHelp());
            }
            else
            {
                return _dependencyResolver?.GetService(type);
            }
        }

        private IEnumerable<Helpline> BuildHelp()
        {
            if (_args.Length > 1)
            {
                var verb = GetVerbType(_args.Skip(1));
                if (verb != null)
                {
                    yield return new Helpline { Value = $"Usage: {_cliName} {verb.Attribute.Usage} [Options]", Level = TraceLevel.Info };
                    yield return new Helpline { Value = "\t", Level = TraceLevel.Info };

                    var longestOption = verb.Options.Select(s => s.Attribute.GetUsage(s.PropertyInfo.Name)).Select(s => s.Length).Max();
                    foreach (var option in verb.Options)
                    {
                        yield return new Helpline { Value = string.Format($"   {{0, {-longestOption}}} {{1}}", option.Attribute.GetUsage(option.PropertyInfo.Name), option.Attribute.HelpText), Level = TraceLevel.Info };
                    }
                    yield break;
                }
            }

            foreach (var help in GeneralHelp())
            {
                yield return help;
            }
        }

        private IEnumerable<Helpline> GeneralHelp()
        {
            yield return new Helpline { Value = $"Usage: {_cliName} [verb] [Options]", Level = TraceLevel.Info };
            yield return new Helpline { Value = "\t", Level = TraceLevel.Info };

            var longestName = _verbs.Select(p => p.Attribute).Max(v => v.Names.Max(n => n.Length));
            foreach (var verb in _verbs.Where(v => v.Attribute.ShowInHelp))
            {
                foreach (var name in verb.Attribute.Names)
                {
                    yield return new Helpline { Value = string.Format($"   {{0, {-longestName}}}  {{1}}", name, verb.Attribute.HelpText), Level = TraceLevel.Info };
                }
            }
        }

        private static bool TryParseEnum(Type type, string value, out object result)
        {
            result = null;
            try
            {
                result = Enum.Parse(type, value, ignoreCase: true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
