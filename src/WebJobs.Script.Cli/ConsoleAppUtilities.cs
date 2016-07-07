using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Actions.HostActions;

namespace WebJobs.Script.Cli
{
    static class ConsoleAppUtilities
    {
        //public static IEnumerable<Helpline> BuildHelp(string[] args, IEnumerable<VerbType> verbTypes, string cliName, bool faulted)
        //{
        //    if (args.Length > 1)
        //    {
        //        var userVerbString = (faulted ? args : args.Skip(1)).First();
        //        var verbsToShowInHelp = verbTypes.Where(p => p.Metadata.Names.Any(n => n.Equals(userVerbString, StringComparison.OrdinalIgnoreCase)) && p.Metadata.ShowInHelp);
        //        foreach (var verb in verbsToShowInHelp)
        //        {
        //            if (verbsToShowInHelp.Count() > 1)
        //            {
        //                yield return new Helpline { Value = $"{TitleColor("Usage")}: {cliName} {userVerbString} {AdditionalInfoColor(verb.Metadata.Scope.ToString() ?? "")} {verb.Metadata.Usage ?? "\b"} [Options]", Level = TraceLevel.Info };
        //            }
        //            else
        //            {
        //                yield return new Helpline { Value = $"{TitleColor("Usage")}: {cliName} {userVerbString} {verb.Metadata.Usage ?? "\b"} {AdditionalInfoColor("[Options]")}", Level = TraceLevel.Info };
        //            }

        //            yield return new Helpline { Value = "\t", Level = TraceLevel.Info };

        //            var options = verb.Options.Where(o => o.Attribute.ShowInHelp).ToList();
        //            var longestOption = options.Select(s => s.Attribute.GetUsage(s.PropertyInfo.Name)).Select(s => s.Length).Concat(new[] { 0 }).Max();
        //            longestOption += 3;
        //            foreach (var option in options)
        //            {
        //                var helpText = option.Attribute.HelpText;
        //                if (helpText == null && option.PropertyInfo.PropertyType.IsEnum)
        //                {
        //                    var type = option.PropertyInfo.PropertyType;
        //                    helpText = $"[{Enum.GetNames(type).Where(n => !n.Equals("none", StringComparison.OrdinalIgnoreCase)).Aggregate((a, b) => $"{a}/{b}")}]";
        //                }
        //                else if (helpText == null && option.Attribute._order != -1)
        //                {
        //                    helpText = "(Required)";
        //                }
        //                else if (helpText == null)
        //                {
        //                    helpText = string.Empty;
        //                }

        //                yield return new Helpline { Value = string.Format($"   {{0, {-longestOption}}} {{1}}", option.Attribute.GetUsage(option.PropertyInfo.Name), helpText), Level = TraceLevel.Info };
        //            }

        //            if (options.Any())
        //            {
        //                yield return new Helpline { Value = string.Empty, Level = TraceLevel.Info };
        //            }
        //        }
        //        yield break;
        //    }

        //    foreach (var help in GeneralHelp(verbTypes, cliName))
        //    {
        //        yield return help;
        //    }
        //}

    }
}
