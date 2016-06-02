// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class ApiHubTableBindingMetadata : BindingMetadata
    {
        [AutoResolve]
        public string DataSetName { get; set; }

        [AutoResolve]
        public string TableName { get; set; }

        [AutoResolve]
        public string EntityId { get; set; }
    }
}
