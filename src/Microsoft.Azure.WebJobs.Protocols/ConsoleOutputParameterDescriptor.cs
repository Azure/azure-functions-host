// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents a parameter bound to a <see cref="TextWriter"/> for console ouput.</summary>
    [JsonTypeName("ConsoleOutput")]
#if PUBLICPROTOCOL
    public class ConsoleOutputParameterDescriptor : ParameterDescriptor
#else
    internal class ConsoleOutputParameterDescriptor : ParameterDescriptor
#endif
    {
    }
}
