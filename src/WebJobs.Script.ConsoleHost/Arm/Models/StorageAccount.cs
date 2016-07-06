// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class StorageAccount : BaseResource
    {
        private string _csmIdTemplate = "/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Storage/storageAccounts/{2}";

        public string StorageAccountName { get; private set; }

        public override string ArmId
        {
            get
            {
                return string.Format(CultureInfo.InvariantCulture, this._csmIdTemplate, this.SubscriptionId, this.ResourceGroupName, this.StorageAccountName);
            }
        }

        public string StorageAccountKey { get; set; }

        public StorageAccount(string subscriptionId, string resourceGroupName, string storageAccountName)
            : base(subscriptionId, resourceGroupName)
        {
            this.StorageAccountName = storageAccountName;
        }

        public string GetConnectionString()
        {
            return string.Format(Constants.StorageConnectionStringTemplate, StorageAccountName, StorageAccountKey);
        }
    }
}