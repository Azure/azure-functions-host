// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class TransmissionStatusHandlerTest
    {
        [Fact]
        public void FormattedLog_Failure()
        {
            Transmission transmission = new(new Uri("https://test"), new List<ITelemetry>() { new RequestTelemetry(), new EventTelemetry() }, new TimeSpan(500));
            HttpWebResponseWrapper response = new()
            {
                StatusCode = 400,
                StatusDescription = "Invalid IKey",
                Content = null
            };

            TransmissionStatusEventArgs args = new(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(TransmissionStatusHandler.FormattedLog(transmission, args));

            Assert.Equal(400, log.Value<int>("statusCode"));
            Assert.Equal("Invalid IKey", log.Value<string>("statusDescription"));
            Assert.Equal(100, log.Value<int>("responseTimeInMs"));
            Assert.True(log.ContainsKey("id"));
        }

        [Fact]
        public void FormattedLog_PartialResponse()
        {
            TransmissionStatusHandler handler = new();
            Transmission transmission = new(new Uri("https://test"), new List<ITelemetry>() { new RequestTelemetry(), new EventTelemetry() }, new TimeSpan(500));
            IngestionServiceResponse backendResponse = new()
            {
                Errors = new IngestionServiceResponse.Error[2]
                {
                    new IngestionServiceResponse.Error() { Message = "Invalid IKey", StatusCode = 206 },
                    new IngestionServiceResponse.Error() { Message = "Invalid IKey", StatusCode = 206 }
                },
                ItemsAccepted = 100,
                ItemsReceived = 102
            };
            HttpWebResponseWrapper response = new()
            {
                StatusCode = 206,
                StatusDescription = "Invalid IKey",
                Content = JsonConvert.SerializeObject(backendResponse, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })
            };

            TransmissionStatusEventArgs args = new(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(TransmissionStatusHandler.FormattedLog(transmission, args));

            Assert.Equal(206, log.Value<int>("statusCode"));
            Assert.Equal("Invalid IKey", log.Value<string>("statusDescription"));
            Assert.Equal(100, log.Value<int>("responseTimeInMs"));
            Assert.True(log.ContainsKey("id"));

            Assert.Equal("Invalid IKey", log.Value<string>("errorMessage"));
            Assert.Equal(206, log.Value<int>("errorCode"));
            Assert.Equal(100, log.Value<int>("accepted"));
            Assert.Equal(102, log.Value<int>("received"));
        }

        [Fact]
        public void FormattedLog_Success()
        {
            TransmissionStatusHandler handler = new();
            Transmission transmission = new(new("https://test"), new List<ITelemetry>() { new RequestTelemetry(), new RequestTelemetry(), new EventTelemetry(), new DependencyTelemetry() }, new TimeSpan(500));
            IngestionServiceResponse backendResponse = new()
            {
                ItemsAccepted = 100,
                ItemsReceived = 100
            };
            HttpWebResponseWrapper response = new()
            {
                StatusCode = 200,
                StatusDescription = "ok",
                Content = JsonConvert.SerializeObject(backendResponse, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })
            };

            TransmissionStatusEventArgs args = new(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(TransmissionStatusHandler.FormattedLog(transmission, args));

            Assert.Equal(200, log.Value<int>("statusCode"));
            Assert.Equal("ok", log.Value<string>("statusDescription"));
            Assert.Equal(100, log.Value<int>("responseTimeInMs"));
            Assert.True(log.ContainsKey("id"));
            Assert.Equal(3, log["items"].Count());
            Assert.Null(log.Value<string>("errorMessage"));
        }

        [Fact]
        public void FormattedLog_InstrumentationKeys()
        {
            TransmissionStatusHandler handler = new();
            var request1 = new RequestTelemetry();
            request1.Context.InstrumentationKey = "AAAAA-AAAAAAAAAA-AAAAAAAA-AAAAAAAA";

            var request2 = new RequestTelemetry();
            request2.Context.InstrumentationKey = "CCCCC-CCCCCCCCCCCCCCCCC-";

            var trace1 = new TraceTelemetry();
            trace1.Context.InstrumentationKey = "BBBBB-BBBBBBBBB-BBBBBBBBBBB-BBBBBBB";

            var trace2 = new TraceTelemetry();
            trace2.Context.InstrumentationKey = "BBBBB-BBBBBBBBB-BBBBBBBBBBB-BBBBBBB";

            Transmission transmission = new(new("https://test"), new List<ITelemetry>() { request1, request2, trace1, trace2 }, new TimeSpan(500));
            IngestionServiceResponse backendResponse = new()
            {
                ItemsAccepted = 100,
                ItemsReceived = 100
            };
            HttpWebResponseWrapper response = new()
            {
                StatusCode = 200,
                StatusDescription = "ok",
                Content = JsonConvert.SerializeObject(backendResponse, new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() })
            };

            TransmissionStatusEventArgs args = new(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(TransmissionStatusHandler.FormattedLog(transmission, args));

            Assert.Equal(200, log.Value<int>("statusCode"));
            Assert.Equal("https://test", log.Value<string>("endpointAddress"));
            Assert.Equal("ok", log.Value<string>("statusDescription"));
            Assert.Equal(100, log.Value<int>("responseTimeInMs"));
            Assert.True(log.ContainsKey("id"));
            Assert.Equal(2, log["items"].Count());
            Assert.Equal(3, log["iKeys"].Count());
            Assert.Null(log.Value<string>("errorMessage"));
            string keys = log["iKeys"].ToString();
            Assert.True(keys.Contains("AAAAA-AAAAAAAAAA-AAAAAAA************") && keys.Contains("BBBBB-BBBBBBBBB-BBBBBBBB************") && keys.Contains("CCCCC-CCCCCCCCCCCCCCCCC-"));
        }
    }
}