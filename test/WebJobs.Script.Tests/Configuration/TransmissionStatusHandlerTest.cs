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
    }
}