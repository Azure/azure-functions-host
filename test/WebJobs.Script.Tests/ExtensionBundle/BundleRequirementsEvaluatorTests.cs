// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Script.Conditions;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.ExtensionBundle
{
    public class BundleRequirementsEvaluatorTests
    {
        private readonly TestLogger _logger = new TestLogger("test");
        private readonly TestEnvironment _environment = new TestEnvironment();
        private readonly TestSystemRuntimeInformation _runtimeInfo = new TestSystemRuntimeInformation();

        [Fact]
        public void EvaluateFromFile_MissingFile_ReturnsTrue()
        {
            var evaluator = CreateEvaluator();
            Assert.True(evaluator.EvaluateFromFile(Path.Combine(Path.GetTempPath(), "does-not-exist.json")));
        }

        [Fact]
        public void EvaluateFromFile_NoRequirementsKey_ReturnsTrue()
        {
            var path = WriteTempJson("{ \"id\": \"x\", \"version\": \"1.0.0\" }");
            try
            {
                Assert.True(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EvaluateFromFile_EmptyRequirementsArray_ReturnsTrue()
        {
            var path = WriteTempJson("{ \"id\": \"x\", \"version\": \"1.0.0\", \"requirements\": [] }");
            try
            {
                Assert.True(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EvaluateFromFile_AllPass_ReturnsTrue()
        {
            // hostProperty Platform=Linux (TestSystemRuntimeInformation returns Linux by default)
            var path = WriteTempJson(@"{
                ""id"": ""x"",
                ""version"": ""1.0.0"",
                ""requirements"": [
                    { ""conditionType"": ""hostProperty"", ""conditionName"": ""Platform"", ""conditionExpression"": ""^LINUX$"" }
                ]
            }");
            try
            {
                Assert.True(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EvaluateFromFile_OneFails_ReturnsFalse()
        {
            // Requires Platform=Windows — but TestSystemRuntimeInformation reports Linux
            var path = WriteTempJson(@"{
                ""id"": ""x"",
                ""version"": ""1.0.0"",
                ""requirements"": [
                    { ""conditionType"": ""hostProperty"", ""conditionName"": ""Platform"", ""conditionExpression"": ""^Windows$"" }
                ]
            }");
            try
            {
                Assert.False(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EvaluateFromFile_AndSemantics_OnePassOneFail_ReturnsFalse()
        {
            var path = WriteTempJson(@"{
                ""id"": ""x"",
                ""version"": ""1.0.0"",
                ""requirements"": [
                    { ""conditionType"": ""hostProperty"", ""conditionName"": ""Platform"", ""conditionExpression"": ""^LINUX$"" },
                    { ""conditionType"": ""hostProperty"", ""conditionName"": ""Platform"", ""conditionExpression"": ""^Windows$"" }
                ]
            }");
            try
            {
                Assert.False(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EvaluateFromFile_MalformedJson_ReturnsTrue()
        {
            // Malformed metadata cannot be evaluated; evaluator intentionally falls back to "no
            // requirements" rather than hard-failing — matches backward-compat semantics.
            var path = WriteTempJson("not-json-at-all");
            try
            {
                Assert.True(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void EvaluateFromFile_UnknownConditionType_ReturnsFalse()
        {
            var path = WriteTempJson(@"{
                ""id"": ""x"",
                ""version"": ""1.0.0"",
                ""requirements"": [
                    { ""conditionType"": ""notARealType"", ""conditionName"": ""X"", ""conditionExpression"": ""y"" }
                ]
            }");
            try
            {
                Assert.False(CreateEvaluator().EvaluateFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private BundleRequirementsEvaluator CreateEvaluator()
        {
            var provider = new BundleConditionProvider(_logger, _environment, _runtimeInfo);
            return new BundleRequirementsEvaluator(provider, _logger);
        }

        private static string WriteTempJson(string content)
        {
            var path = Path.Combine(Path.GetTempPath(), "bundle-req-test-" + System.Guid.NewGuid() + ".json");
            File.WriteAllText(path, content);
            return path;
        }
    }
}
