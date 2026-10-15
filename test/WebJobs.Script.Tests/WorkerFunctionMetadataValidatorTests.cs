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
    [Fact]
    public void ValidateMetadata_DefaultPathUsesConfiguredRuntime()
    {
        Mock<IWorkerRuntimeResolver> resolver = new(MockBehavior.Strict);
        resolver.Setup(value => value.GetWorkerRuntime(It.IsAny<string>())).Returns("node");
        WorkerFunctionMetadataValidator validator = new(NullLogger.Instance, resolver.Object);
        RawFunctionMetadata raw = new()
        {
            Metadata = new FunctionMetadata { Name = "HttpFunction", Language = "worker-language" },
            Bindings = ["""{"type":"httpTrigger","name":"req","direction":"in"}"""],
        };

        FunctionMetadata function = Assert.Single(validator.ValidateMetadata([raw]));

        Assert.Equal("node", function.Language);
        Assert.Equal("worker-language", raw.Metadata.Language);
        Assert.Empty(validator.FunctionErrors);
        resolver.Verify(value => value.GetWorkerRuntime(It.IsAny<string>()), Times.Once);
    }
}
