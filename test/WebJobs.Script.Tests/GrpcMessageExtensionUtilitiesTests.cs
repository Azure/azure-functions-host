// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public sealed class GrpcMessageExtensionUtilitiesTests
    {
        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Accepted)]
        [InlineData(HttpStatusCode.NotFound)]
        public async Task ConvertFromHttpMessageToExpando_ShouldReturnExpandoObject_WithStringResponseAndStatusCode(HttpStatusCode httpStatusCode)
        {
            var responseBodyString = "Hello world";
            var rpcHttp = await CreateRpcHttpAsync(responseBodyString, httpStatusCode);
            rpcHttp.EnableContentNegotiation = true;

            dynamic result = GrpcMessageExtensionUtilities.ConvertFromHttpMessageToExpando(rpcHttp);

            AssertValidHttpResponseObject(result, "GET", httpStatusCode, responseBodyString, enableContentNegotiation: true);

            var headers = result.headers as IDictionary<string, object>;
            Assert.Empty(headers);

            var cookies = (IEnumerable<object>)result.cookies;
            Assert.Empty(cookies);
        }

        [Fact]
        public async Task ConvertFromHttpMessageToExpando_ShouldReturnExpandoObject_WithPOCOResponse()
        {
            var bookPoco = new Book("Book1", "Author1", 49.99, new DateTime(2012, 1, 1), true, ["C#", "CLR"]);
            var responseHeaders = new MapField<string, string>
                {
                    { "header1", "value1" },
                    { "header2", "value2" }
                };
            var responseCookies = new RepeatedField<RpcHttpCookie>
                {
                    new RpcHttpCookie
                    {
                        Name = "cookie1",
                        Value = "value1",
                        Domain = new NullableString { Value = "microsoft.com" }
                    }
                };
            var rpcHttp = await CreateRpcHttpAsync(bookPoco, headers: responseHeaders, cookies: responseCookies);

            dynamic result = GrpcMessageExtensionUtilities.ConvertFromHttpMessageToExpando(rpcHttp);

            AssertValidHttpResponseObject(result, "GET", HttpStatusCode.OK);

            var headers = result.headers as IDictionary<string, object>;
            Assert.Equal(2, headers.Count);
            Assert.Equal("value1", headers["header1"]);
            Assert.Equal("value2", headers["header2"]);

            var cookies = (IEnumerable<object>)result.cookies;
            Assert.Single(cookies);

            var body = result.body;
            Assert.NotNull(body);

            if (body is byte[] bodyAsByteArray)
            {
                var actualBook = JsonSerializer.Deserialize<Book>(Encoding.UTF8.GetString(bodyAsByteArray));

                Assert.Equal(bookPoco.Title, actualBook.Title);
                Assert.Equal(bookPoco.Author, actualBook.Author);
                Assert.Equal(bookPoco.Price, actualBook.Price);
                Assert.Equal(bookPoco.PublishedDate.ToString("yyyy-MM-dd"), actualBook.PublishedDate.ToString("yyyy-MM-dd"));
                Assert.Equal(bookPoco.IsBestSeller, actualBook.IsBestSeller);
                Assert.Equal(bookPoco.Tags, actualBook.Tags);
            }
        }

        [Fact]
        public void ConvertFromHttpMessageToExpando_ShouldReturnNull_WhenInputIsNull()
        {
            var result = GrpcMessageExtensionUtilities.ConvertFromHttpMessageToExpando(null);

            Assert.Null(result);
        }

        private static async Task<RpcHttp> CreateRpcHttpAsync<T>(T body,
            HttpStatusCode httpStatusCode = HttpStatusCode.OK, string method = "GET",
            MapField<string, string> headers = null, RepeatedField<RpcHttpCookie> cookies = null)
        {
            var response = new RpcHttp
            {
                Method = method,
                Query = { },
                StatusCode = ((int)httpStatusCode).ToString(),
                Headers = { },
                Cookies = { },
                Body = await CreateResponseBodyTypedDataAsync(body)
            };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    response.Headers.Add(header.Key, header.Value);
                }
            }

            if (cookies != null)
            {
                foreach (var cookie in cookies)
                {
                    response.Cookies.Add(cookie);
                }
            }

            return response;
        }

        private static async Task<TypedData> CreateResponseBodyTypedDataAsync<T>(T value)
        {
            TypedData typedData = new();

            using var memoryStream = new MemoryStream();
            if (value is string stringValue)
            {
                await memoryStream.WriteAsync(Encoding.UTF8.GetBytes(stringValue));
            }
            else
            {
                await JsonSerializer.SerializeAsync(memoryStream, value, typeof(T));
            }

            memoryStream.Position = 0;
            typedData.Bytes = await ByteString.FromStreamAsync(memoryStream);

            return typedData;
        }

        private static void AssertValidHttpResponseObject(dynamic expando, string expectedMethod, HttpStatusCode expectedStatusCode, string expectedResponseBodyContent = null, bool enableContentNegotiation = false)
        {
            Assert.NotNull(expando);
            Assert.Equal(expectedMethod, expando.method);
            Assert.Equal(((int)expectedStatusCode).ToString(), expando.statusCode);
            Assert.Equal(enableContentNegotiation, expando.enableContentNegotiation);

            if (expectedResponseBodyContent is null || expando.body is not byte[] bodyAsByteArray)
            {
                return;
            }

            var bodyAsString = Encoding.UTF8.GetString(bodyAsByteArray);
            Assert.Equal(expectedResponseBodyContent, bodyAsString);
        }
    }

    internal record Book(string Title, string Author, double Price, DateTime PublishedDate, bool IsBestSeller, List<string> Tags);
}
