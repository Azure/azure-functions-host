//Copyright(c).NET Foundation.All rights reserved.
//Licensed under the MIT License. See License.txt in the project root for license information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    /// <summary>
    /// Handler is used to track AI ingestion service response by subscribing to the transmission event.
    /// The notification includes information about the response, and also the telemetry items which were part of the transmission.
    /// </summary>
    internal class TransmissionStatusHandler : IDisposable
    {
        private static readonly DiagnosticListener _source = new(ScriptConstants.HostDiagnosticSourcePrefix + "ApplicationInsights");

        internal static void Handler(object sender, TransmissionStatusEventArgs args)
        {
            if (args == null)
            {
                _source.Write("TransmissionStatus", "Unable to parse transmission status response, args is null");
                return;
            }

            // Always log if the response is non-success or if the feature flag is enabled
            if (args.Response?.StatusCode != 200 || _source.IsEnabled(ScriptConstants.HostDiagnosticSourceDebugEventNamePrefix))
            {
                _source.Write("TransmissionStatus", FormattedLog(sender, args));
            }
        }

        internal static string FormattedLog(object sender, TransmissionStatusEventArgs args)
        {
            var transmission = sender as Transmission;
            var items = transmission?.TelemetryItems.GroupBy(n => n.GetEnvelopeName())
                        .Select(n => new TelemetryItem
                        {
                            Type = n.Key,
                            Count = n.Count()
                        });

            IngestionServiceResponse response = null;
            if (!string.IsNullOrWhiteSpace(args?.Response?.Content))
            {
                response = JsonSerializer.Deserialize(
                    args.Response.Content, IngestionServiceResponseContext.Default.IngestionServiceResponse);
            }

            string topErrorMessage = null;
            int? topStatusCode = null;
            if (response?.Errors != null && response.Errors.Length > 0)
            {
                topErrorMessage = response.Errors[0].Message;
                topStatusCode = response?.Errors[0].StatusCode;
            }

            var log = new LogMessage
            {
                StatusCode = args?.Response?.StatusCode,
                StatusDescription = args?.Response?.StatusDescription,
                Id = transmission?.Id,
                Items = items,
                Received = response?.ItemsReceived,
                Accepted = response?.ItemsAccepted,
                ErrorMessage = topErrorMessage,
                ErrorCode = topStatusCode,
                ResponseTimeInMs = args?.ResponseDurationInMs,
                RetryAfterHeader = args?.Response?.RetryAfterHeader
            };
            return JsonSerializer.Serialize(log, LogMessageContext.Default.LogMessage);
        }

        /// <summary>
        /// Disposes the object.
        /// </summary>
        public void Dispose()
        {
            _source.Dispose();
        }
    }

    [JsonSerializable(typeof(IngestionServiceResponse), GenerationMode = JsonSourceGenerationMode.Metadata)]
    internal partial class IngestionServiceResponseContext : JsonSerializerContext
    {
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, GenerationMode = JsonSourceGenerationMode.Serialization, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(LogMessage))]
    internal partial class LogMessageContext : JsonSerializerContext
    {
    }

    internal class IngestionServiceResponse
    {
        [JsonPropertyName("itemsReceived")]
        public int? ItemsReceived { get; set; }

        [JsonPropertyName("itemsAccepted")]
        public int? ItemsAccepted { get; set; }

        [JsonPropertyName("errors")]
        public Error[] Errors { get; set; }

        internal class Error
        {
            [JsonPropertyName("statusCode")]
            public int StatusCode { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; }
        }
    }

    internal class LogMessage
    {
        public int? StatusCode { get; set; }

        public string StatusDescription { get; set; }

        public string Id { get; set; }

        public int? Received { get; set; }

        public int? Accepted { get; set; }

        public string ErrorMessage { get; set; }

        public int? ErrorCode { get; set; }

        public long? ResponseTimeInMs { get; set; }

        public string RetryAfterHeader { get; set; }

        public IEnumerable<TelemetryItem> Items { get; set; }
    }

    internal class TelemetryItem
    {
        public int Count { get; set; }

        public string Type { get; set; }
    }
}