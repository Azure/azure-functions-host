using System.Linq;
using System.Threading.Tasks;
using ARMClient.Authentication.Contracts;
using Colors.Net;
using NSubstitute;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Arm.Models;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Interfaces;
using WebJobs.Script.Cli.Verbs;
using WebJobs.Script.Cli.Verbs.List;
using Xunit;

namespace WebJobs.Script.Cli.Tests.VerbsTest
{
    public class ListVerbTests
    {

        [Theory]
        [InlineData("appName", "West US")]
        public async Task ListCommandTest(string functionAppName, string location)
        {
            // Setup
            var armManager = Substitute.For<IArmManager>();
            var stdout = Substitute.For<IConsoleWriter>();
            var secretsManager = Substitute.For<ISecretsManager>();
            var tipsManager = Substitute.For<ITipsManager>();

            ColoredConsole.Out = stdout;

            var apps = new []
            {
                new Site(string.Empty, string.Empty, functionAppName) { Location = location }
            }.AsEnumerable();

            armManager.GetFunctionAppsAsync().Returns(apps);
            armManager.GetCurrentTenantAsync().Returns(new TenantCacheInfo());
            armManager.GetUserAsync().Returns(new ArmWebsitePublishingCredentials { PublishingUserName = "test" });

            // Test
            var listVerb = new ListFunctionApps(armManager, tipsManager);

            await listVerb.RunAsync();

            // Assert
            armManager
                .Received()
                .GetFunctionAppsAsync()
                .Ignore();

            stdout
                .Received()
                .WriteLine(Arg.Is<object>(v => v.ToString().Contains(functionAppName)));
        }
    }
}
