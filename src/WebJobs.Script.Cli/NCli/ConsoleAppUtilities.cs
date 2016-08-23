using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Colors.Net;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace NCli
{
    internal static class ConsoleAppUtilities
    {
        public static IEnumerable<Helpline> BuildHelp(string[] args, IEnumerable<VerbType> verbTypes, string cliName, bool faulted)
        {
            if (args.Length > 1)
            {
                var userVerbString = (faulted ? args : args.Skip(1)).First();
                var verbsToShowInHelp = verbTypes.Where(p => p.Metadata.Names.Any(n => n.Equals(userVerbString, StringComparison.OrdinalIgnoreCase)) && p.Metadata.ShowInHelp);
                foreach (var verb in verbsToShowInHelp)
                {
                    if (verbTypes.Count() > 1)
                    {
                        yield return new Helpline { Value = $"{TitleColor("Usage")}: {cliName} {userVerbString} {AdditionalInfoColor(verb.Metadata.Scope.ToString())} {verb.Metadata.Usage ?? "\b"} [Options]", Level = TraceLevel.Info };
                    }
                    else
                    {
                        yield return new Helpline { Value = $"{TitleColor("Usage")}: {cliName} {userVerbString} {verb.Metadata.Usage ?? "\b"} {AdditionalInfoColor("[Options]")}", Level = TraceLevel.Info };
                    }

                    yield return new Helpline { Value = "\t", Level = TraceLevel.Info };

                    var options = verb.Options.Where(o => o.Attribute.ShowInHelp).ToList();
                    var longestOption = options.Select(s => s.Attribute.GetUsage(s.PropertyInfo.Name)).Select(s => s.Length).Concat(new[] { 0 }).Max();
                    longestOption += 3;
                    foreach (var option in options)
                    {
                        var helpText = option.Attribute.HelpText;
                        if (helpText == null && option.PropertyInfo.PropertyType.IsEnum)
                        {
                            var type = option.PropertyInfo.PropertyType;
                            helpText = $"[{Enum.GetNames(type).Where(n => !n.Equals("none", StringComparison.OrdinalIgnoreCase)).Aggregate((a, b) => $"{a}/{b}")}]";
                        }
                        else if (helpText == null && option.Attribute._order != -1)
                        {
                            helpText = "(Required)";
                        }
                        else if (helpText == null)
                        {
                            helpText = string.Empty;
                        }

                        yield return new Helpline { Value = string.Format($"   {{0, {-longestOption}}} {{1}}", option.Attribute.GetUsage(option.PropertyInfo.Name), helpText), Level = TraceLevel.Info };
                    }

                    if (options.Any())
                    {
                        yield return new Helpline { Value = string.Empty, Level = TraceLevel.Info };
                    }
                }
                yield break;
            }

            foreach (var help in GeneralHelp(verbTypes, cliName))
            {
                yield return help;
            }
        }

        public static IEnumerable<Helpline> GeneralHelp(IEnumerable<VerbType> verbTypes, string cliName)
        {
            yield return new Helpline { Value = $"Usage: {cliName} [verb] [Options]", Level = TraceLevel.Info };
            yield return new Helpline { Value = "\t", Level = TraceLevel.Info };

            var hashSet = new HashSet<string>();
            var longestName = verbTypes.Select(p => p.Metadata).Max(v => v.Names.Max(n => n.Length));
            foreach (var verb in verbTypes.Where(v => v.Metadata.ShowInHelp))
            {
                foreach (var name in verb.Metadata.Names)
                {
                    if (!hashSet.Contains(name))
                    {
                        hashSet.Add(name);
                        yield return new Helpline { Value = string.Format($"   {{0, {-longestName}}}  {{1}}", name, verb.Metadata.HelpText), Level = TraceLevel.Info };
                    }
                }
            }
        }

        public static bool TryParseEnum(Type type, string value, out object result)
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


        public static void ValidateVerbs(IEnumerable<VerbType> verbTypes)
        {
            var scopesNotEnums = verbTypes.Where(v => v.Metadata?.Scope?.GetType()?.IsEnum == false);

            if (scopesNotEnums.Any())
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Scope attribute can only be an Enum"));
                ColoredConsole.Error.WriteLine(ErrorColor("Found:"));
                foreach (var scope in scopesNotEnums)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Scope on verb '{scope.Type.Name}' is set to '{scope.Metadata.Scope}' of type '{scope.Metadata.Scope.GetType().Name}'"));
                }
                throw new ParseException("Scope attribute can only be an Enum.");
            }

            foreach (var verb in verbTypes)
            {
                if (verb.Metadata.Scope != null) continue;

                var verbsShareName = verbTypes.Where(v => v.Metadata.Names.Intersect(verb.Metadata.Names).Any() && v.Type != verb.Type);
                if (verbsShareName.Any())
                {
                    ColoredConsole.Error.WriteLine(ErrorColor($"Verb '{verb.Type.Name}' shares the same name with other verb(s), but doesn't have Scope defined"));
                    ColoredConsole.Error.WriteLine(ErrorColor("Verbs:"));
                    foreach (var v in verbsShareName)
                    {
                        ColoredConsole.Error.WriteLine(ErrorColor($"\t{v.Type.Name}"));
                    }

                    throw new ParseException($"Verb '{verb.Type.Name}' shares the same name with other verb(s), but doesn't have Scope defined");
                }
            }
        }

        public static VerbType GetVerbType(string[] args, IEnumerable<VerbType> verbTypes)
        {
            var helpVerb = verbTypes
                    .FirstOrDefault(p => p.Metadata.Names.Any(n => n.Equals("help", StringComparison.OrdinalIgnoreCase)))
                    ?? new VerbType { Type = typeof(DefaultHelp) };

            if (args == null || !args.Any())
            {
                return helpVerb;
            }

            var userVerb = args.First();

            verbTypes = verbTypes.Where(p => p.Metadata.Names.Any(n => n.Equals(userVerb, StringComparison.OrdinalIgnoreCase)));

            if (!verbTypes.Any())
            {
                return helpVerb;
            }
            else if (verbTypes.Count() == 1 || args.Count() == 1)
            {
                return verbTypes.First();
            }
            else
            {
                var scopeString = args.Skip(1).First();
                foreach (var verb in verbTypes)
                {
                    var scopeType = verb.Metadata.Scope.GetType();
                    object scopeEnum;
                    if (TryParseEnum(scopeType, scopeString, out scopeEnum))
                    {
                        return verbTypes.FirstOrDefault(v => v.Metadata.Scope.ToString() == scopeEnum.ToString()) ?? helpVerb;
                    }
                }
            }

            return helpVerb;
        }

        public static bool TryParseOption(PropertyInfo option, Stack<string> args, out object value)
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

        public static bool TryCast(string arg, Type type, out object obj)
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
                return false;
            }
        }

        public static VerbAttribute TypeToAttribute(Type type)
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
                    Usage = attribute.Usage,
                    Scope = attribute.Scope
                };
            }
            else
            {
                return attribute;
            }
        }

        public static VerbType TypeToVerbType(Type type)
        {
            return new VerbType
            {
                Type = type,
                Metadata = TypeToAttribute(type)
            };
        }

    }
}
