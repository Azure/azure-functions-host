// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class MeterLogger : ILogger
    {
        private readonly string _category;
        private readonly IEnvironment _environment;
        private readonly IExternalScopeProvider _scopeProvider;
        private readonly Meter _meter;
        private readonly bool _isUserFunction = false;
        private readonly bool _isAggregator = false;
        private readonly LogLevel _logLevel = LogLevel.Information;
        private static readonly ConcurrentDictionary<string, ObservableGauge<int>> ObservableGauges = new ConcurrentDictionary<string, ObservableGauge<int>>();
        private static readonly ConcurrentDictionary<string, int> ObservableGaugesValues = new ConcurrentDictionary<string, int>();

        public MeterLogger(string category, IEnvironment environment, IExternalScopeProvider scopeProvider, Meter meter)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _meter = meter ?? throw new ArgumentNullException(nameof(meter));
            _isUserFunction = LogCategories.IsFunctionUserCategory(category);
            _isAggregator = category.Equals(LogCategories.Aggregator, StringComparison.OrdinalIgnoreCase);

            // Add a gauge for Aggregate category
            if (_isUserFunction)
            {
                string[] parts = _category.Split('.');

                if (!ObservableGauges.ContainsKey(parts[1] + ".Count"))
                {
                    ObservableGaugesValues.TryAdd(parts[1] + ".Count", 0);
                    ObservableGauges.TryAdd(parts[1] + ".Count", _meter.CreateObservableGauge<int>(parts[1] + ".Count", () => GetMetricValue(parts[1] + ".Count")));
                }
            }
        }

        private int GetMetricValue(string metricName)
        {
            if (ObservableGaugesValues.ContainsKey(metricName))
            {
                int value = ObservableGaugesValues[metricName];
                ObservableGaugesValues[metricName] = 0;
                Console.WriteLine("------------------------------------------------ " + metricName + " consumed, returned- " + value);
                return value;
            }
            return 0;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            // We want to instantiate this Logger in placeholder mode to warm it up, but do not want to log anything.
            return _logLevel == logLevel && (_isUserFunction || _isAggregator);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (_isUserFunction && eventId.Id == LogConstants.MetricEventId)
            {
                var scopeProps = _scopeProvider.GetScopeDictionaryOrNull();
                // Log a metric from user logs only
                //LogMetric(stateValues, logLevel, eventId);
            }
            else if (_isAggregator)
            {
                string functionName = "DefaultFuncName";
                string key = "DefaultKey";
                int countValue = 0;
                foreach (KeyValuePair<string, object> value in (IEnumerable<KeyValuePair<string, object>>)state)
                {
                    switch (value.Key)
                    {
                        case LogConstants.NameKey when value.Value is string name:
                            functionName = name;
                            break;
                        case LogConstants.TimestampKey:
                        case LogConstants.OriginalFormatKey:
                            // Timestamp is created automatically
                            // We won't use the format string here
                            break;
                        default:
                            if (value.Key == "Count" && value.Value is int intValue)
                            {
                                key = value.Key;
                                countValue = intValue;
                            }
                            // do nothing otherwise
                            break;
                    }
                }
                if (ObservableGaugesValues.ContainsKey(functionName + "." + key))
                {
                    ObservableGaugesValues[functionName + "." + key] += countValue;
                }
                // Log metrics
            }
        }
    }
}