using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using NCli;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Verbs
{
    internal class ConfigVerb : BaseVerb
    {
        private readonly ISettings _settings;

        [Option(0)]
        public string Name { get; set; }

        [Option(1)]
        public string Value { get; set; }

        public ConfigVerb(ISettings settings, ITipsManager tipsManager)
            : base(tipsManager)
        {
            _settings = settings;
        }

        public override Task RunAsync()
        {
            if (string.IsNullOrEmpty(Name))
            {
                foreach (var pair in _settings.GetSettings())
                {
                    ColoredConsole
                        .WriteLine($"{TitleColor(pair.Key)} = {pair.Value}");
                }
            }
            else
            {
                var setting = _settings
                    .GetSettings()
                    .Select(s => new { Name = s.Key, Value = s.Value })
                    .FirstOrDefault(s => s.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));
                if (setting == null)
                {
                    ColoredConsole
                        .Error
                        .WriteLine(ErrorColor($"Cannot find setting '{Name}'"));
                }
                else
                {
                    if (string.IsNullOrEmpty(Value) && setting.Value.GetType() != typeof(string))
                    {
                        ColoredConsole
                            .Error
                            .WriteLine(ErrorColor("Value cannot be empty."));
                        return Task.CompletedTask;
                    }
                    _settings.SetSetting(Name, Value);
                }
            }

            return Task.CompletedTask;
        }
    }
}