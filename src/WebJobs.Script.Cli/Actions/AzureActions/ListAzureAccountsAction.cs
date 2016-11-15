using System;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Account, HelpText = "List subscriptions for the logged in user")]
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Subscriptions, HelpText = "List subscriptions for the logged in user")]
    class ListAzureAccountsAction : BaseAzureAccountAction
    {
        public ListAzureAccountsAction(IArmManager armManager, ISettings settings)
            : base(armManager, settings)
        {
        }

        public override async Task RunAsync()
        {
            await PrintAccountsAsync();
        }
    }
}
