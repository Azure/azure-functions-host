// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExtensionsOptionContractResolverTests
    {
        [Fact]
        public void ParseNestedClassWithFilterOutNonBasicTypes()
        {
            var eventHubOptions = new ScopedEventHubOptions();
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new ExtensionsOptionContractResolver(),
                DefaultValueHandling = DefaultValueHandling.Include,
                Formatting = Formatting.Indented
            };
            var json = JObject.Parse(JsonConvert.SerializeObject(eventHubOptions, settings));
            // Should be fetch the nested custom class configrations with default value
            Assert.Equal(100, json["batchCheckpointFrequency"].Value<int>());
            Assert.Equal(10, json["eventProcessorOptions"]["maxBatchSize"].Value<int>());
            // Should not fetch non-basic types
            Assert.Null(json["eventProcessorOptions"]["webProxy"]);
            Assert.Null(json["eventProcessorOptions"]["initialOffsetProvider"]);
        }

        public class ScopedEventHubOptions
        {
            public int BatchCheckpointFrequency { get; set; } = 100;

            public EventProcessorOptions EventProcessorOptions { get; set; } = new EventProcessorOptions();

            public InitialOffsetOptions InitialOffsetOptions { get; set; } = new InitialOffsetOptions();
        }

        public class EventProcessorOptions
        {
            public int MaxBatchSize { get; set; } = 10;

            public int PrefetchCount { get; set; } = 20;

            public Func<string, object> InitialOffsetProvider
            {
                get;
                set;
            }
            = x => x;

            public IWebProxy WebProxy
            {
                get;
                set;
            }
            = new Mock<IWebProxy>().Object;
        }

        public class InitialOffsetOptions
        {
            public string Type { get; set; } = string.Empty;

            public string EnqueuedTimeUTC { get; set; } = string.Empty;
        }
    }
}
