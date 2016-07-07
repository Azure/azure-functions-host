// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Globalization;

namespace WebJobs.Script.Cli.Arm.Models
{
    internal class ResourceGroup : BaseResource
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
        public string Location { get; private set; }
        public StorageAccount FunctionsStorageAccount { get; internal set; }
        public Site FunctionsSite { get; internal set; }

        public ResourceGroup(string subscriptionId, string resourceGroupName, string location)
            : base(subscriptionId, resourceGroupName)
        {
            this.Location = location;
        }
    }
}