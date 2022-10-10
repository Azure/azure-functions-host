// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Transmission Status Telemetry Module
    /// </summary>
    internal class TransmissionStatusTelemetryModule : ITelemetryModule, IDisposable
    {
        private bool isInitialized = false;
        private DiagnosticListener source = new DiagnosticListener(ScriptConstants.HostDiagnosticSourcePrefix + "ApplicationInsights");
        private JsonSerializerSettings options = new JsonSerializerSettings()
        {
            Error = (sender, error) => error.ErrorContext.Handled = true,
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// Initializes the telemetry module.
        /// </summary>
        /// <param name="configuration">Telemetry configuration to use for initialization.</param>
        public void Initialize(TelemetryConfiguration configuration)
        {
            // Prevent the telemetry module from being initialized multiple times.
            if (isInitialized)
            {
                return;
            }
            if (configuration.TelemetryChannel is ServerTelemetryChannel)
            {
                (configuration.TelemetryChannel as ServerTelemetryChannel).TransmissionStatusEvent += Handler;
                isInitialized = true;
            }
        }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        public void Dispose()
        {
            source.Dispose();
        }

        internal void Handler(object sender, TransmissionStatusEventArgs args)
        {
            if (args == null)
            {
                source.Write("TransmissionStatus", "Unable to parse transmission status response, args is null");
                return;
            }

            // Always log if the response is non-success or if the feature flag is enabled
            if (args.Response?.StatusCode != 200 || source.IsEnabled(ScriptConstants.HostDiagnosticSourceDebugEventNamePrefix))
            {
                source.Write("TransmissionStatus", FormattedLog(sender, args));
            }
        }

        internal string FormattedLog(object sender, TransmissionStatusEventArgs args)
        {
            var transmission = sender as Transmission;
            var items = transmission?.TelemetryItems.GroupBy(n => n.GetEnvelopeName())
                        .Select(n => new
                        {
                            type = n.Key,
                            count = n.Count()
                        });

            BackendResponse backendResponse = null;
            if (!string.IsNullOrWhiteSpace(args?.Response?.Content))
            {
                backendResponse = JsonConvert.DeserializeObject<BackendResponse>(args.Response.Content, options);
            }

            string topErrorMessage = null;
            int? topStatusCode = null;
            if (backendResponse?.Errors != null && backendResponse.Errors.Length > 0)
            {
                topErrorMessage = backendResponse.Errors[0].Message;
                topStatusCode = backendResponse?.Errors[0].StatusCode;
            }

            var log = new
            {
                statusCode = args?.Response?.StatusCode,
                statusDescription = args?.Response?.StatusDescription,
                id = transmission?.Id,
                items = items,
                received = backendResponse?.ItemsReceived,
                accepted = backendResponse?.ItemsAccepted,
                errorMessage = topErrorMessage,
                errorCode = topStatusCode,
                responseTimeInMs = args?.ResponseDurationInMs,
                retryAfterHeader = args?.Response?.RetryAfterHeader,
                timeout = transmission?.Timeout,
            };
            return JsonConvert.SerializeObject(log, options);
        }
    }

    internal class BackendResponse
    {
        [JsonProperty("itemsReceived")]
        public int ItemsReceived { get; set; }

        [JsonProperty("itemsAccepted")]
        public int ItemsAccepted { get; set; }

        [JsonProperty("errors")]
        public Error[] Errors { get; set; }

        internal class Error
        {
            [JsonProperty("statusCode")]
            public int StatusCode { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; }
        }
    }
}