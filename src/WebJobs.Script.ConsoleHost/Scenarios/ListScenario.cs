using ARMClient.Library;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Common;

namespace WebJobs.Script.ConsoleHost.Scenarios
{
    public class ListScenario : BaseArmScenario
    {
        [ValueOption(0)]
        public NewOptions ListOption { get; set; }

        public override async Task Run()
        {
            if (ListOption == NewOptions.FunctionApp)
            {
                var client = new AzureClient(retryCount: 3);
                var subsR = await client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.Subscriptions.Bind(string.Empty));
                var subs = (dynamic[])(await subsR.Content.ReadAsAsync<dynamic>()).value;

                var results = await Task.WhenAll(subs.Select(e => e.subscriptionId).Cast<string>()
                    .Select(s => client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionSites.Bind(new { subscriptionId = s }))));


            }
            else
            {
                TraceInfo("not supported");
            }
        }
    }
}
