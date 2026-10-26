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
    [InlineData("startupMode")]
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
            string.Equals(changedField, "startupMode", StringComparison.Ordinal)
                ? WorkerStartupMode.SpecializationRequired : WorkerStartupMode.Preconfigured,
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
        Assert.ThrowsAny<ArgumentException>(() => new WorkerAssignment(WorkerStartupMode.Preconfigured, value!, "http", false, environment, "/app"));
        Assert.ThrowsAny<ArgumentException>(() => new WorkerAssignment(WorkerStartupMode.Preconfigured, "app", value!, false, environment, "/app"));
        Assert.ThrowsAny<ArgumentException>(() => new WorkerAssignment(WorkerStartupMode.SpecializationRequired, "app", "http", false, environment, value!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    [InlineData("/app")]
    public void Construction_PreconfiguredPreservesDirectoryWithoutApplyingValidationForSpecialization(string directory)
    {
        WorkerAssignment assignment = new(WorkerStartupMode.Preconfigured, "app", "http", false, new Dictionary<string, string>(), directory);

        Assert.Equal(WorkerStartupMode.Preconfigured, assignment.StartupMode);
        Assert.Equal(directory, assignment.FunctionAppDirectory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Construction_RejectsNullDirectoryInBothModes(bool specializationRequired)
    {
        WorkerStartupMode startupMode = specializationRequired ? WorkerStartupMode.SpecializationRequired : WorkerStartupMode.Preconfigured;
        Assert.Throws<ArgumentNullException>(() =>
            new WorkerAssignment(startupMode, "app", "http", false, new Dictionary<string, string>(), null!));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void Construction_RejectsUndefinedStartupMode(int startupMode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkerAssignment((WorkerStartupMode)startupMode, "app", "http", false, new Dictionary<string, string>(), "/app"));
    }

    [Fact]
    public void Equality_PreconfiguredEmptyDirectoryStillParticipatesInIdentity()
    {
        WorkerAssignment empty = new(WorkerStartupMode.Preconfigured, "app", "http", false, new Dictionary<string, string>(), string.Empty);
        WorkerAssignment whitespace = new(WorkerStartupMode.Preconfigured, "app", "http", false, new Dictionary<string, string>(), " ");

        Assert.False(empty.IsEquivalentTo(whitespace));
        Assert.False(whitespace.IsEquivalentTo(empty));
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
        => new(WorkerStartupMode.Preconfigured, "app", "http", false, environment, "/home/site/wwwroot");
}
