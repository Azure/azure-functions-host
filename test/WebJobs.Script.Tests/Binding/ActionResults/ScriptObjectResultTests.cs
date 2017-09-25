using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Binding;
using Newtonsoft.Json.Linq;
using Xunit;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.Azure.WebJobs.Script.Tests.Binding.ActionResults
{
    public class ScriptObjectResultTests
    {
        [Fact]
        public void HasExpectedProperties()
        {
            var obj = JObject.Parse("{ \"a\": 1 }");
            var result = new ScriptObjectResult(obj, new Dictionary<string, object>());
            Assert.Equal(0, result.ContentTypes.Count);
            Assert.Equal(obj.ToString(), result.Value.ToString());
        }

        [Fact]
        public void AddsExistingContentType()
        {
            var obj = JObject.Parse("{ \"a\": 1 }");
            var result = new ScriptObjectResult(obj, new Dictionary<string, object>()
            {
                ["content-type"] = "application/json; charset=utf-8"
            });
            Assert.Equal("application/json; charset=utf-8", result.ContentTypes.First());
        }

        [Fact]
        public void AddsHeadersToResponse()
        {
            var obj = JObject.Parse("{ \"a\": 1 }");
            var result = new ScriptObjectResult(obj, new Dictionary<string, object>()
            {
                ["custom-header"] = "header"
            });
            var context = new DefaultHttpContext();
            result.AddResponseHeaders(context);
            Assert.Equal("header", context.Response.Headers["custom-header"]);
        }
    }
}
