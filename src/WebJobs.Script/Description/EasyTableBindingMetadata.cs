// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class EasyTableBindingMetadata : BindingMetadata
    {
        public string TableName { get; set; }

        public string Id { get; set; }

        public override void ApplyToConfig(JobHostConfigurationBuilder configBuilder)
        {
            if (configBuilder == null)
            {
                throw new ArgumentNullException("configBuilder");
            }

            configBuilder.Config.UseEasyTables();
        }
    }
}
