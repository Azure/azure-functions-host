using System;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "logout", Context = Context.Azure, HelpText = "Log out of Azure account")]
    class LogoutAction : BaseAction
    {
        private readonly IArmManager _armManager;

        public LogoutAction(IArmManager armManager)
        {
            _armManager = armManager;
        }

        public override Task RunAsync()
        {
            _armManager.Logout();
            return Task.CompletedTask;
        }
    }
}
