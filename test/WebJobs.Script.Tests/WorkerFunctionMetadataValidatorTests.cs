// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public sealed class WorkerFunctionMetadataValidatorTests
{
    [Theory]
    [InlineData("worker-language")]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateMetadata_DefaultPathUsesConfiguredRuntime(string workerLanguage)
    {
        Mock<IWorkerRuntimeResolver> resolver = new(MockBehavior.Strict);
        resolver.Setup(value => value.GetWorkerRuntime(It.IsAny<string>())).Returns("node");
        WorkerFunctionMetadataValidator validator = new(NullLogger.Instance, resolver.Object);
        RawFunctionMetadata raw = new()
        {
            Metadata = new FunctionMetadata { Name = "HttpFunction", Language = workerLanguage },
            Bindings = ["""{"type":"httpTrigger","name":"req","direction":"in"}"""],
        };

        FunctionMetadata function = Assert.Single(validator.ValidateMetadata([raw]));

        Assert.Equal("node", function.Language);
        Assert.Equal(workerLanguage, raw.Metadata.Language);
        Assert.Empty(validator.FunctionErrors);
        resolver.Verify(value => value.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateMetadata_PreserveWorkerLanguageRejectsMissingLanguage(string workerLanguage)
    {
        Mock<IWorkerRuntimeResolver> resolver = new(MockBehavior.Strict);
        WorkerFunctionMetadataValidator validator = new(NullLogger.Instance, resolver.Object);
        RawFunctionMetadata invalid = new()
        {
            Metadata = new FunctionMetadata { Name = "InvalidFunction", Language = workerLanguage },
            Bindings = ["""{"type":"httpTrigger","name":"req","direction":"in"}"""],
        };
        RawFunctionMetadata valid = new()
        {
            Metadata = new FunctionMetadata { Name = "ValidFunction", Language = "Python" },
            Bindings = ["""{"type":"httpTrigger","name":"req","direction":"in"}"""],
        };

        FunctionMetadata function = Assert.Single(validator.ValidateMetadata([invalid, valid], preserveWorkerLanguage: true));

        Assert.Equal("ValidFunction", function.Name);
        Assert.Equal("Python", function.Language);
        var error = Assert.Single(validator.FunctionErrors);
        Assert.Equal("InvalidFunction", error.Key);
        Assert.Equal("The worker returned function metadata without a language.", Assert.Single(error.Value));
        Assert.Equal(workerLanguage, invalid.Metadata.Language);
        resolver.Verify(value => value.GetWorkerRuntime(It.IsAny<string>()), Times.Never);
    }
}
