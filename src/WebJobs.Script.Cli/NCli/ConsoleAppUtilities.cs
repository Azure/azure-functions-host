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
        public static IEnumerable<Helpline> BuildHelp(string[] args, IEnumerable<TypePair<VerbAttribute>> verbs, string cliName, bool faulted)
        {
            if (args.Length > 1)
            {
                var userVerbString = (faulted ? args : args.Skip(1)).First();
                var verbsToShowInHelp = verbs.Where(p => p.Attribute.Names.Any(n => n.Equals(userVerbString, StringComparison.OrdinalIgnoreCase)) && p.Attribute.ShowInHelp);
                foreach (var verb in verbsToShowInHelp)
                {
                    if (verbs.Count() > 1)
                    {
                        yield return new Helpline { Value = $"{TitleColor("Usage")}: {cliName} {userVerbString} {AdditionalInfoColor(verb.Attribute.Scope.ToString())} {verb.Attribute.Usage ?? "\b"} [Options]", Level = TraceLevel.Info };
                    }
                    else
                    {
                        yield return new Helpline { Value = $"{TitleColor("Usage")}: {cliName} {userVerbString} {verb.Attribute.Usage ?? "\b"} {AdditionalInfoColor("[Options]")}", Level = TraceLevel.Info };
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

            foreach (var help in GeneralHelp(verbs, cliName))
            {
                yield return help;
            }
        }

        public static IEnumerable<Helpline> GeneralHelp(IEnumerable<TypePair<VerbAttribute>> verbs, string cliName)
        {
            yield return new Helpline { Value = $"Usage: {cliName} [verb] [Options]", Level = TraceLevel.Info };
            yield return new Helpline { Value = "\t", Level = TraceLevel.Info };

            var hashSet = new HashSet<string>();
            var longestName = verbs.Select(p => p.Attribute).Max(v => v.Names.Max(n => n.Length));
            foreach (var verb in verbs.Where(v => v.Attribute.ShowInHelp))
            {
                foreach (var name in verb.Attribute.Names)
                {
                    if (!hashSet.Contains(name))
                    {
                        hashSet.Add(name);
                        yield return new Helpline { Value = string.Format($"   {{0, {-longestName}}}  {{1}}", name, verb.Attribute.HelpText), Level = TraceLevel.Info };
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


        public static void ValidateVerbs(IEnumerable<TypePair<VerbAttribute>> verbs)
        {
            var scopesNotEnums = verbs.Where(v => v.Attribute?.Scope?.GetType()?.IsEnum == false);

            if (scopesNotEnums.Any())
            {
                ColoredConsole.Error.WriteLine(ErrorColor("Scope attribute can only be an Enum"));
                ColoredConsole.Error.WriteLine(ErrorColor("Found:"));
                foreach (var scope in scopesNotEnums)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Scope on verb '{scope.Type.Name}' is set to '{scope.Attribute.Scope}' of type '{scope.Attribute.Scope.GetType().Name}'"));
                }
                throw new ParseException("Scope attribute can only be an Enum.");
            }

            foreach (var verb in verbs)
            {
                if (verb.Attribute.Scope != null) continue;

                var verbsShareName = verbs.Where(v => v.Attribute.Names.Intersect(verb.Attribute.Names).Any() && v.Type != verb.Type);
                if (verbsShareName.Any())
                {
                    ColoredConsole.Error.WriteLine(ErrorColor($"Verb '{verb.Type.Name}' shares the same name with other verb(s), but doesn't have Scope defined"));
                    ColoredConsole.Error.WriteLine(ErrorColor("Verbs:"));
                    foreach (var v in verbsShareName)
                    {
                        ColoredConsole.Error.WriteLine(ErrorColor($"\t{v.Type.Name}"));
                    }

                    throw new ParseException("Scope attribute can only be an Enum.");
                }
            }
        }

        public static TypePair<VerbAttribute> GetVerbType(string[] args, IEnumerable<TypePair<VerbAttribute>> verbs)
        {
            var helpVerb = verbs
                    .FirstOrDefault(p => p.Attribute.Names.Any(n => n.Equals("help", StringComparison.OrdinalIgnoreCase)))
                    ?? new TypePair<VerbAttribute> { Type = typeof(DefaultHelp) };

            if (args == null || !args.Any())
            {
                return helpVerb;
            }

            var userVerb = args.First();

            verbs = verbs.Where(p => p.Attribute.Names.Any(n => n.Equals(userVerb, StringComparison.OrdinalIgnoreCase)));

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

    }
}
