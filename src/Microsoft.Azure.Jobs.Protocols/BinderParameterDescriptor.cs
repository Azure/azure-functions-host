// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to an IBinder.</summary>
    [JsonTypeName("IBinder")]
#if PUBLICPROTOCOL
    public class BinderParameterDescriptor : ParameterDescriptor
#else
    internal class BinderParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
