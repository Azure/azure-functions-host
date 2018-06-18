using Microsoft.AspNetCore.Http;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class RpcMessageConversionExtensionsTests
    {
        [Theory]
        [InlineData("application/x-www-form-urlencoded’", "say=Hi&to=Mom")]
        public void HttpObjects_StringBody(string expectedContentType, object body)
        {
            var headers = new HeaderDictionary();
            headers.Add("content-type", expectedContentType);
            HttpRequest request = HttpTestHelpers.CreateHttpRequest("GET", "http://localhost/api/httptrigger-scenarios", headers, body);

            var rpcRequestObject = request.ToRpc();
            Assert.Equal(body.ToString(), rpcRequestObject.Http.Body.String);

            string contentType;
            rpcRequestObject.Http.Headers.TryGetValue("content-type", out contentType);
            Assert.Equal(expectedContentType, contentType);
        }
    }
}
