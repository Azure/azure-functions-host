// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class MSIContext
    {
        public string SiteName { get; set; }

        public string MSISecret { get; set; }

        public IEnumerable<ManagedServiceIdentity> Identities { get; set; }

        public ManagedServiceIdentity SystemAssignedIdentity { get; set; }

        public IEnumerable<ManagedServiceIdentity> UserAssignedIdentities { get; set; }

        public IEnumerable<ManagedServiceIdentity> DelegatedIdentities { get; set; }
    }
}
