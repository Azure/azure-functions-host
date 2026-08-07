// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Azure.WebJobs.Script.Tests;

/// <summary>
/// Represents immutable process facts controlled by a test.
/// </summary>
public sealed record TestProcessFacts
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestProcessFacts"/> class.
    /// </summary>
    public TestProcessFacts(
        OSPlatform platform, Architecture osArchitecture, bool is64BitProcess, int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        Platform = platform;
        OSArchitecture = osArchitecture;
        Is64BitProcess = is64BitProcess;
        ProcessorCount = processorCount;
    }

    /// <summary>
    /// Gets the operating-system platform.
    /// </summary>
    public OSPlatform Platform { get; }

    /// <summary>
    /// Gets the operating-system architecture.
    /// </summary>
    public Architecture OSArchitecture { get; }

    /// <summary>
    /// Gets a value indicating whether the process is 64-bit.
    /// </summary>
    public bool Is64BitProcess { get; }

    /// <summary>
    /// Gets the processor count exposed to the test.
    /// </summary>
    public int ProcessorCount { get; }
}
