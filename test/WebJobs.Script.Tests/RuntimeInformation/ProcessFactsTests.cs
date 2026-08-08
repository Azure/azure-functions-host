// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class ProcessFactsTests
{
    [Fact]
    public void Capture_MatchesCurrentRuntime()
    {
        ProcessFacts facts = ProcessFacts.Capture();

        Assert.Equal(SystemEnvironment.GetCurrentPlatform(), facts.Platform);
        Assert.Equal(RuntimeInformation.OSArchitecture, facts.OSArchitecture);
        Assert.Equal(Environment.Is64BitProcess, facts.Is64BitProcess);
        Assert.Equal(Environment.ProcessorCount, facts.ProcessorCount);
    }

    [Fact]
    public void Constructor_ProcessorCountMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessFacts(OSPlatform.Windows, Architecture.X64, true, 0));
    }
}
