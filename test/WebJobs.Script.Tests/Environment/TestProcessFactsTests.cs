// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Config.Tests;

public class TestProcessFactsTests
{
    [Fact]
    public void ValuesAreImmutableAndControllable()
    {
        TestProcessFacts facts = new(OSPlatform.Linux, Architecture.Arm64, false, 7);

        Assert.Equal(OSPlatform.Linux, facts.Platform);
        Assert.Equal(Architecture.Arm64, facts.OSArchitecture);
        Assert.False(facts.Is64BitProcess);
        Assert.Equal(7, facts.ProcessorCount);
    }

    [Fact]
    public void ProcessorCountMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TestProcessFacts(OSPlatform.Windows, Architecture.X64, true, 0));
    }
}
