using System;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Account)]
    [Action(Name = "list", Context = Context.Azure, SubContext = Context.Subscriptions)]
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
