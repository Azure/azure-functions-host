// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Extensions.EasyTables;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class EasyTableBindingMetadata : BindingMetadata
    {
        public string TableName { get; set; }

        public string Id { get; set; }

        [AllowNameResolution]
        public string MobileAppUri { get; set; }

        [AllowNameResolution]
        public string ApiKey { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            EasyTablesConfiguration config = new EasyTablesConfiguration();

            // Overwrite the default values if they have been specified directly.
            // There are no resource pickers for these values (yet), so we need
            // to be able to treat them as both literal strings and replacements.
            if (!string.IsNullOrEmpty(MobileAppUri))
            {
                config.MobileAppUri = new Uri(MobileAppUri);
            }

            if (!string.IsNullOrEmpty(ApiKey))
            {
                config.ApiKey = ApiKey;
            }

            configBuilder.Config.UseEasyTables();
        }
    }
}
