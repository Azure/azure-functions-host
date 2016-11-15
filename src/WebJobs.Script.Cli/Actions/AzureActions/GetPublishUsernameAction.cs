using System;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "get-publish-username", Context = Context.Azure, HelpText = "Get the source control publishing username for all Function Apps in Azure")]
    class GetPublishUserNameAction : BaseAction
    {
        private readonly IArmManager _armManager;

        public GetPublishUserNameAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override async Task RunAsync()
        {
            var user = await _armManager.GetUserAsync();
            if (string.IsNullOrEmpty(user.PublishingUserName))
            {
                ColoredConsole.WriteLine($"Publishing user is not configured. Run {ExampleColor("func azure set-publish-username <userName>")} to configure your publishing user");
            }
            else
            {
                ColoredConsole
                    .Write(TitleColor("Publishing Username: "))
                    .Write(user.PublishingUserName)
                    .WriteLine()
                    .WriteLine()
                    .Write("run ")
                    .Write(ExampleColor($"\"func azure set-publish-password {user.PublishingUserName}\" "))
                    .WriteLine("to update the password");
            }
        }
    }
}
