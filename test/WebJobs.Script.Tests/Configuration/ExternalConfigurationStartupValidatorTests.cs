// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        private const string _functionName1 = "TestFunction1";
        private const string _functionName2 = "TestFunction2";
        private const string _lookup = "lookup";
        private const string _bindingExpressionLookup = "bindingExpression";
        private readonly string _bindingExpression = $"%{_bindingExpressionLookup}%";
        private readonly IFunctionMetadataManager _metadataManager;
        private readonly IConfigurationBuilder _configBuilder;

        public ExternalConfigurationStartupValidatorTests()
        {
            BindingMetadata bindingMetadata1 = new BindingMetadata
            {
                Type = "testTrigger",
                Raw = JObject.FromObject(new
                {
                    key0 = _lookup,
                    key1 = "nonlookup",
                    key2 = true,
                    key3 = 1234,
                })
            };

            FunctionMetadata functionMetadata1 = new FunctionMetadata
            {
                Name = _functionName1
            };

            functionMetadata1.Bindings.Add(bindingMetadata1);

            BindingMetadata bindingMetadata2 = new BindingMetadata
            {
                Type = "testTrigger",
                Raw = JObject.FromObject(new
                {
                    key0 = _bindingExpression
                })
            };

            FunctionMetadata functionMetadata2 = new FunctionMetadata
            {
                Name = _functionName2
            };

            functionMetadata2.Bindings.Add(bindingMetadata2);

            ICollection<FunctionMetadata> metadata = new Collection<FunctionMetadata>
            {
                functionMetadata1,
                functionMetadata2
            };

            _metadataManager = new MockMetadataManager(metadata);

            _configBuilder = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>()
                {
                    { _lookup, "abc" },
                    { _bindingExpressionLookup, "def" }
                });
        }

        [Fact]
        public void MultipleTriggers_Succeeds()
        {
            BindingMetadata bindingMetadata1 = new BindingMetadata
            {
                Type = "ATrigger",
                Raw = JObject.FromObject(new
                {
                    key0 = "foo"
                })
            };

            BindingMetadata bindingMetadata2 = new BindingMetadata
            {
                Type = "BTrigger",
                Raw = JObject.FromObject(new
                {
                    key0 = "bar"
                })
            };

            FunctionMetadata functionMetadata = new FunctionMetadata
            {
                Name = "test"
            };

            functionMetadata.Bindings.Add(bindingMetadata1);
            functionMetadata.Bindings.Add(bindingMetadata2);

            ICollection<FunctionMetadata> metadata = new Collection<FunctionMetadata>
            {
                functionMetadata
            };

            var metadataManager = new MockMetadataManager(metadata);
            var config = _configBuilder.Build();

            var validator = new ExternalConfigurationStartupValidator(config, metadataManager);
            var invalidValues = validator.Validate(config);
            Assert.Empty(invalidValues);
        }

        [Fact]
        public void ChangedLookup_ReturnsInvalidValues()
        {
            var orig = _configBuilder.Build();

            // Simulate changing the lookup in an ExternalConfigurationStartup
            AddInMemory(_configBuilder, _lookup, "123");

            var current = _configBuilder.Build();

            var validator = new ExternalConfigurationStartupValidator(current, _metadataManager);
            var invalidValues = validator.Validate(orig);

            Assert.Equal(1, invalidValues.Count);
            string invalidValue = invalidValues[_functionName1].Single();
            Assert.Equal(_lookup, invalidValue);
        }

        [Fact]
        public void ChangedBindingExpression_ReturnsInvalidValues()
        {
            var orig = _configBuilder.Build();

            // Simulate changing the binding expression in an ExternalConfigurationStartup
            AddInMemory(_configBuilder, _bindingExpressionLookup, "456");

            var current = _configBuilder.Build();

            var validator = new ExternalConfigurationStartupValidator(current, _metadataManager);
            var invalidValues = validator.Validate(orig);

            Assert.Equal(1, invalidValues.Count);
            string invalidValue = invalidValues[_functionName2].Single();
            Assert.Equal(_bindingExpression, invalidValue);
        }

        [Fact]
        public void MultipleChangedLookups_ReturnsInvalidValues()
        {
            var orig = _configBuilder.Build();

            // Simulate changing the lookup in an ExternalConfigurationStartup
            AddInMemory(_configBuilder, _lookup, "123");
            AddInMemory(_configBuilder, _bindingExpressionLookup, "456");

            var current = _configBuilder.Build();

            var validator = new ExternalConfigurationStartupValidator(current, _metadataManager);
            var invalidValues = validator.Validate(orig);

            Assert.Equal(2, invalidValues.Count);

            var invalidValue1 = invalidValues[_functionName1].Single();
            var invalidValue2 = invalidValues[_functionName2].Single();

            Assert.Equal(_lookup, invalidValue1);
            Assert.Equal(_bindingExpression, invalidValue2);
        }

        [Fact]
        public void NoChanges_ReturnsEmpty()
        {
            var orig = _configBuilder.Build();

            // Simulate changing another value an ExternalConfigurationStartup
            AddInMemory(_configBuilder, "anotherLookup", "123");

            var current = _configBuilder.Build();

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
