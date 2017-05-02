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

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal class ApplicationInsightsLogger : ILogger
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly string _categoryName;
        private const string DefaultCategoryName = "Default";
        private const string DateTimeFormatString = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffK";
        private Func<string, LogLevel, bool> _filter;

        public ApplicationInsightsLogger(TelemetryClient client, string categoryName, Func<string, LogLevel, bool> filter)
        {
            _telemetryClient = client;
            _categoryName = categoryName ?? DefaultCategoryName;
            _filter = filter;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string formattedMessage = formatter?.Invoke(state, exception);

            IEnumerable<KeyValuePair<string, object>> stateValues = state as IEnumerable<KeyValuePair<string, object>>;

            // We only support lists of key-value pairs. Anything else we'll skip.
            if (stateValues == null)
            {
                return;
            }

            // Log a function result
            if (_categoryName == LogCategories.Results)
            {
                LogFunctionResult(stateValues, exception);
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

        private void ApplyCustomProperties(ISupportProperties telemetry, IEnumerable<KeyValuePair<string, object>> values)
        {
            telemetry.Properties.Add(LoggingKeys.CategoryName, _categoryName);

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
                telemetry.Properties.Add(LoggingKeys.CustomPropertyPrefix + property.Key, stringValue);
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
                    case LoggingKeys.Name:
                        functionName = value.Value.ToString();
                        break;
                    case LoggingKeys.Timestamp:
                    case LoggingKeys.OriginalFormat:
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

        private void LogFunctionResult(IEnumerable<KeyValuePair<string, object>> values, Exception exception)
        {
            IDictionary<string, object> scopeProps = DictionaryLoggerScope.GetMergedStateDictionary() ?? new Dictionary<string, object>();

            RequestTelemetry requestTelemetry = new RequestTelemetry();
            requestTelemetry.Success = exception == null;
            requestTelemetry.ResponseCode = "0";

            // Set ip address to zeroes. If we find HttpRequest details below, we will update this
            requestTelemetry.Context.Location.Ip = LoggingConstants.ZeroIpAddress;

            ApplyFunctionResultProperties(requestTelemetry, values);

            // Functions attaches the HttpRequest, which allows us to log richer request details.
            object request;
            if (scopeProps.TryGetValue(ApplicationInsightsScopeKeys.HttpRequest, out request))
            {
                ApplyHttpRequestProperties(requestTelemetry, request as HttpRequestMessage);
            }

            // log associated exception details
            if (exception != null)
            {
                ExceptionTelemetry exceptionTelemetry = new ExceptionTelemetry(exception);
                _telemetryClient.TrackException(exceptionTelemetry);
            }

            _telemetryClient.TrackRequest(requestTelemetry);
        }

        private static void ApplyHttpRequestProperties(RequestTelemetry requestTelemetry, HttpRequestMessage request)
        {
            if (request == null)
            {
                return;
            }

            requestTelemetry.Url = new Uri(request.RequestUri.GetLeftPart(UriPartial.Path));
            requestTelemetry.Properties[LoggingKeys.HttpMethod] = request.Method.ToString();

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
                    case LoggingKeys.Name:
                        requestTelemetry.Name = prop.Value.ToString();
                        break;
                    case LoggingKeys.InvocationId:
                        requestTelemetry.Id = prop.Value.ToString();
                        break;
                    case LoggingKeys.StartTime:
                        DateTimeOffset startTime = new DateTimeOffset((DateTime)prop.Value, TimeSpan.Zero);
                        requestTelemetry.Timestamp = startTime;
                        requestTelemetry.Properties.Add(prop.Key, startTime.ToString(DateTimeFormatString));
                        break;
                    case LoggingKeys.Duration:
                        if (prop.Value is TimeSpan)
                        {
                            requestTelemetry.Duration = (TimeSpan)prop.Value;
                        }
                        break;
                    case LoggingKeys.OriginalFormat:
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
            if (_filter == null)
            {
                return true;
            }

            return _filter(_categoryName, logLevel);
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return DictionaryLoggerScope.Push(state);
        }

        internal static string GetIpAddress(HttpRequestMessage httpRequest)
        {
            // first check for X-Forwarded-For; used by load balancers
            IEnumerable<string> headers;
            if (httpRequest.Headers.TryGetValues(ApplicationInsightsScopeKeys.ForwardedForHeaderName, out headers))
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
