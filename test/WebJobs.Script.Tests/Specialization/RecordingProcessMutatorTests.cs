// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Specialization;

public class RecordingProcessMutatorTests
{
    [Fact]
    public void SetPreservesExactOrderNullEmptyAndDuplicatesWithoutChangingProcess()
    {
        string name = $"RecordingProcessMutatorTests_{Guid.NewGuid():N}";
        string original = System.Environment.GetEnvironmentVariable(name);
        RecordingProcessMutator mutator = new();

        mutator.Set(name, null);
        mutator.Set(name, string.Empty);
        mutator.Set(name, "value");
        mutator.Set(name, "value");

        Assert.Equal(
            [
                new ProcessMutation(name, null),
                new ProcessMutation(name, string.Empty),
                new ProcessMutation(name, "value"),
                new ProcessMutation(name, "value"),
            ],
            mutator.Attempts);
        Assert.Equal(original, System.Environment.GetEnvironmentVariable(name));
    }
}
