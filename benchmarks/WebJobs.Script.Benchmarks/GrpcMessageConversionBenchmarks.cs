// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Google.Protobuf.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class GrpcMessageConversionBenchmarks
    {
        private static byte[] _byteArray = new byte[2000];
        private static string _str = new string('-', 2000);
        private static double _dbl = 2000;
        private static byte[][] _byteJaggedArray = new byte[1000][];
        private static string[] _strArray = new string[]{ new string('-', 1000), new string('-', 1000) };
        private static double[] _dblArray = new double[1000];
        private static long[] _longArray = new long[1000];
        private static JObject _jObj = JObject.Parse(@"{'name': 'lilian'}");
        internal GrpcCapabilities grpcCapabilities = new GrpcCapabilities(NullLogger.Instance);

        // Not easy to benchmark
        // public static HttpRequest _httpRequest;

        [Benchmark]
        public Task ToRpc_Null() => InvokeToRpc(((object)null));

        [Benchmark]
        public Task ToRpc_ByteArray() => InvokeToRpc(_byteArray);

        [Benchmark]
        public Task ToRpc_String() => InvokeToRpc(_str);

        [Benchmark]
        public Task ToRpc_Double() => InvokeToRpc(_dbl);

        [Benchmark]
        public Task ToRpc_ByteJaggedArray() => InvokeToRpc(_byteJaggedArray);

        [Benchmark]
        public Task ToRpc_StringArray() => InvokeToRpc(_strArray);

        [Benchmark]
        public Task ToRpc_DoubleArray() => InvokeToRpc(_dblArray);

        [Benchmark]
        public Task ToRpc_LongArray() => InvokeToRpc(_longArray);

        [Benchmark]
        public Task ToRpc_JObject() => InvokeToRpc(_jObj);

        public async Task InvokeToRpc(object obj) => await obj.ToRpc(NullLogger.Instance, grpcCapabilities);

        [GlobalSetup]
        public void Setup()
        {
            MapField<string, string> addedCapabilities = new MapField<string, string>
            {
                { RpcWorkerConstants.TypedDataCollection, "1" }
            };
            grpcCapabilities.UpdateCapabilities(addedCapabilities, GrpcCapabilitiesUpdateStrategy.Merge);
        }
    }
}
