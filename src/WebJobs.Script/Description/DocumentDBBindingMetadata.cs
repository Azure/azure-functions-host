// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal class DocumentDBBindingMetadata : BindingMetadata
    {
        public string DatabaseName { get; set; }

        public string CollectionName { get; set; }

        public bool CreateIfNotExists { get; set; }
    }
}
