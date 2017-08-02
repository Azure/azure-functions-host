// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Http;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    internal class ApplicationInsightsLogger : ILogger
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly string _categoryName;
        private const string DefaultCategoryName = "Default";
        private const string DateTimeFormatString = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffK";
        private const string OperationContext = "MS_OperationContext";

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

        private void LogException(LogLevel logLevel, IEnumerable<KeyValuePair<string, object>> values, Exception exception, string formattedMessage)
        {
            ExceptionTelemetry telemetry = new ExceptionTelemetry(exception);
            telemetry.Message = formattedMessage;
            telemetry.SeverityLevel = GetSeverityLevel(logLevel);
            telemetry.Timestamp = DateTimeOffset.UtcNow;
            ApplyCustomProperties(telemetry, values);
            _telemetryClient.TrackException(telemetry);
        }

        private void LogTrace(LogLevel logLevel, IEnumerable<KeyValuePair<string, object>> values, string formattedMessage)
        {
            TraceTelemetry telemetry = new TraceTelemetry(formattedMessage);
            telemetry.SeverityLevel = GetSeverityLevel(logLevel);
            telemetry.Timestamp = DateTimeOffset.UtcNow;
            ApplyCustomProperties(telemetry, values);
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

        private static void ApplyCustomProperties(ISupportProperties telemetry, IEnumerable<KeyValuePair<string, object>> values)
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

                // Since there is no nesting of properties, apply a prefix before the property name to lessen
                // the chance of collisions.
                telemetry.Properties.Add(LogConstants.CustomPropertyPrefix + property.Key, stringValue);
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

            var operation = scopeProps.GetValueOrDefault<IOperationHolder<RequestTelemetry>>(OperationContext);

            // We somehow never started the operation, so there's no way to complete it.
            if (operation == null || operation.Telemetry == null)
            {
                throw new InvalidOperationException("No started telemetry was found.");
            }

            RequestTelemetry requestTelemetry = operation.Telemetry;
            requestTelemetry.Success = exception == null;
            requestTelemetry.ResponseCode = "0";

            // Set ip address to zeroes. If we find HttpRequest details below, we will update this
            requestTelemetry.Context.Location.Ip = LoggingConstants.ZeroIpAddress;

            ApplyFunctionResultProperties(requestTelemetry, values);

            // Functions attaches the HttpRequest, which allows us to log richer request details.
            if (scopeProps.TryGetValue(ApplicationInsightsScopeKeys.HttpRequest, out object request))
            {
                ApplyHttpRequestProperties(requestTelemetry, request as HttpRequest);
            }

            // log associated exception details
            if (exception != null)
            {
                LogException(logLevel, values, exception, null);
            }

            // Note: we do not have to set Duration, StartTime, etc. These are handled by the call
            // to StopOperation, which also tracks the telemetry.
            _telemetryClient.StopOperation(operation);
        }

        private static void ApplyHttpRequestProperties(RequestTelemetry requestTelemetry, HttpRequest request)
        {
            if (request == null)
            {
                return;
            }

            var uriBuilder = new UriBuilder
            {
                Scheme = request.Scheme,
                Host = request.Host.Host,
                Path = request.Path
            };

            if (request.Host.Port.HasValue)
            {
                uriBuilder.Port = request.Host.Port.Value;
            }

            requestTelemetry.Url = uriBuilder.Uri;
            requestTelemetry.Properties[LogConstants.HttpMethodKey] = request.Method;

            requestTelemetry.Context.Location.Ip = GetIpAddress(request);
            if (request.Headers.TryGetValue(HeaderNames.UserAgent, out StringValues userAgentHeader))
            {
                requestTelemetry.Context.User.UserAgent = userAgentHeader.FirstOrDefault();
            }

            HttpResponse response = GetResponse(request);

            // If a function throws an exception, we don't get a response attached to the request.
            // In that case, we'll consider it a 500.
            if (response?.StatusCode != null)
            {
                requestTelemetry.ResponseCode = response.StatusCode.ToString();
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
                        // Name is already set at telemetry start
                        break;
                    case LogConstants.InvocationIdKey:
                        // InvocationId is already set at telemetry start
                        break;
                    case LogConstants.StartTimeKey:
                        DateTimeOffset startTime = new DateTimeOffset((DateTime)prop.Value, TimeSpan.Zero);
                        requestTelemetry.Timestamp = startTime;
                        requestTelemetry.Properties.Add(prop.Key, startTime.ToString(DateTimeFormatString));
                        break;
                    case LogConstants.DurationKey:
                        if (prop.Value is TimeSpan)
                        {
                            requestTelemetry.Duration = (TimeSpan)prop.Value;
                        }
                        break;
                    case LogConstants.OriginalFormatKey:
                        // this is the format string; we won't use it here
                        break;
                    default:
                        if (prop.Value is DateTime)
                        {
                            DateTimeOffset date = new DateTimeOffset((DateTime)prop.Value, TimeSpan.Zero);
                            requestTelemetry.Properties.Add(prop.Key, date.ToString(DateTimeFormatString));
                        }
                        else
                        {
                            requestTelemetry.Properties.Add(prop.Key, prop.Value?.ToString());
                        }
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

            var stateValues = state as IDictionary<string, object>;
            if (stateValues == null)
            {
                // There's nothing we can do without a dictionary.
                return null;
            }

            StartTelemetryIfFunctionInvocation(stateValues);

            return DictionaryLoggerScope.Push(new ReadOnlyDictionary<string, object>(stateValues));
        }

        private void StartTelemetryIfFunctionInvocation(IDictionary<string, object> stateValues)
        {
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
                stateValues[OperationContext] = _telemetryClient.StartOperation(request);
            }
        }

        internal static string GetIpAddress(HttpRequest httpRequest)
        {
            // first check for X-Forwarded-For; used by load balancers
            if (httpRequest.Headers?.TryGetValue(ApplicationInsightsScopeKeys.ForwardedForHeaderName, out StringValues headerValues) ?? false)
            {
                string ip = headerValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    return RemovePort(ip);
                }
            }

            return httpRequest.HttpContext?.Connection.RemoteIpAddress.ToString() ?? LoggingConstants.ZeroIpAddress;
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

        internal static HttpResponse GetResponse(HttpRequest httpRequest)
        {
            // Grab the response stored by functions
            object value = null;
            httpRequest.HttpContext?.Items?.TryGetValue(ApplicationInsightsScopeKeys.FunctionsHttpResponse, out value);

            return value as HttpResponse;
        }
    }
}
