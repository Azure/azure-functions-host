// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Security
{
    public class KeyAuthorizationResult
    {
        public KeyAuthorizationResult(string keyId, AuthorizationLevel level)
        {
            KeyId = keyId;
            AuthorizationLevel = level;
        }

        public string KeyId { get; }

        public AuthorizationLevel AuthorizationLevel { get; }
    }
}