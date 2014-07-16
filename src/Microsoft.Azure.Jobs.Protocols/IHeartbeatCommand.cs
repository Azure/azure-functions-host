// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines a command that signals a heartbeat from a running host instance.</summary>
#if PUBLICPROTOCOL
    public interface IHeartbeatCommand
#else
    internal interface IHeartbeatCommand
#endif
    {
        /// <summary>Signals a heartbeat from a running host instance.</summary>
        void Beat();
    }
}
