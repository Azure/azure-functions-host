using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Arm.Extensions;
using WebJobs.Script.ConsoleHost.Arm.Models;

namespace WebJobs.Script.ConsoleHost.Arm
{
    public partial class ArmManager
    {
        public async Task<IEnumerable<ServerFarm>> GetServerFarms()
        {
            var subscriptions = await GetSubscriptions();
            var temp = await subscriptions
                .Select(s => GetServerFarms(s))
                .WhenAll();
            return temp.SelectMany(i => i);
        }

        public async Task<IEnumerable<ServerFarm>> GetServerFarms(Subscription subscription)
        {
            var serverFarmsResponse = await _client.HttpInvoke(HttpMethod.Get, ArmUriTemplates.SubscriptionLevelServerFarms.Bind(subscription));
            await serverFarmsResponse.EnsureSuccessStatusCodeWithFullError();
            var serverFarms = await serverFarmsResponse.Content.ReadAsAsync<ArmArrayWrapper<ArmServerFarm>>();
            return serverFarms.value
                .Select(sf => new ServerFarm(subscription.SubscriptionId, sf.properties.resourceGroup, sf.name) { GeoRegion = sf.properties.geoRegion });
        }
    }
}