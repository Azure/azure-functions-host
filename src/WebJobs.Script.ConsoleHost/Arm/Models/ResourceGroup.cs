using System.Collections.Generic;
using System.Globalization;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class ResourceGroup : BaseResource
    {
        private const string _csmIdTemplate = "/subscriptions/{0}/resourceGroups/{1}";

        public override string ArmId
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, _csmIdTemplate, SubscriptionId, ResourceGroupName);
            }
        }

        public IEnumerable<Site> FunctionsApps { get; set; }
        public IEnumerable<StorageAccount> StorageAccounts { get; set; }

        public Dictionary<string, string> Tags { get; set; }
        public string Location { get; private set; }
        public StorageAccount FunctionsStorageAccount { get; internal set; }
        public Site FunctionsSite { get; internal set; }

        public ResourceGroup(string subsciptionId, string resourceGroupName, string location)
            : base(subsciptionId, resourceGroupName)
        {
            this.Tags = new Dictionary<string, string>();
            this.Location = location;
        }
    }
}