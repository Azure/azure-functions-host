// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Utility
    {
        // Prefix that uniquely identifies our assemblies
        // i.e.: "f-<functionname>"
        public const string AssemblyPrefix = "f-";
        public const string AssemblySeparator = "__";

        private static readonly string UTF8ByteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
        public const string AzureWebsiteSku = "WEBSITE_SKU";
        public const string DynamicSku = "Dynamic";
        private static readonly FilteredExpandoObjectConverter _filteredExpandoObjectConverter = new FilteredExpandoObjectConverter();

        /// <summary>
        /// Gets a value indicating whether the JobHost is running in a Dynamic
        /// App Service WebApp.
        /// </summary>
        public static bool IsDynamic
        {
            get
            {
                string value = ScriptSettingsManager.Instance.GetSetting(AzureWebsiteSku);
                return string.Compare(value, DynamicSku, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }

        /// <summary>
        /// Delays while the specified condition remains true.
        /// </summary>
        /// <param name="timeoutSeconds">The maximum number of seconds to delay.</param>
        /// <param name="pollingIntervalMilliseconds">The polling interval.</param>
        /// <param name="condition">The condition to check</param>
        /// <returns>A Task representing the delay.</returns>
        internal static async Task DelayAsync(int timeoutSeconds, int pollingIntervalMilliseconds, Func<bool> condition)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            TimeSpan delay = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds);
            TimeSpan timeWaited = TimeSpan.Zero;

            while (condition() && (timeWaited < timeout))
            {
                await Task.Delay(delay);
                timeWaited += delay;
            }
        }

        /// <summary>
        /// Implements a configurable exponential backoff strategy.
        /// </summary>
        /// <param name="exponent">The backoff exponent. E.g. for backing off in a retry loop,
        /// this would be the retry attempt count.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to use.</param>
        /// <param name="unit">The time unit for the backoff delay. Default is 1 second, producing a backoff sequence
        /// of 2, 4, 8, 16, etc. seconds.</param>
        /// <param name="min">The minimum delay.</param>
        /// <param name="max">The maximum delay.</param>
        /// <returns>A <see cref="Task"/> representing the computed backoff interval.</returns>
        public static async Task DelayWithBackoffAsync(int exponent, CancellationToken cancellationToken, TimeSpan? unit = null, TimeSpan? min = null, TimeSpan? max = null)
        {
            TimeSpan delay = ComputeBackoff(exponent, unit, min, max);

            if (delay.TotalMilliseconds > 0)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // when the delay is cancelled it is not an error
                }
            }
        }

        internal static TimeSpan ComputeBackoff(int exponent, TimeSpan? unit = null, TimeSpan? min = null, TimeSpan? max = null)
        {
            // determine the exponential backoff factor
            long backoffFactor = Convert.ToInt64((Math.Pow(2, exponent) - 1) / 2);

            // compute the backoff delay
            unit = unit ?? TimeSpan.FromSeconds(1);
            long totalDelayTicks = backoffFactor * unit.Value.Ticks;
            TimeSpan delay = TimeSpan.FromTicks(totalDelayTicks);

            // apply minimum restriction
            if (min.HasValue && delay < min)
            {
                delay = min.Value;
            }

            // apply maximum restriction
            if (max.HasValue && delay > max)
            {
                delay = max.Value;
            }

            return delay;
        }

        /// <summary>
        /// Computes a stable non-cryptographic hash
        /// </summary>
        /// <param name="value">The string to use for computation</param>
        /// <returns>A stable, non-cryptographic, hash</returns>
        internal static int GetStableHash(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            unchecked
            {
                int hash = 23;
                foreach (char c in value)
                {
                    hash = (hash * 31) + c;
                }
                return hash;
            }
        }

        public static string GetSubscriptionId(ScriptSettingsManager settingsManager)
        {
            string ownerName = settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName) ?? string.Empty;
            if (!string.IsNullOrEmpty(ownerName))
            {
                int idx = ownerName.IndexOf('+');
                if (idx > 0)
                {
                    return ownerName.Substring(0, idx);
                }
            }

            return null;
        }

        public static string GetInformationalVersion(Type type)
            => type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;

        public static bool IsValidUserType(Type type)
        {
            return !type.IsInterface && !type.IsPrimitive && !(type.Namespace == "System");
        }

        public static IReadOnlyDictionary<string, string> ToStringValues(this IReadOnlyDictionary<string, object> data)
        {
            return data.ToDictionary(p => p.Key, p => p.Value != null ? p.Value.ToString() : null, StringComparer.OrdinalIgnoreCase);
        }

        // "Namespace.Class.Method" --> "Method"
        public static string GetFunctionShortName(string functionName)
        {
            int idx = functionName.LastIndexOf('.');
            if (idx > 0)
            {
                functionName = functionName.Substring(idx + 1);
            }

            return functionName;
        }

        // "Namespace.Class.Method" --> "Namespace.Class"
        public static string GetFullClassName(string fullFunctionName)
        {
            int i = fullFunctionName.LastIndexOf('.');
            var typeName = fullFunctionName.Substring(0, i);
            return typeName;
        }

        internal static string GetDefaultHostId(ScriptSettingsManager settingsManager, ScriptHostConfiguration scriptConfig)
        {
            // We're setting the default here on the newly created configuration
            // If the user has explicitly set the HostID via host.json, it will overwrite
            // what we set here
            string hostId = null;
            if (scriptConfig.IsSelfHost)
            {
                // When running locally, derive a stable host ID from machine name
                // and root path. We use a hash rather than the path itself to ensure
                // IDs differ (due to truncation) between folders that may share the same
                // root path prefix.
                // Note that such an ID won't work in distributed scenarios, so should
                // only be used for local/CLI scenarios.
                string sanitizedMachineName = Environment.MachineName
                    .Where(char.IsLetterOrDigit)
                    .Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString();
                hostId = $"{sanitizedMachineName}-{Math.Abs(GetStableHash(scriptConfig.RootScriptPath))}";
            }
            else if (!string.IsNullOrEmpty(settingsManager?.AzureWebsiteUniqueSlotName))
            {
                // If running on Azure Web App, derive the host ID from unique site slot name
                hostId = settingsManager.AzureWebsiteUniqueSlotName;
            }

            if (!string.IsNullOrEmpty(hostId))
            {
                if (hostId.Length > ScriptConstants.MaximumHostIdLength)
                {
                    // Truncate to the max host name length if needed
                    hostId = hostId.Substring(0, ScriptConstants.MaximumHostIdLength);
                }
            }

            // Lowercase and trim any trailing '-' as they can cause problems with queue names
            return hostId?.ToLowerInvariant().TrimEnd('-');
        }

        public static string FlattenException(Exception ex, Func<string, string> sourceFormatter = null, bool includeSource = true)
        {
            StringBuilder flattenedErrorsBuilder = new StringBuilder();
            string lastError = null;
            sourceFormatter = sourceFormatter ?? ((s) => s);

            if (ex is AggregateException)
            {
                ex = ex.InnerException;
            }

            do
            {
                StringBuilder currentErrorBuilder = new StringBuilder();
                if (includeSource && !string.IsNullOrEmpty(ex.Source))
                {
                    currentErrorBuilder.AppendFormat("{0}: ", sourceFormatter(ex.Source));
                }

                currentErrorBuilder.Append(ex.Message);

                if (!ex.Message.EndsWith("."))
                {
                    currentErrorBuilder.Append(".");
                }

                // sometimes inner exceptions are exactly the same
                // so first check before duplicating
                string currentError = currentErrorBuilder.ToString();
                if (lastError == null ||
                    string.Compare(lastError.Trim(), currentError.Trim()) != 0)
                {
                    if (flattenedErrorsBuilder.Length > 0)
                    {
                        flattenedErrorsBuilder.Append(" ");
                    }
                    flattenedErrorsBuilder.Append(currentError);
                }

                lastError = currentError;
            }
            while ((ex = ex.InnerException) != null);

            return flattenedErrorsBuilder.ToString();
        }

        /// <summary>
        /// Applies any additional binding data from the input value to the specified binding data.
        /// This binding data then becomes available to the binding process (in the case of late bound bindings)
        /// </summary>
        internal static void ApplyBindingData(object value, Dictionary<string, object> bindingData)
        {
            try
            {
                // if the input value is a JSON string, extract additional
                // binding data from it
                string json = value as string;
                if (!string.IsNullOrEmpty(json) && Utility.IsJson(json))
                {
                    // parse the object adding top level properties
                    JObject parsed = JObject.Parse(json);
                    var additionalBindingData = parsed.Children<JProperty>()
                        .Where(p => p.Value != null && (p.Value.Type != JTokenType.Array))
                        .ToDictionary(p => p.Name, p => ConvertPropertyValue(p));

                    if (additionalBindingData != null)
                    {
                        foreach (var item in additionalBindingData)
                        {
                            if (item.Value != null)
                            {
                                bindingData[item.Key] = item.Value;
                            }
                        }
                    }
                }
            }
            catch
            {
                // it's not an error if the incoming message isn't JSON
                // there are cases where there will be output binding parameters
                // that don't bind to JSON properties
            }
        }

        private static object ConvertPropertyValue(JProperty property)
        {
            if (property.Value != null && property.Value.Type == JTokenType.Object)
            {
                return (JObject)property.Value;
            }
            else
            {
                return (string)property.Value;
            }
        }

        public static bool IsJson(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            input = input.Trim();
            return (input.StartsWith("{", StringComparison.OrdinalIgnoreCase) && input.EndsWith("}", StringComparison.OrdinalIgnoreCase))
                || (input.StartsWith("[", StringComparison.OrdinalIgnoreCase) && input.EndsWith("]", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Converts the first letter of the specified string to lower case if it
        /// isn't already.
        /// </summary>
        public static string ToLowerFirstCharacter(string input)
        {
            if (!string.IsNullOrEmpty(input) && char.IsUpper(input[0]))
            {
                input = char.ToLowerInvariant(input[0]) + input.Substring(1);
            }

            return input;
        }

        /// <summary>
        /// Checks if a given string has a UTF8 BOM
        /// </summary>
        /// <param name="input">The string to be evalutated</param>
        /// <returns>True if the string begins with a UTF8 BOM; Otherwise, false.</returns>
        public static bool HasUtf8ByteOrderMark(string input)
            => input != null && CultureInfo.InvariantCulture.CompareInfo.IsPrefix(input, UTF8ByteOrderMark, CompareOptions.Ordinal);

        public static string RemoveUtf8ByteOrderMark(string input)
        {
            if (HasUtf8ByteOrderMark(input))
            {
                input = input.Substring(UTF8ByteOrderMark.Length);
            }

            return input;
        }

        public static string ToJson(ExpandoObject value, Formatting formatting = Formatting.Indented)
        {
            return JsonConvert.SerializeObject(value, formatting, _filteredExpandoObjectConverter);
        }

        public static JObject ToJObject(ExpandoObject value)
        {
            string json = ToJson(value);
            return JObject.Parse(json);
        }

        public static bool TryMatchAssembly(string assemblyName, Type type, out Assembly matchedAssembly)
        {
            matchedAssembly = null;

            var candidateAssembly = type.Assembly;
            if (string.Compare(assemblyName, AssemblyNameCache.GetName(candidateAssembly).Name, StringComparison.OrdinalIgnoreCase) == 0)
            {
                matchedAssembly = candidateAssembly;
                return true;
            }

            return false;
        }

        public static IJobHostMetadataProvider CreateMetadataProvider(this JobHost host)
        {
            return (IJobHostMetadataProvider)host.Services.GetService(typeof(IJobHostMetadataProvider));
        }

        internal static bool IsNullable(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        internal static LogLevel ToLogLevel(System.Diagnostics.TraceLevel traceLevel)
        {
            switch (traceLevel)
            {
                case System.Diagnostics.TraceLevel.Verbose:
                    return LogLevel.Trace;
                case System.Diagnostics.TraceLevel.Info:
                    return LogLevel.Information;
                case System.Diagnostics.TraceLevel.Warning:
                    return LogLevel.Warning;
                case System.Diagnostics.TraceLevel.Error:
                    return LogLevel.Error;
                default:
                    return LogLevel.None;
            }
        }

        public static LoggerFilterOptions CreateLoggerFilterOptions()
        {
            // TODO: Whitelist should be configurable
            // Whitelist our log categories to remove large amounts of ASP.NET logs.
            var filterOptions = new LoggerFilterOptions();
            filterOptions.AddFilter((category, level) => category.StartsWith($"{ScriptConstants.LogCategoryHost}.") || category.StartsWith($"{ScriptConstants.LogCategoryFunction}.") || category.StartsWith($"{ScriptConstants.LogCategoryWorker}."));

            return filterOptions;
        }

        public static bool GetStateBoolValue(IEnumerable<KeyValuePair<string, object>> state, string key)
        {
            if (state == null)
            {
                return false;
            }

            var kvps = state.Where(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));

            if (!kvps.Any())
            {
                return false;
            }

            // Choose the last one rather than throwing for multiple hits. Since we use our own keys to track
            // this, we shouldn't have conflicts.
            return Convert.ToBoolean(kvps.Last().Value);
        }

        public static TValue GetStateValueOrDefault<TValue>(IEnumerable<KeyValuePair<string, object>> state, string key)
        {
            if (state == null)
            {
                return default(TValue);
            }

            var kvps = state.Where(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));

            if (!kvps.Any())
            {
                return default(TValue);
            }

            // Choose the last one rather than throwing for multiple hits. Since we use our own keys to track
            // this, we shouldn't have conflicts.
            return (TValue)kvps.Last().Value;
        }

        public static string GetValueFromState<TState>(TState state, string key)
        {
            string value = string.Empty;
            if (state is IEnumerable<KeyValuePair<string, object>> stateDict)
            {
                value = GetStateValueOrDefault<string>(stateDict, key) ?? string.Empty;
            }
            return value;
        }

        public static string GetValueFromScope(IDictionary<string, object> scopeProperties, string key)
        {
            object value;
            if (scopeProperties != null && scopeProperties.TryGetValue(key, out value) && value != null)
            {
                return value.ToString();
            }
            return null;
        }

        public static string GetAssemblyNameFromMetadata(Description.FunctionMetadata metadata, string suffix)
        {
            return AssemblyPrefix + metadata.Name + AssemblySeparator + suffix.GetHashCode().ToString();
        }

        private class FilteredExpandoObjectConverter : ExpandoObjectConverter
        {
            public override bool CanWrite => true;

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var expando = (IDictionary<string, object>)value;
                var filtered = expando
                    .Where(p => !(p.Value is Delegate))
                    .ToDictionary(p => p.Key, p => p.Value);
                serializer.Serialize(writer, filtered);
            }
        }
    }
}
