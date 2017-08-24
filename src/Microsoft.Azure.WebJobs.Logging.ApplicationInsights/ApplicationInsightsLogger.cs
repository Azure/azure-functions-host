// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    internal class ApplicationInsightsLogger : ILogger
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly string _categoryName;

        private const string DefaultCategoryName = "Default";
        private const string DateTimeFormatString = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffK";
        private const string OperationContext = "MS_OperationContext";

        internal const string MetricCountKey = "count";
        internal const string MetricMinKey = "min";
        internal const string MetricMaxKey = "max";
        internal const string MetricStandardDeviationKey = "standarddeviation";

        private static readonly string[] SystemScopeKeys =
            {
                LogConstants.CategoryNameKey,
                LogConstants.LogLevelKey,
                LogConstants.OriginalFormatKey,
                ScopeKeys.Event,
                ScopeKeys.FunctionInvocationId,
                ScopeKeys.FunctionName,
                OperationContext
            };

        public ApplicationInsightsLogger(TelemetryClient client, string categoryName)
        {
            _telemetryClient = client;
            _categoryName = categoryName ?? DefaultCategoryName;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            string formattedMessage = formatter?.Invoke(state, exception);
            IEnumerable<KeyValuePair<string, object>> stateValues = state as IEnumerable<KeyValuePair<string, object>>;

            // If we don't have a message or any key/value pairs, there's nothing to log.
            if (stateValues == null && string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            // Initialize stateValues so the rest of the methods don't have to worry about null values.
            stateValues = stateValues ?? new Dictionary<string, object>();

            // Add some well-known properties to the scope dictionary so the TelemetryIniitalizer can add them
            // for all telemetry.
            using (BeginScope(new Dictionary<string, object>
            {
                [LogConstants.CategoryNameKey] = _categoryName,
                [LogConstants.LogLevelKey] = (LogLevel?)logLevel
            }))
            {
                // Log a metric
                if (eventId.Id == LogConstants.MetricEventId)
                {
                    LogMetric(stateValues);
                    return;
                }

                // Log a function result
                if (_categoryName == LogCategories.Results)
                {
                    LogFunctionResult(stateValues, logLevel, exception);
                    return;
                }

                // Log an aggregate record
                if (_categoryName == LogCategories.Aggregator)
                {
                    LogFunctionResultAggregate(stateValues);
                    return;
                }

                // Log an exception
                if (exception != null)
                {
                    LogException(logLevel, stateValues, exception, formattedMessage);
                    return;
                }

                // Otherwise, log a trace
                LogTrace(logLevel, stateValues, formattedMessage);
            }
        }

        private void LogMetric(IEnumerable<KeyValuePair<string, object>> values)
        {
            MetricTelemetry telemetry = new MetricTelemetry();

            foreach (var entry in values)
            {
                if (entry.Value == null)
                {
                    continue;
                }

                // Name and Value are not lower-case so check for them explicitly first and move to the
                // next entry if found.
                switch (entry.Key)
                {
                    case LogConstants.NameKey:
                        telemetry.Name = entry.Value.ToString();
                        continue;
                    case LogConstants.MetricValueKey:
                        telemetry.Sum = (double)entry.Value;
                        continue;
                    default:
                        break;
                }

                // Now check for case-insensitive matches
                switch (entry.Key.ToLowerInvariant())
                {
                    case MetricCountKey:
                        telemetry.Count = Convert.ToInt32(entry.Value);
                        break;
                    case MetricMinKey:
                        telemetry.Min = Convert.ToDouble(entry.Value);
                        break;
                    case MetricMaxKey:
                        telemetry.Max = Convert.ToDouble(entry.Value);
                        break;
                    case MetricStandardDeviationKey:
                        telemetry.StandardDeviation = Convert.ToDouble(entry.Value);
                        break;
                    default:
                        // Otherwise, it's a custom property.
                        ApplyProperty(telemetry, entry, LogConstants.CustomPropertyPrefix);
                        break;
                }
            }

            ApplyCustomScopeProperties(telemetry);

            _telemetryClient.TrackMetric(telemetry);
        }

        // Applies custom scope properties; does not apply 'system' used properties
        private static void ApplyCustomScopeProperties(ISupportProperties telemetry)
        {
            var scopeProperties = DictionaryLoggerScope.GetMergedStateDictionary()
                .Where(p => !SystemScopeKeys.Contains(p.Key, StringComparer.Ordinal));

            ApplyProperties(telemetry, scopeProperties, LogConstants.CustomPropertyPrefix);
        }

        private void LogException(LogLevel logLevel, IEnumerable<KeyValuePair<string, object>> values, Exception exception, string formattedMessage)
        {
            ExceptionTelemetry telemetry = new ExceptionTelemetry(exception)
            {
                Message = formattedMessage,
                SeverityLevel = GetSeverityLevel(logLevel),
                Timestamp = DateTimeOffset.UtcNow
            };
            ApplyProperties(telemetry, values, LogConstants.CustomPropertyPrefix);
            ApplyCustomScopeProperties(telemetry);
            _telemetryClient.TrackException(telemetry);
        }

        private void LogTrace(LogLevel logLevel, IEnumerable<KeyValuePair<string, object>> values, string formattedMessage)
        {
            TraceTelemetry telemetry = new TraceTelemetry(formattedMessage)
            {
                SeverityLevel = GetSeverityLevel(logLevel),
                Timestamp = DateTimeOffset.UtcNow
            };
            ApplyProperties(telemetry, values, LogConstants.CustomPropertyPrefix);
            ApplyCustomScopeProperties(telemetry);
            _telemetryClient.TrackTrace(telemetry);
        }

        private static SeverityLevel? GetSeverityLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    return SeverityLevel.Verbose;
                case LogLevel.Information:
                    return SeverityLevel.Information;
                case LogLevel.Warning:
                    return SeverityLevel.Warning;
                case LogLevel.Error:
                    return SeverityLevel.Error;
                case LogLevel.Critical:
                    return SeverityLevel.Critical;
                case LogLevel.None:
                default:
                    return null;
            }
        }

        private static void ApplyProperty(ISupportProperties telemetry, KeyValuePair<string, object> value, string propertyPrefix = null)
        {
            ApplyProperties(telemetry, new[] { value }, propertyPrefix);
        }

        // Inserts properties into the telemetry's properties. Properly formats dates, removes nulls, applies prefix, etc.
        private static void ApplyProperties(ISupportProperties telemetry, IEnumerable<KeyValuePair<string, object>> values, string propertyPrefix = null)
        {
            foreach (var property in values)
            {
                string stringValue = null;

                // drop null properties
                if (property.Value == null)
                {
                    continue;
                }

                // Format dates
                Type propertyType = property.Value.GetType();
                if (propertyType == typeof(DateTime))
                {
                    stringValue = ((DateTime)property.Value).ToUniversalTime().ToString(DateTimeFormatString);
                }
                else if (propertyType == typeof(DateTimeOffset))
                {
                    stringValue = ((DateTimeOffset)property.Value).UtcDateTime.ToString(DateTimeFormatString);
                }
                else
                {
                    stringValue = property.Value.ToString();
                }

                telemetry.Properties.Add($"{propertyPrefix}{property.Key}", stringValue);
            }
        }

        private void LogFunctionResultAggregate(IEnumerable<KeyValuePair<string, object>> values)
        {
            // Metric names will be created like "{FunctionName} {MetricName}"
            IDictionary<string, double> metrics = new Dictionary<string, double>();
            string functionName = LoggingConstants.Unknown;

            // build up the collection of metrics to send
            foreach (KeyValuePair<string, object> value in values)
            {
                switch (value.Key)
                {
                    case LogConstants.NameKey:
                        functionName = value.Value.ToString();
                        break;
                    case LogConstants.TimestampKey:
                    case LogConstants.OriginalFormatKey:
                        // Timestamp is created automatically
                        // We won't use the format string here
                        break;
                    default:
                        if (value.Value is TimeSpan)
                        {
                            // if it's a TimeSpan, log the milliseconds
                            metrics.Add(value.Key, ((TimeSpan)value.Value).TotalMilliseconds);
                        }
                        else if (value.Value is double || value.Value is int)
                        {
                            metrics.Add(value.Key, Convert.ToDouble(value.Value));
                        }

                        // do nothing otherwise
                        break;
                }
            }

            foreach (KeyValuePair<string, double> metric in metrics)
            {
                _telemetryClient.TrackMetric($"{functionName} {metric.Key}", metric.Value);
            }
        }

        private void LogFunctionResult(IEnumerable<KeyValuePair<string, object>> values, LogLevel logLevel, Exception exception)
        {
            IDictionary<string, object> scopeProps = DictionaryLoggerScope.GetMergedStateDictionary() ?? new Dictionary<string, object>();

            RequestTelemetry requestTelemetry = scopeProps.GetValueOrDefault<RequestTelemetry>(OperationContext);

            // We somehow never started the operation, so there's no way to complete it.
            if (requestTelemetry == null)
            {
                throw new InvalidOperationException("No started telemetry was found.");
            }

            requestTelemetry.Success = exception == null;
            requestTelemetry.ResponseCode = "0";

            // Set ip address to zeroes. If we find HttpRequest details below, we will update this
            requestTelemetry.Context.Location.Ip = LoggingConstants.ZeroIpAddress;

            ApplyFunctionResultProperties(requestTelemetry, values);

            // Functions attaches the HttpRequest, which allows us to log richer request details.
            if (scopeProps.TryGetValue(ApplicationInsightsScopeKeys.HttpRequest, out object request))
            {
                ApplyHttpRequestProperties(requestTelemetry, request as HttpRequestMessage);
            }

            // log associated exception details
            if (exception != null)
            {
                LogException(logLevel, values, exception, null);
            }

            // Note: we do not have to set Duration, StartTime, etc. These are handled by the call to Stop()
            requestTelemetry.Stop();
            _telemetryClient.TrackRequest(requestTelemetry);
        }

        private static void ApplyHttpRequestProperties(RequestTelemetry requestTelemetry, HttpRequestMessage request)
        {
            if (request == null)
            {
                return;
            }

            requestTelemetry.Url = new Uri(request.RequestUri.GetLeftPart(UriPartial.Path));
            requestTelemetry.Properties[LogConstants.HttpMethodKey] = request.Method.ToString();

            requestTelemetry.Context.Location.Ip = GetIpAddress(request);
            requestTelemetry.Context.User.UserAgent = request.Headers.UserAgent?.ToString();

            HttpResponseMessage response = GetResponse(request);

            // If a function throws an exception, we don't get a response attached to the request.
            // In that case, we'll consider it a 500.
            if (response?.StatusCode != null)
            {
                requestTelemetry.ResponseCode = ((int)response.StatusCode).ToString();
            }
            else
            {
                requestTelemetry.ResponseCode = "500";
            }
        }

        private static void ApplyFunctionResultProperties(RequestTelemetry requestTelemetry, IEnumerable<KeyValuePair<string, object>> stateValues)
        {
            // Build up the telemetry model. Some values special and go right on the telemetry object. All others
            // are added to the Properties bag.
            foreach (KeyValuePair<string, object> prop in stateValues)
            {
                switch (prop.Key)
                {
                    case LogConstants.NameKey:
                    case LogConstants.InvocationIdKey:
                    case LogConstants.StartTimeKey:
                    case LogConstants.DurationKey:
                        // These values are set by the calls to Start/Stop the telemetry. Other
                        // Loggers may want them, but we'll ignore.
                        break;
                    default:
                        // There should be no custom properties here, so just copy
                        // the passed-in values without any 'prop__' prefix.
                        ApplyProperty(requestTelemetry, prop);
                        break;
                }
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            // Filtering will occur in the Application Insights pipeline. This allows for the QuickPulse telemetry
            // to always be sent, even if logging actual records is completely disabled.
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            StartTelemetryIfFunctionInvocation(state as IDictionary<string, object>);

            return DictionaryLoggerScope.Push(state);
        }

        private static void StartTelemetryIfFunctionInvocation(IDictionary<string, object> stateValues)
        {
            if (stateValues == null)
            {
                return;
            }

            string functionName = stateValues.GetValueOrDefault<string>(ScopeKeys.FunctionName);
            string functionInvocationId = stateValues.GetValueOrDefault<string>(ScopeKeys.FunctionInvocationId);
            string eventName = stateValues.GetValueOrDefault<string>(ScopeKeys.Event);

            // If we have the invocation id, function name, and event, we know it's a new function. That means
            // that we want to start a new operation and let App Insights track it for us.
            if (!string.IsNullOrEmpty(functionName) &&
                !string.IsNullOrEmpty(functionInvocationId) &&
                eventName == LogConstants.FunctionStartEvent)
            {
                RequestTelemetry request = new RequestTelemetry()
                {
                    Id = functionInvocationId,
                    Name = functionName
                };

                // We'll need to store this operation context so we can stop it when the function completes
                request.Start();
                stateValues[OperationContext] = request;
            }
        }

        internal static string GetIpAddress(HttpRequestMessage httpRequest)
        {
            // first check for X-Forwarded-For; used by load balancers
            if (httpRequest.Headers.TryGetValues(ApplicationInsightsScopeKeys.ForwardedForHeaderName, out IEnumerable<string> headers))
            {
                string ip = headers.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return RemovePort(ip);
                }
            }

            HttpContextBase context = httpRequest.Properties.GetValueOrDefault<HttpContextBase>(ApplicationInsightsScopeKeys.HttpContext);
            return context?.Request?.UserHostAddress ?? LoggingConstants.ZeroIpAddress;
        }

        private static string RemovePort(string address)
        {
            // For Web sites in Azure header contains ip address with port e.g. 50.47.87.223:54464
            int portSeparatorIndex = address.IndexOf(":", StringComparison.OrdinalIgnoreCase);

            if (portSeparatorIndex > 0)
            {
                return address.Substring(0, portSeparatorIndex);
            }

            return address;
        }

        internal static HttpResponseMessage GetResponse(HttpRequestMessage httpRequest)
        {
            // Grab the response stored by functions
            return httpRequest.Properties.GetValueOrDefault<HttpResponseMessage>(ApplicationInsightsScopeKeys.FunctionsHttpResponse);
        }
    }
}
