// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class ProgramTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 7)]
    public void SelectEffectiveCores_PreservesPolicyRestrictionAndUsesCapturedCount(
        int environmentEffectiveCores, int expected)
    {
        TestProcessFacts processFacts = new(
            SystemEnvironment.GetCurrentPlatform(),
            System.Runtime.InteropServices.Architecture.X64,
            true,
            7);

        int result = Program.SelectEffectiveCores(
            environmentEffectiveCores, processFacts);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(7, 42)]
    public void CalculateMinimumThreadCount_UsesSixThreadsPerEffectiveCore(
        int effectiveCores, int expected)
    {
        int result = Program.CalculateMinimumThreadCount(effectiveCores);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void CalculateMinimumThreadCount_RejectsNonPositiveCoreCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Program.CalculateMinimumThreadCount(0));
    }
}
