// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.HttpWorker
{
    public class HttpScriptInvocationResultExtensionsTests
    {
        [Theory]
        [InlineData("{\"statusCode\": \"204\"}")]
        [InlineData("{\"body\": \"hello\"}")]
        [InlineData("{\"body\": \"hello\", \"statusCode\":\"300\"}")]
        [InlineData("{\"StatusCode\":\"301\",\"Status\":\"Succeeded\",\"Body\":\"foobar\",\"Headers\":{\"header1\":\"header1Value\",\"header2\":\"header2Value\"}}", false)]
        [InlineData("SomeBlah")]
        public void GetHttpOutputBindingResponse_ReturnsExpected(string inputString, bool exceptionExpected = true)
        {
            Dictionary<string, object> outputsFromWorker = new Dictionary<string, object>();
            outputsFromWorker["httpOutput1"] = inputString;
            if (exceptionExpected)
            {
                Assert.Throws<HttpOutputDeserializationException>(() => HttpScriptInvocationResultExtensions.GetHttpOutputBindingResponse("httpOutput1", outputsFromWorker));
            }
            else
            {
                var actualResult = HttpScriptInvocationResultExtensions.GetHttpOutputBindingResponse("httpOutput1", outputsFromWorker);
                Assert.True(actualResult is ExpandoObject);
                Assert.Equal(inputString, JsonConvert.SerializeObject(actualResult));
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
            Assert.Null(result.Return);
            ExpandoObject output = result.Outputs["res"] as ExpandoObject;
            output.TryGetValue<object>("Body", out var resResult, ignoreCase: true);
            Assert.Equal("my world", resResult);
        }
    }
}
