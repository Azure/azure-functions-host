// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.AppCapabilities
{
    public class AppCapabilitiesOptionsValidatorTests
    {
        private readonly AppCapabilitiesOptionsValidator _validator;

        public AppCapabilitiesOptionsValidatorTests()
        {
            _validator = new AppCapabilitiesOptionsValidator();
        }

        [Fact]
        public void Validate_EmptyOptions_ReturnsSuccess()
        {
            var options = new AppCapabilitiesOptions();

            var result = _validator.Validate(null, options);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void Validate_SmallOptions_ReturnsSuccess()
        {
            var options = new AppCapabilitiesOptions
            {
                ["feature1"] = "value1",
                ["feature2"] = "value2",
                ["extensionSupport"] = "enabled"
            };

            var result = _validator.Validate(null, options);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void Validate_OptionsAtSizeLimit_ReturnsSuccess()
        {
            var options = CreateOptionsNearSizeLimit(ScriptConstants.MaxTriggersStringLength);

            var result = _validator.Validate(null, options);

            Assert.True(result.Succeeded);
        }

        [Fact]
        public void Validate_OptionsExceedingSizeLimit_ReturnsFailed()
        {
            var options = CreateOptionsNearSizeLimit(ScriptConstants.MaxTriggersStringLength + 1000);

            var result = _validator.Validate(null, options);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.FailureMessage);
            Assert.Contains("exceeds maximum", result.FailureMessage);
            Assert.Contains($"{ScriptConstants.MaxTriggersStringLength} bytes", result.FailureMessage);
        }

        [Fact]
        public void Validate_FailureMessage_ContainsActualAndMaxSize()
        {
            var options = CreateOptionsNearSizeLimit(ScriptConstants.MaxTriggersStringLength + 1000);

            var result = _validator.Validate(null, options);

            Assert.False(result.Succeeded);
            Assert.Contains("bytes) exceeds maximum", result.FailureMessage);
            Assert.Contains($"{ScriptConstants.MaxTriggersStringLength} bytes", result.FailureMessage);
        }

        private static AppCapabilitiesOptions CreateOptionsNearSizeLimit(int targetSizeBytes)
        {
            var options = new AppCapabilitiesOptions();

            // Build a large value string to approach the size limit
            // JSON serialization adds overhead for keys, quotes, colons, braces, etc.
            // Account for JSON structure overhead: {"key":"value"} adds ~10-15 chars per entry
            const int overheadPerEntry = 20;
            int valueSize = (targetSizeBytes / 10) - overheadPerEntry;

            if (valueSize < 1)
            {
                valueSize = 1;
            }

            string largeValue = new string('x', valueSize);

            for (int i = 0; i < 10; i++)
            {
                options[$"capability{i}"] = largeValue;
            }

            return options;
        }
    }
}
