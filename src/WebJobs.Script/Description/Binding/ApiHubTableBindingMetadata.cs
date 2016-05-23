// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ApiHubTableBindingMetadata : BindingMetadata
    {
        [AllowNameResolution]
        public string DataSetName { get; set; }

        [AllowNameResolution]
        public string TableName { get; set; }

        [AllowNameResolution]
        public string EntityId { get; set; }
    }
}
