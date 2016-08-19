using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using NSubstitute;
using WebJobs.Script.Cli.Interfaces;
using WebJobs.Script.Cli.Verbs;
using Xunit;

namespace WebJobs.Script.Cli.Tests.VerbsTests
{
    public class ConfigVerbTests
    {
        [Theory]
        [InlineData("configName", "configValue")]
        public async Task ConfigVerbTest(string configName, string configValue)
        {
            // Setup
            var settings = Substitute.For<ISettings>();
            var tipsManager = Substitute.For<ITipsManager>();
            var stdout = Substitute.For<IConsoleWriter>();
            var stderr = Substitute.For<IConsoleWriter>();

            ColoredConsole.Out = stdout;
            ColoredConsole.Error = stderr;

            settings.GetSettings().Returns(new Dictionary<string, object> { { configName, configValue } });

            // Test
            var configVerb = new ConfigVerb(settings, tipsManager);
            await configVerb.RunAsync();

            // Assert
            stdout
                .Received()
                .WriteLine(Arg.Is<object>(l => l.ToString().Contains(configName) && l.ToString().Contains(configValue)));
        }

        [Theory]
        [InlineData("configName", "oldConfigValue", "newConfigValue", false)]
        [InlineData("configName", "oldConfigValue", "newConfigValue", false)]
        [InlineData("configName", false, "", true)]
        [InlineData("notFound", null, null, true)]
        public async Task ConfigVerbNameTest(string configName, object oldConfigValue, string newConfigValue, bool error)
        {
            // Setup
            var settings = Substitute.For<ISettings>();
            var tipsManager = Substitute.For<ITipsManager>();
            var stdout = Substitute.For<IConsoleWriter>();
            var stderr = Substitute.For<IConsoleWriter>();

            ColoredConsole.Out = stdout;
            ColoredConsole.Error = stderr;

            if (configName != "notFound")
            {
                settings.GetSettings().Returns(new Dictionary<string, object> { { configName, oldConfigValue } });
            }
            else
            {
                settings.GetSettings().Returns(new Dictionary<string, object>());
            }

            // Test
            var configVerb = new ConfigVerb(settings, tipsManager)
            {
                Name = configName,
                Value = newConfigValue
            };

            await configVerb.RunAsync();

            // Assert
            if (configName == "notFound")
            {
                stderr
                    .Received()
                    .WriteLine(Arg.Is<object>(l => l.ToString().Contains("Cannot find setting ")));
            }
            else if (!error)
            {
                settings
                    .Received()
                    .SetSetting(Arg.Is<string>(n => n == configName), Arg.Is<string>(o => o == newConfigValue));
            }
            else
            {
                stderr
                    .Received()
                    .WriteLine(Arg.Is<object>(o => o.ToString().Contains("Value cannot be empty.")));
            }
        }
    }
}
