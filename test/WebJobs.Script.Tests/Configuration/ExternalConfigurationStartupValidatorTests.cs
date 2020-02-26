// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Configuration;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.FunctionInvokerBaseTests;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ExternalConfigurationStartupValidatorTests
    {
        [Fact]
        public void Validates_AllProperties()
        {
            BindingMetadata bindingMetadata = new BindingMetadata();
            bindingMetadata.Type = "testTrigger";
            bindingMetadata.Raw["key"] = "lookup";

            FunctionMetadata functionMetadata = new FunctionMetadata();
            functionMetadata.Bindings.Add(bindingMetadata);

            ICollection<FunctionMetadata> metadata = new Collection<FunctionMetadata>();
            metadata.Add(functionMetadata);

            var metadataManager = new MockMetadataManager(metadata);

            var configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>()
                {
                    { "lookup", "abc" }
                });

            var orig = configBuilder.Build();

            configBuilder
                .AddInMemoryCollection(new Dictionary<string, string>()
                {
                    { "lookup", "123" }
                });

            var validator = new ExternalConfigurationStartupValidator(orig, metadataManager);
            var invalidServices = validator.Validate(orig);
        }
    }
}
