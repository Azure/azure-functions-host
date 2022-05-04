// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class HttpScriptInvocationContextExtensionTests
    {
        [Fact]
        public async Task ToHttpRequestTest()
        {
            HttpScriptInvocationContext testContext = new HttpScriptInvocationContext
            {
                Data = new Dictionary<string, object>(),
                Metadata = new Dictionary<string, object>(),
            };
            testContext.Data.Add("randomDataKey", "randomDataValue");
            testContext.Metadata.Add("randomMetaDataKey", "randomMetaDataValue");

            string expectedUri = "http://randomhost";
            HttpRequestMessage result = testContext.ToHttpRequestMessage(expectedUri);

            string resultContent = await result.Content.ReadAsStringAsync();
            HttpScriptInvocationContext expected = JsonConvert.DeserializeObject<HttpScriptInvocationContext>(resultContent);

            Assert.Equal(result.RequestUri.ToString(), new Uri(expectedUri).ToString());
            Assert.Equal(result.Method, HttpMethod.Post);
            foreach (string key in testContext.Data.Keys)
            {
                Assert.Equal(testContext.Data[key], expected.Data[key]);
            }
            foreach (string key in testContext.Metadata.Keys)
            {
                Assert.Equal(testContext.Metadata[key], expected.Metadata[key]);
            }
        }

        [Fact]
        public void TestSetRetryContext_NoRetry()
        {
            ScriptInvocationContext scriptInvocationContext = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
            };

            HttpScriptInvocationContext testContext = new HttpScriptInvocationContext()
            {
                Data = new Dictionary<string, object>(),
                Metadata = new Dictionary<string, object>()
            };

            ScriptInvocationContextExtensions.SetRetryContext(scriptInvocationContext, testContext);

            Assert.Empty(testContext.Metadata);
        }

        [Fact]
        public void TestSetRetryContext_Retry()
        {
            ScriptInvocationContext context = new ScriptInvocationContext()
            {
                ExecutionContext = new ExecutionContext()
                {
                    RetryContext = new Host.RetryContext()
                    {
                        RetryCount = 1,
                        MaxRetryCount = 2,
                        Exception = new Exception("test")
                    }
                }
            };
            HttpScriptInvocationContext testContext = new HttpScriptInvocationContext()
            {
                Data = new Dictionary<string, object>(),
                Metadata = new Dictionary<string, object>()
            };
            ScriptInvocationContextExtensions.SetRetryContext(context, testContext);
            var retryContext = (RetryContext)testContext.Metadata["RetryContext"];
            Assert.NotNull(retryContext);
            Assert.Equal(retryContext.RetryCount, context.ExecutionContext.RetryContext.RetryCount);
            Assert.Equal(retryContext.MaxRetryCount, context.ExecutionContext.RetryContext.MaxRetryCount);
            Assert.NotNull(retryContext.Exception);
        }
    }
}
