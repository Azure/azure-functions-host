// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Xunit;
using static Microsoft.Azure.WebJobs.Script.Tests.FunctionInvokerBaseTests;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ExternalConfigurationStartupValidatorTests
    {
        private const string _testName = "TestFunction";
        private const string _lookup = "lookup";
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly BindingMetadata _bindingMetadata;

        public ExternalConfigurationStartupValidatorTests()
        {
            _bindingMetadata = new BindingMetadata();
            _bindingMetadata.Type = "testTrigger";
            _bindingMetadata.Raw = JObject.FromObject(new
            {
                key0 = _lookup,
                key1 = "nonlookup",
                key2 = true,
                key3 = 1234
            });

            FunctionMetadata functionMetadata = new FunctionMetadata();
            functionMetadata.Name = _testName;
            functionMetadata.Bindings.Add(_bindingMetadata);

            ICollection<FunctionMetadata> metadata = new Collection<FunctionMetadata>();
            metadata.Add(functionMetadata);

            _metadataManager = new MockMetadataManager(metadata);
        }

        [Fact]
        public void ChangedLookup_ReturnsInvalidValues()
        {
            var configBuilder = AddInMemory(new ConfigurationBuilder(), _lookup, "abc");

            var orig = configBuilder.Build();

            // Simulate changing the lookup in an ExternalConfigurationStartup
            AddInMemory(configBuilder, _lookup, "123");

            var current = configBuilder.Build();

            var validator = new ExternalConfigurationStartupValidator(current, _metadataManager);
            var invalidValues = validator.Validate(orig);

            Assert.Equal(1, invalidValues.Count);
            string invalidValue = invalidValues[_testName].Single();
            Assert.Equal(_lookup, invalidValue);
        }

        [Fact]
        public void NoChanges_ReturnsEmpty()
        {
            var configBuilder = AddInMemory(new ConfigurationBuilder(), _lookup, "abc");

            var orig = configBuilder.Build();

            // Simulate changing another value an ExternalConfigurationStartup
            AddInMemory(configBuilder, "anotherLookup", "123");

            var current = configBuilder.Build();

            var validator = new ExternalConfigurationStartupValidator(current, _metadataManager);
            var invalidValues = validator.Validate(orig);

            Assert.NotNull(invalidValues);
            Assert.Equal(0, invalidValues.Count);
        }

        private static IConfigurationBuilder AddInMemory(IConfigurationBuilder configBuilder, string key, string value)
        {
            return configBuilder
                .AddInMemoryCollection(new Dictionary<string, string>()
                {
                    { key, value }
                });
        }
    }
}
