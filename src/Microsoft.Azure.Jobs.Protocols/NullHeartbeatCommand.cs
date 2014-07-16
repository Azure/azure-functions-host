// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a null object implementation of <see cref="IHeartbeatCommand"/>.</summary>
#if PUBLICPROTOCOL
    public class NullHeartbeatCommand : IHeartbeatCommand
#else
    internal class NullHeartbeatCommand : IHeartbeatCommand
#endif
    {
        /// <inheritdoc />
        public void Beat()
        {
        }
    }
}
