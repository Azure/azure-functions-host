// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class ManagedServiceIdentity
    {
        public ManagedServiceIdentityType Type { get; set; }

        public string ClientId { get; set; }

        public string TenantId { get; set; }

        public string Thumbprint { get; set; }

        public string SecretUrl { get; set; }

        public string ResourceId { get; set; }

        public string Certificate { get; set; }

        public string PrincipalId { get; set; }

        public string AuthenticationEndpoint { get; set; }
    }
}
