// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Benchmarks
{
    public class SerializationBenchmarks
    {
        private static IList<RpcWorkerConfig> WorkerConfigs = new List<RpcWorkerConfig>(new RpcWorkerConfig[] { new RpcWorkerConfig{Description = new RpcWorkerDescription()
            {
                Extensions = new List<string>()
                 {
                     { "extension" }
                 },
                Language = "language",
                WorkerDirectory = "testDir",
                WorkerIndexing = "workerIndexing"
            } } });

        [Benchmark]
        public string JsonSerializerSerialize()
        {
            return JsonSerializer.Serialize(WorkerConfigs);
        }

        [Benchmark]
        public string Utf8JsonWriterSerialization()
        {
            using (MemoryStream memoryStream = new MemoryStream())
            using (Utf8JsonWriter writer = new Utf8JsonWriter(memoryStream))
            {
                writer.WriteStartObject(); // writes {
                writer.WriteStartArray(nameof(WorkerConfigs)); // writes "WorkerConfigs" : [
                foreach (var config in WorkerConfigs)
                {
                    writer.WriteStartObject(); // writes {
                    writer.WriteStartObject(nameof(RpcWorkerConfig.Description)); // writes "Description" : {
                    writer.WriteString(nameof(RpcWorkerConfig.Description.Language), config.Description.Language); //...
                                                                                                                   // and so on...
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                return Encoding.UTF8.GetString(memoryStream.ToArray());
            }
        }

        [Benchmark]
        public string SourceGenerationSerialization()
        {
            return JsonSerializer.Serialize(WorkerConfigs, typeof(List<RpcWorkerConfig>), SourceGenerationContext.Default);
        }

    }

    [JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = false)]
    [JsonSerializable(typeof(List<RpcWorkerConfig>))]
    internal partial class SourceGenerationContext: JsonSerializerContext
    {

    }
}
