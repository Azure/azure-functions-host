// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.State;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.State;

public class WorkerAssignmentTests
{
    [Fact]
    public void Equality_IgnoresEnvironmentOrderAndCopiesInput()
    {
        Dictionary<string, string> environment = new() { ["A"] = "one", ["B"] = "two" };
        WorkerAssignment assignment = Create(environment);
        WorkerAssignment reordered = Create(new Dictionary<string, string> { ["B"] = "two", ["A"] = "one" });

        environment["A"] = "changed";
        environment.Add("C", "three");

        Assert.True(assignment.IsEquivalentTo(reordered));
        Assert.True(reordered.IsEquivalentTo(assignment));
        Assert.Equal("one", assignment.Environment["A"]);
        Assert.Equal(2, assignment.Environment.Count);
    }

    [Theory]
    [InlineData("app")]
    [InlineData("group")]
    [InlineData("alwaysReady")]
    [InlineData("directory")]
    [InlineData("key")]
    [InlineData("value")]
    [InlineData("count")]
    public void Equality_UsesEveryFieldAndOrdinalStrings(string changedField)
    {
        WorkerAssignment original = Create(new Dictionary<string, string> { ["KEY"] = "Value" });
        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            [string.Equals(changedField, "key", StringComparison.Ordinal) ? "key" : "KEY"] =
                string.Equals(changedField, "value", StringComparison.Ordinal) ? "value" : "Value"
        };
        if (string.Equals(changedField, "count", StringComparison.Ordinal))
        {
            environment.Add("EXTRA", "Value");
        }

        WorkerAssignment changed = new(
            string.Equals(changedField, "app", StringComparison.Ordinal) ? "APP" : "app",
            string.Equals(changedField, "group", StringComparison.Ordinal) ? "HTTP" : "http",
            string.Equals(changedField, "alwaysReady", StringComparison.Ordinal),
            environment,
            string.Equals(changedField, "directory", StringComparison.Ordinal) ? "/HOME/site/wwwroot" : "/home/site/wwwroot");

        Assert.False(original.IsEquivalentTo(changed));
        Assert.False(changed.IsEquivalentTo(original));
    }

    [Fact]
    public void Equality_PreservesDistinctEnvironmentKeyCasing()
    {
        WorkerAssignment assignment = Create(new Dictionary<string, string> { ["KEY"] = "one", ["key"] = "two" });

        Assert.Equal(2, assignment.Environment.Count);
        Assert.Equal("one", assignment.Environment["KEY"]);
        Assert.Equal("two", assignment.Environment["key"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Construction_RejectsMissingIdentity(string? value)
    {
        Dictionary<string, string> environment = [];
        Assert.ThrowsAny<ArgumentException>(() => new WorkerAssignment(value!, "http", false, environment, "/app"));
        Assert.ThrowsAny<ArgumentException>(() => new WorkerAssignment("app", value!, false, environment, "/app"));
        Assert.ThrowsAny<ArgumentException>(() => new WorkerAssignment("app", "http", false, environment, value!));
    }

    [Fact]
    public void Construction_RejectsInvalidEnvironment()
    {
        Assert.Throws<ArgumentNullException>(() => Create(null!));
        Assert.Throws<ArgumentException>(() => Create(new Dictionary<string, string> { [string.Empty] = "value" }));
        Assert.Throws<ArgumentNullException>(() => Create(new Dictionary<string, string> { ["KEY"] = null! }));
    }

    [Fact]
    public void Equality_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Create(new Dictionary<string, string>()).IsEquivalentTo(null!));
    }

    private static WorkerAssignment Create(IReadOnlyDictionary<string, string> environment)
        => new("app", "http", false, environment, "/home/site/wwwroot");
}
