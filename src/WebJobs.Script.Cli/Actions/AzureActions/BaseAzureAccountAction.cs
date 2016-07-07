using System;
using System.Linq;
using System.Threading.Tasks;
using Colors.Net;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Interfaces;
using static WebJobs.Script.Cli.Common.OutputTheme;

namespace WebJobs.Script.Cli.Actions.AzureActions
{
    abstract class BaseAzureAccountAction : BaseAction
    {
        public readonly IArmManager ArmManager;
        public readonly ISettings Settings;

        public BaseAzureAccountAction(IArmManager armManager, ISettings settings)
        {
            ArmManager = armManager;
            Settings = settings;
        }

        public async Task PrintAccountsAsync()
        {
            var tenants = ArmManager.GetTenants();
            var currentSub = Settings.CurrentSubscription;
            var subscriptions = tenants
                .Select(t => t.subscriptions)
                .SelectMany(s => s)
                .Select(s => new
                {
                    displayName = s.displayName,
                    subscriptionId = s.subscriptionId,
                    isCurrent = s.subscriptionId.Equals(currentSub, StringComparison.OrdinalIgnoreCase)
                })
                .Distinct();

            if (subscriptions.Any())
            {
                if (!subscriptions.Any(s => s.isCurrent))
                {
                    Settings.CurrentSubscription = subscriptions.First().subscriptionId;
                    currentSub = Settings.CurrentSubscription;
                }

                await ArmManager.SelectTenantAsync(currentSub);

                var longestName = subscriptions.Max(s => s.displayName.Length) + subscriptions.First().subscriptionId.Length + "( ) ".Length;

                ColoredConsole.WriteLine(string.Format($"{{0, {-longestName}}}   {{1}}", TitleColor("Subscription"), TitleColor("Current")));
                ColoredConsole.WriteLine(string.Format($"{{0, {-longestName}}} {{1}}", "------------", "-------"));

                foreach (var subscription in subscriptions)
                {
                    var current = subscription.subscriptionId.Equals(currentSub, StringComparison.OrdinalIgnoreCase)
                        ? TitleColor(true.ToString()).ToString()
                        : false.ToString();
                    ColoredConsole.WriteLine(string.Format($"{{0, {-longestName}}} {{1}}", $"{subscription.displayName} ({subscription.subscriptionId}) ", current));
                }
            }
        }
    }
}
