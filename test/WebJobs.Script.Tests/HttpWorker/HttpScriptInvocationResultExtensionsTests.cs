// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpWorker
{
    public class HttpScriptInvocationResultExtensionsTests
    {
        [Theory]
        [InlineData("{\"statusCode\": \"204\"}", "{\"StatusCode\":\"204\",\"Status\":null,\"Body\":null,\"Headers\":null}")]
        [InlineData("{}", "{\"StatusCode\":null,\"Status\":null,\"Body\":null,\"Headers\":null}")]
        [InlineData("{\"body\": \"hello\"}", "{\"StatusCode\":null,\"Status\":null,\"Body\":\"hello\",\"Headers\":null}")]
        [InlineData("{\"body\": \"hello\", \"statusCode\":\"300\"}", "{\"StatusCode\":\"300\",\"Status\":null,\"Body\":\"hello\",\"Headers\":null}")]
        [InlineData("{\"body\":\"foobar\",\"statusCode\":\"301\",\"headers\":{\"header1\":\"header1Value\",\"header2\":\"header2Value\"}}", "{\"StatusCode\":\"301\",\"Status\":null,\"Body\":\"foobar\",\"Headers\":{\"header1\":\"header1Value\",\"header2\":\"header2Value\"}}")]
        [InlineData("SomeBlah", "", true)]
        public void GetHttpOutputBindingResponse_ReturnsExpected(string inputString, string expectedOutput = "", bool isExceptionExpected = false)
        {
            Dictionary<string, object> outputsFromWorker = new Dictionary<string, object>();
            outputsFromWorker["httpOutput1"] = inputString;
            if (isExceptionExpected)
            {
                Assert.Throws<InvalidOperationException>(() => HttpScriptInvocationResultExtensions.GetHttpOutputBindingResponse("httpOutput1", outputsFromWorker));
            }
            else
            {
                var actualResult = HttpScriptInvocationResultExtensions.GetHttpOutputBindingResponse("httpOutput1", outputsFromWorker);
                Assert.True(actualResult is ExpandoObject);
                Assert.Equal(expectedOutput, JsonConvert.SerializeObject(actualResult));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ToScripInvocationResultTests(bool includeReturnValue)
        {
            var testInvocationContext = HttpWorkerTestUtilities.GetSimpleHttpTriggerScriptInvocationContext("test", Guid.NewGuid(), new TestLogger("test"));
            var testHttpInvocationResult = HttpWorkerTestUtilities.GetHttpScriptInvocationResultWithJsonRes();
            if (includeReturnValue)
            {
                testHttpInvocationResult.ReturnValue = "Hello return";
            }
            var result = testHttpInvocationResult.ToScriptInvocationResult(testInvocationContext);
            if (includeReturnValue)
            {
                Assert.Equal(testHttpInvocationResult.ReturnValue, result.Return);
            }
            else
            {
                Assert.Null(result.Return);
            }
            ExpandoObject output = result.Outputs["res"] as ExpandoObject;
            output.TryGetValue<object>("Body", out var resResult, ignoreCase: true);
            Assert.Equal("my world", resResult);
        }
    }
}
