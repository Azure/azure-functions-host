// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Utility
    {
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
                string value = GetSettingFromConfigOrEnvironment(AzureWebsiteSku);
                return string.Compare(value, DynamicSku, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }

        public static string GetSettingFromConfigOrEnvironment(string settingName)
        {
            string configValue = ConfigurationManager.AppSettings[settingName];
            if (!string.IsNullOrEmpty(configValue))
            {
                // config values take precedence over environment values
                return configValue;
            }

            return Environment.GetEnvironmentVariable(settingName) ?? configValue;
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
        public async static Task DelayWithBackoffAsync(int exponent, CancellationToken cancellationToken, TimeSpan? unit = null, TimeSpan? min = null, TimeSpan? max = null)
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

        public static string GetSubscriptionId()
        {
            string ownerName = ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsiteOwnerName) ?? string.Empty;
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

        public static bool IsValidUserType(Type type)
        {
            return !type.IsInterface && !type.IsPrimitive && !(type.Namespace == "System");
        }

        public static IReadOnlyDictionary<string, string> ToStringValues(this IReadOnlyDictionary<string, object> data)
        {
            return data.ToDictionary(p => p.Key, p => p.Value != null ? p.Value.ToString() : null, StringComparer.OrdinalIgnoreCase);
        }

        public static string GetFunctionShortName(string functionName)
        {
            int idx = functionName.LastIndexOf('.');
            if (idx > 0)
            {
                functionName = functionName.Substring(idx + 1);
            }

            return functionName;
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
                    // parse the object skipping any nested objects (binding data
                    // only includes top level properties)
                    JObject parsed = JObject.Parse(json);
                    var additionalBindingData = parsed.Children<JProperty>()
                        .Where(p => p.Value != null &&
                        (p.Value.Type != JTokenType.Object & p.Value.Type != JTokenType.Array))
                        .ToDictionary(p => p.Name, p => (string)p);

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

        public static JObject ToJObject(ExpandoObject value)
        {
            string json = ToJson(value);
            return JObject.Parse(json);
        }
    }
}
