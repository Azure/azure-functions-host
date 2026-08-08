// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script;

internal interface IProcessFacts : ISystemRuntimeInformation
{
    OSPlatform Platform { get; }

    Architecture OSArchitecture { get; }

    bool Is64BitProcess { get; }

    int ProcessorCount { get; }
}
