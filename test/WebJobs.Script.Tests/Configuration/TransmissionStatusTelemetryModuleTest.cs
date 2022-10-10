// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class TransmissionStatusTelemetryModuleTest
    {
        [Fact]
        public void Initializer_Delegate()
        {
            TransmissionStatusTelemetryModule module = new TransmissionStatusTelemetryModule();
            TelemetryConfiguration config = new TelemetryConfiguration(string.Empty, new ServerTelemetryChannel());
            module.Initialize(config);
            Assert.Equal("Handler", ((ServerTelemetryChannel)config.TelemetryChannel).TransmissionStatusEvent.Method.Name);
        }

        [Fact]
        public void FormattedLog_Failure()
        {
            TransmissionStatusTelemetryModule module = new TransmissionStatusTelemetryModule();
            Transmission transmission = new Transmission(new Uri("https://test"), new List<ITelemetry>() { new RequestTelemetry(), new EventTelemetry() }, new TimeSpan(500));
            HttpWebResponseWrapper response = new HttpWebResponseWrapper()
            {
                StatusCode = 400,
                StatusDescription = "Invalid IKey",
                Content = null
            };

            TransmissionStatusEventArgs args = new TransmissionStatusEventArgs(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(module.FormattedLog(transmission, args));

            Assert.Equal(400, log.Value<int>("statusCode"));
            Assert.Equal("Invalid IKey", log.Value<string>("statusDescription"));
            Assert.Equal("00:00:00.0000500", log.Value<string>("timeout"));
            Assert.Equal(100, log.Value<int>("responseTimeInMs"));
            Assert.True(log.ContainsKey("id"));
        }

        [Fact]
        public void FormattedLog_PartialResponse()
        {
            TransmissionStatusTelemetryModule module = new TransmissionStatusTelemetryModule();
            Transmission transmission = new Transmission(new Uri("https://test"), new List<ITelemetry>() { new RequestTelemetry(), new EventTelemetry() }, new TimeSpan(500));
            BackendResponse backendResponse = new BackendResponse()
            {
                Errors = new BackendResponse.Error[2]
                {
                    new BackendResponse.Error() { Message = "Invalid IKey", StatusCode = 206 },
                    new BackendResponse.Error() { Message = "Invalid IKey", StatusCode = 206 }
                },
                ItemsAccepted = 100,
                ItemsReceived = 102
            };
            HttpWebResponseWrapper response = new HttpWebResponseWrapper()
            {
                StatusCode = 206,
                StatusDescription = "Invalid IKey",
                Content = JsonConvert.SerializeObject(backendResponse)
            };

            TransmissionStatusEventArgs args = new TransmissionStatusEventArgs(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(module.FormattedLog(transmission, args));

            Assert.Equal(206, log.Value<int>("statusCode"));
            Assert.Equal("Invalid IKey", log.Value<string>("statusDescription"));
            Assert.Equal("00:00:00.0000500", log.Value<string>("timeout"));
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
            TransmissionStatusTelemetryModule module = new TransmissionStatusTelemetryModule();
            Transmission transmission = new Transmission(new Uri("https://test"), new List<ITelemetry>() { new RequestTelemetry(), new RequestTelemetry(), new EventTelemetry() }, new TimeSpan(500));
            BackendResponse backendResponse = new BackendResponse()
            {
                ItemsAccepted = 100,
                ItemsReceived = 100
            };
            HttpWebResponseWrapper response = new HttpWebResponseWrapper()
            {
                StatusCode = 200,
                StatusDescription = "ok",
                Content = JsonConvert.SerializeObject(backendResponse)
            };

            TransmissionStatusEventArgs args = new TransmissionStatusEventArgs(response, 100);
            JObject log = JsonConvert.DeserializeObject<JObject>(module.FormattedLog(transmission, args));

            Assert.Equal(200, log.Value<int>("statusCode"));
            Assert.Equal("ok", log.Value<string>("statusDescription"));
            Assert.Equal("00:00:00.0000500", log.Value<string>("timeout"));
            Assert.Equal(100, log.Value<int>("responseTimeInMs"));
            Assert.True(log.ContainsKey("id"));
            Assert.Equal(3, log["items"].Count());
            Assert.Null(log.Value<string>("errorMessage"));
        }
    }
}