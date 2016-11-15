using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using Fclp;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    [Action(Name = "set", Context = Context.Azure, SubContext = Context.Account, HelpText = "Set the active subscription")]
    [Action(Name = "set", Context = Context.Azure, SubContext = Context.Subscriptions, HelpText = "Set the active subscription")]
    class SetAzureAccountAction : BaseAzureAccountAction
    {
        private string _subscription { get; set; }

        public SetAzureAccountAction(IArmManager armManager, ISettings settings)
            : base(armManager, settings)
        {
        }

        public override ICommandLineParserResult ParseArgs(string[] args)
        {
            if (args.Any())
            {
                _subscription = args.First();
            }
            else
            {
                throw new ArgumentException("Must specify subscription id.");
            }

            return base.ParseArgs(args);
        }

        public override async Task RunAsync()
        {
            var tenants = ArmManager.GetTenants();
            var validSub = tenants
                .Select(t => t.subscriptions)
                .SelectMany(s => s)
                .Any(s => s.subscriptionId.Equals(_subscription));
            if (validSub)
            {
                Settings.CurrentSubscription = _subscription;
            }
            else
            {
                ColoredConsole.Error.WriteLine($"Unable to find ${_subscription}");
            }
            await PrintAccountsAsync();
        }
    }
}
