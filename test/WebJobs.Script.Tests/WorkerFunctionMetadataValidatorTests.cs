// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public sealed class WorkerFunctionMetadataValidatorTests
{
    [Theory]
    [InlineData(null, "node", "node")]
    [InlineData("", "node", "node")]
    [InlineData("python", "node", "python")]
    [InlineData("python", "", "python")]
    [InlineData(null, "", "")]
    [InlineData(null, " ", " ")]
    [InlineData(null, null, null)]
    public void ValidateMetadata_DefaultPathRetainsConfiguredRuntimeAndCaching(
        string profileRuntime, string configuredRuntime, string expectedRuntime)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                [EnvironmentSettingNames.FunctionWorkerRuntime] = configuredRuntime,
            })
            .Build();
        Mock<IOptionsMonitor<ScriptJobHostOptions>> options = new();
        options.Setup(monitor => monitor.CurrentValue)
            .Returns(new ScriptJobHostOptions { ProfileWorkerRuntime = profileRuntime });
        ScriptHostWorkerRuntimeResolver resolver = new(configuration, options.Object);
        WorkerFunctionMetadataValidator validator = new(NullLogger.Instance, resolver);
        RawFunctionMetadata raw = CreateMetadata("HttpFunction", "worker-language");

        FunctionMetadata function = Assert.Single(validator.ValidateMetadata([raw]));

        Assert.Equal(expectedRuntime, function.Language);
        Assert.Equal("worker-language", raw.Metadata.Language);
        Assert.Empty(validator.FunctionErrors);

        configuration[EnvironmentSettingNames.FunctionWorkerRuntime] = "changed-runtime";
        Assert.Equal(expectedRuntime ?? "changed-runtime", resolver.GetWorkerRuntime("caller-language"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "host-runtime")]
    [InlineData("profile-runtime", "host-runtime")]
    public void ValidateMetadata_WorkerAuthorityDoesNotReadOrPopulateRuntimeCache(string profileRuntime, string configuredRuntime)
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        configuration.Setup(config => config[EnvironmentSettingNames.FunctionWorkerRuntime]).Returns(configuredRuntime);
        Mock<IOptionsMonitor<ScriptJobHostOptions>> options = new();
        options.Setup(monitor => monitor.CurrentValue).Returns(new ScriptJobHostOptions { ProfileWorkerRuntime = profileRuntime });
        ScriptHostWorkerRuntimeResolver resolver = new(configuration.Object, options.Object);
        WorkerFunctionMetadataValidator validator = new(NullLogger.Instance, resolver);

        FunctionMetadata function = Assert.Single(validator.ValidateMetadata(
            [CreateMetadata("HttpFunction", "python")], preserveWorkerLanguage: true));

        Assert.Equal("python", function.Language);
        Assert.Empty(validator.FunctionErrors);
        configuration.Verify(config => config[EnvironmentSettingNames.FunctionWorkerRuntime], Times.Never);
        options.Verify(monitor => monitor.CurrentValue, Times.Never);
        Assert.Equal(profileRuntime ?? configuredRuntime, resolver.GetWorkerRuntime());
    }

    [Fact]
    public void ValidateMetadata_WorkerAuthorityIgnoresPreviouslyCachedProfileRuntime()
    {
        Mock<IConfiguration> configuration = new(MockBehavior.Strict);
        ScriptJobHostOptions hostOptions = new() { ProfileWorkerRuntime = "profile-runtime" };
        Mock<IOptionsMonitor<ScriptJobHostOptions>> options = new();
        options.Setup(monitor => monitor.CurrentValue).Returns(hostOptions);
        ScriptHostWorkerRuntimeResolver resolver = new(configuration.Object, options.Object);
        Assert.Equal("profile-runtime", resolver.GetWorkerRuntime("caller-language"));
        hostOptions.ProfileWorkerRuntime = "changed-profile";
        WorkerFunctionMetadataValidator validator = new(NullLogger.Instance, resolver);

        FunctionMetadata function = Assert.Single(validator.ValidateMetadata(
            [CreateMetadata("HttpFunction", "python")], preserveWorkerLanguage: true));

        Assert.Equal("python", function.Language);
        Assert.Equal("profile-runtime", resolver.GetWorkerRuntime("another-caller-language"));
        Assert.Empty(validator.FunctionErrors);
    }

    private static RawFunctionMetadata CreateMetadata(string name, string language)
        => new()
        {
            Metadata = new FunctionMetadata { Name = name, Language = language },
            Bindings = ["""{"type":"httpTrigger","name":"req","direction":"in"}"""],
        };
}
