// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a request to abort the host instance.</summary>
    [JsonTypeName("Abort")]
#if PUBLICPROTOCOL
    public class AbortHostInstanceMessage : HostMessage
#else
    internal class AbortHostInstanceMessage : HostMessage
#endif
    {
    }
}
