// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script;

internal sealed class ProcessFacts : IProcessFacts
{
    internal ProcessFacts(
        OSPlatform platform, Architecture osArchitecture, bool is64BitProcess, int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);

        Platform = platform;
        OSArchitecture = osArchitecture;
        Is64BitProcess = is64BitProcess;
        ProcessorCount = processorCount;
    }

    public OSPlatform Platform { get; }

    public Architecture OSArchitecture { get; }

    public bool Is64BitProcess { get; }

    public int ProcessorCount { get; }

    internal static ProcessFacts Capture()
    {
        return new ProcessFacts(
            SystemEnvironment.GetCurrentPlatform(),
            RuntimeInformation.OSArchitecture,
            Environment.Is64BitProcess,
            Environment.ProcessorCount);
    }

    internal static IProcessFacts FromSystemRuntimeInformation(
        ISystemRuntimeInformation systemRuntimeInformation)
    {
        ArgumentNullException.ThrowIfNull(systemRuntimeInformation);

        return systemRuntimeInformation as IProcessFacts
            ?? new ProcessFacts(
                systemRuntimeInformation.GetOSPlatform(),
                systemRuntimeInformation.GetOSArchitecture(),
                Environment.Is64BitProcess,
                Environment.ProcessorCount);
    }

    public Architecture GetOSArchitecture()
    {
        return OSArchitecture;
    }

    public OSPlatform GetOSPlatform()
    {
        return Platform;
    }
}
