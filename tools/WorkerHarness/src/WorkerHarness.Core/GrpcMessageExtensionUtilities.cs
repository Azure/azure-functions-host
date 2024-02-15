using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Newtonsoft.Json;
using System.Dynamic;
namespace WorkerHarness.Core
{
    internal static class GrpcMessageConversionExtensions
    {
        private static readonly JsonSerializerSettings _datetimeSerializerSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
        private static readonly TypedData EmptyRpcHttp = new() { Http = new() };

        public static object ToObject(this TypedData typedData)
        {
            if (typedData.DataCase == TypedData.DataOneofCase.String)
            {
                return typedData.String;
            }
            else if (typedData.DataCase == TypedData.DataOneofCase.Json)
            {
                return JsonConvert.DeserializeObject(typedData.Json, _datetimeSerializerSettings);
            }
            else if (typedData.DataCase == TypedData.DataOneofCase.Bytes || typedData.DataCase == TypedData.DataOneofCase.Stream)
            {
                return typedData.Bytes.ToByteArray();
            }
            else if (typedData.DataCase == TypedData.DataOneofCase.Http)
            {
                return GrpcMessageExtensionUtilities.ConvertFromHttpMessageToExpando(typedData.Http);
            }
            else if (typedData.DataCase == TypedData.DataOneofCase.Int)
            {
                return typedData.Int;
            }
            else if (typedData.DataCase == TypedData.DataOneofCase.Double)
            {
                return typedData.Double;
            }
            else
            {
                throw new InvalidOperationException($"Unknown RpcDataType: {typedData.DataCase}");
            }
        }
    }

    internal static class GrpcMessageExtensionUtilities
    {
        public static HttpResponseData ConvertFromHttpMessageToExpando(RpcHttp inputMessage)
        {
            if (inputMessage == null)
            {
                return null;
            }

            var httpResponse = new HttpResponseData
            {
                StatusCode = inputMessage.StatusCode
            };
           

            if (inputMessage.Body != null)
            {
                httpResponse.Body = (byte[])inputMessage.Body.ToObject();
            }
            return httpResponse;
        }
    }

    internal class HttpResponseData
    {
        internal string StatusCode { set; get; }
        internal byte[] Body { set; get; }
    }
}
