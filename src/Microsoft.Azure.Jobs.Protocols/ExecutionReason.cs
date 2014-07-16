// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Defines constants for reasons a function is executed.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
#if PUBLICPROTOCOL
    public enum ExecutionReason
#else
    internal enum ExecutionReason
#endif
    {
        /// <summary>Indicates a function executed because of an automatic trigger.</summary>
        AutomaticTrigger,

        /// <summary>Indicates a function executed because of a programatic host call.</summary>
        HostCall,

        /// <summary>Indicates a function executed because of a request from a dashboard user.</summary>
        Dashboard
    }
}
