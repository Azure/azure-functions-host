using Microsoft.AspNetCore.Http;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{

    public class RpcMessageConversionExtensionsTests
    {
        [Theory]
        [InlineData("application/json", "{\"name\": \"test\" }")]
        [InlineData("application/x-www-form-urlencoded’", "say=Hi&to=Mom")]
        public void HttpObjects(string expectedContentType, object body)
        {
            var headers = new HeaderDictionary();
            headers.Add("content-type", "application/json");
            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", "http://localhost/api/httptrigger-scenarios", headers, body);

            var rpcRequestObject = request.ToRpc();
            Assert.Equal(body.ToString(), JSON.stringify(rpcRequestObject.Http.Body.Json));
            Assert.Equal(expectedContentType, rpcRequestObject.Http.Headers.ToString());

        }
    }
}
