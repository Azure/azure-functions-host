using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "login", Context = Context.Azure)]
    class LoginAction : BaseAzureAccountAction
    {
        public LoginAction(IArmManager armManager, ISettings settings)
            : base(armManager, settings)
        {
        }

        public override async Task RunAsync()
        {
            await ArmManager.LoginAsync();
            await PrintAccountsAsync();
        }
    }
}
