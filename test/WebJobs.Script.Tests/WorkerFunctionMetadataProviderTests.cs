// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WorkerFunctionMetadataProviderTests
    {
        private TestMetricsLogger _testMetricsLogger;
        private ScriptApplicationHostOptions _scriptApplicationHostOptions;

        public WorkerFunctionMetadataProviderTests()
        {
            _testMetricsLogger = new TestMetricsLogger();
            _scriptApplicationHostOptions = new ScriptApplicationHostOptions();
        }

        [Theory]
        [InlineData("")]
        [InlineData("host")]
        [InlineData("Host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
        [InlineData("function.test")]
        [InlineData("function0.1")]
        public void ValidateFunctionName_ThrowsOnInvalidName(string functionName)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateName(functionName);
            });

            Assert.Equal(string.Format("'{0}' is not a valid function name.", functionName), ex.Message);
        }

        [Theory]
        [InlineData("testwithhost")]
        [InlineData("hosts")]
        [InlineData("myfunction")]
        [InlineData("myfunction-test")]
        [InlineData("myfunction_test")]
        public void ValidateFunctionName_DoesNotThrowOnValidName(string functionName)
        {
            try
            {
                WorkerFunctionMetadataProvider.ValidateName(functionName);
            }
            catch (InvalidOperationException)
            {
                Assert.True(false, $"Valid function name {functionName} failed validation.");
            }
        }

        [Fact]
        public void ValidateBindings_NoBindings_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();

            var ex = Assert.Throws<FormatException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("At least one binding must be declared.", ex.Message);
        }

        [Fact]
        public void ValidateBindings_DuplicateBindingNames_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"test\",\"direction\": \"in\", \"blobPath\": \"test\"}");
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("Multiple bindings with name 'dupe' discovered. Binding names must be unique.", ex.Message);
        }

        [Fact]
        public void ValidateBindings_NoTriggerBinding_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"Blob\",\"name\": \"test\"}");

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal("No trigger binding specified. A function must have a trigger input binding.", ex.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("_binding")]
        [InlineData("binding-test")]
        [InlineData("binding name")]
        public void ValidateBindings_InvalidName_Throws(string bindingName)
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");
            rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + bindingName + "\"}");

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal($"The binding name {bindingName} is invalid. Please assign a valid name to the binding.", ex.Message);
        }

        [Theory]
        [InlineData("bindingName")]
        [InlineData("binding1")]
        [InlineData(ScriptConstants.SystemReturnParameterBindingName)]
        public void ValidateBindings_ValidName_DoesNotThrow(string bindingName)
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");

            if (bindingName == ScriptConstants.SystemReturnParameterBindingName)
            {
                rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + bindingName + "\", \"direction\": \"out\"}");
            }
            else
            {
                rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + bindingName + "\"}");
            }

            try
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            }
            catch (ArgumentException)
            {
                Assert.True(false, $"Valid binding name '{bindingName}' failed validation.");
            }
        }

        [Fact]
        public void ValidateBindings_OutputNameWithoutDirection_Throws()
        {
            FunctionMetadata functionMetadata = new FunctionMetadata();
            List<string> rawBindings = new List<string>();
            rawBindings.Add("{\"type\": \"BlobTrigger\",\"name\": \"dupe\",\"direction\": \"in\"}");
            rawBindings.Add("{\"type\": \"Blob\",\"name\": \"" + ScriptConstants.SystemReturnParameterBindingName + "\"}");

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                WorkerFunctionMetadataProvider.ValidateBindings(rawBindings, functionMetadata);
            });

            Assert.Equal($"{ScriptConstants.SystemReturnParameterBindingName} bindings must specify a direction of 'out'.", ex.Message);
        }
    }
}
