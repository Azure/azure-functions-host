// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public static class CloudConstants
    {
        public const string AzureStorageSuffix = "core.windows.net";
        public const string BlackforestStorageSuffix = "core.cloudapi.de";
        public const string FairfaxStorageSuffix = "core.usgovcloudapi.net";
        public const string MooncakeStorageSuffix = "core.chinacloudapi.cn";
        public const string USNatStorageSuffix = "core.eaglex.ic.gov";
        public const string USSecStorageSuffix = "core.microsoft.scloud";

        //https://docs.microsoft.com/en-us/azure/key-vault/general/about-keys-secrets-certificates?ranMID=24542&ranEAID=a1LgFw09t88&ranSiteID=a1LgFw09t88-tllHun9DMwaNbA58tXdq0g&epi=a1LgFw09t88-tllHun9DMwaNbA58tXdq0g&irgwc=1&OCID=AID2000142_aff_7593_1243925&tduid=%28ir__ytry9f99bgkfqkph0higqpq2m22xuky3otwrmkuh00%29%287593%29%281243925%29%28a1LgFw09t88-tllHun9DMwaNbA58tXdq0g%29%28%29&irclickid=_ytry9f99bgkfqkph0higqpq2m22xuky3otwrmkuh00#dns-suffixes-for-base-url
        public const string AzureVaultSuffix = ".vault.azure.net";
        public const string MooncakeVaultSuffix = ".vault.azure.cn";
        public const string FairfaxVaultSuffix = ".vault.usgovcloudapi.net";
        public const string BlackforestVaultSuffix = ".vault.microsoftazure.de";
    }
}
