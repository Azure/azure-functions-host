// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class AppServiceOptions
    {
        public string AppName { get; set; }

        public string SubscriptionId { get; set; }

        public string RuntimeSiteName { get; set; }

        public string SlotName { get; set; }
    }
}
