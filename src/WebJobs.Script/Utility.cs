// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class Utility
    {
        // Prefix that uniquely identifies our assemblies
        // i.e.: "f-<functionname>"
        public const string AssemblyPrefix = "f-";
        public const string AssemblySeparator = "__";
        private static readonly Regex FunctionNameValidationRegex = new Regex(@"^[a-z][a-z0-9_\-]{0,127}$(?<!^host$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string UTF8ByteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
        private static readonly FilteredExpandoObjectConverter _filteredExpandoObjectConverter = new FilteredExpandoObjectConverter();

        private static List<string> dotNetLanguages = new List<string>() { DotNetScriptTypes.CSharp, DotNetScriptTypes.DotNetAssembly };

        public static int ColdStartDelayMS { get; set; } = 5000;

        /// <summary>
        /// Walk from the method up to the containing type, looking for an instance
        /// of the specified attribute type, returning it if found.
        /// </summary>
        /// <param name="method">The method to check.</param>
        internal static T GetHierarchicalAttributeOrNull<T>(MethodInfo method) where T : Attribute
        {
            return (T)GetHierarchicalAttributeOrNull(method, typeof(T));
        }

        /// <summary>
        /// Walk from the method up to the containing type, looking for an instance
        /// of the specified attribute type, returning it if found.
        /// </summary>
        /// <param name="method">The method to check.</param>
        /// <param name="type">The attribute type to look for.</param>
        internal static Attribute GetHierarchicalAttributeOrNull(MethodInfo method, Type type)
        {
            var attribute = method.GetCustomAttribute(type);
            if (attribute != null)
            {
                return attribute;
            }

            attribute = method.DeclaringType.GetCustomAttribute(type);
            if (attribute != null)
            {
                return attribute;
            }

            return null;
        }

        internal static async Task InvokeWithRetriesAsync(Action action, int maxRetries, TimeSpan retryInterval)
        {
            await InvokeWithRetriesAsync(() =>
            {
                action();
                return Task.CompletedTask;
            }, maxRetries, retryInterval);
        }

        internal static async Task InvokeWithRetriesAsync(Func<Task> action, int maxRetries, TimeSpan retryInterval)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    await action();
                    return;
                }
                catch (Exception ex) when (!ex.IsFatal())
                {
                    if (++attempt > maxRetries)
                    {
                        throw;
                    }
                    await Task.Delay(retryInterval);
                }
            }
        }

        /// <summary>
        /// Delays while the specified condition remains true.
        /// </summary>
        /// <param name="timeoutSeconds">The maximum number of seconds to delay.</param>
        /// <param name="pollingIntervalMilliseconds">The polling interval.</param>
        /// <param name="condition">The condition to check</param>
        /// <returns>A Task representing the delay.</returns>
        internal static Task<bool> DelayAsync(int timeoutSeconds, int pollingIntervalMilliseconds, Func<bool> condition)
        {
            Task<bool> Condition() => Task.FromResult(condition());
            return DelayAsync(timeoutSeconds, pollingIntervalMilliseconds, Condition, CancellationToken.None);
        }

        /// <summary>
        /// Delays while the specified condition remains true.
        /// </summary>
        /// <param name="timeoutSeconds">The maximum number of seconds to delay.</param>
        /// <param name="pollingIntervalMilliseconds">The polling interval.</param>
        /// <param name="condition">The async condition to check</param>
        /// <returns>A Task representing the delay.</returns>
        internal static async Task<bool> DelayAsync(int timeoutSeconds, int pollingIntervalMilliseconds, Func<Task<bool>> condition, CancellationToken cancellationToken)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            TimeSpan delay = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds);
            TimeSpan timeWaited = TimeSpan.Zero;
            bool conditionResult = await condition();
            while (conditionResult && (timeWaited < timeout) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(delay);
                timeWaited += delay;
                conditionResult = await condition();
            }
            return conditionResult;
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
        /// <param name="logger">An optional logger that will emit the delay.</param>
        /// <returns>A <see cref="Task"/> representing the computed backoff interval.</returns>
        public static async Task DelayWithBackoffAsync(int exponent, CancellationToken cancellationToken, TimeSpan? unit = null,
            TimeSpan? min = null, TimeSpan? max = null, ILogger logger = null)
        {
            TimeSpan delay = TimeSpan.FromSeconds(5);

            try
            {
                delay = ComputeBackoff(exponent, unit, min, max);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, $"Exception while calculating backoff. Using a default '{delay}' delay.");
            }

            logger?.LogDebug($"Delay is '{delay}'.");

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
            TimeSpan maxValue = max ?? TimeSpan.MaxValue;

            // prevent an OverflowException
            if (exponent >= 64)
            {
                return maxValue;
            }

            // determine the exponential backoff factor
            long backoffFactor = Convert.ToInt64((Math.Pow(2, exponent) - 1) / 2);

            // compute the backoff delay
            unit = unit ?? TimeSpan.FromSeconds(1);
            long totalDelayTicks = backoffFactor * unit.Value.Ticks;

            // If we've overflowed long, return max.
            if (backoffFactor > 0 && totalDelayTicks <= 0)
            {
                return maxValue;
            }

            TimeSpan delay = TimeSpan.FromTicks(totalDelayTicks);

            // apply minimum restriction
            if (min.HasValue && delay < min)
            {
                delay = min.Value;
            }

            // apply maximum restriction
            if (delay > maxValue)
            {
                delay = maxValue;
            }

            return delay;
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

        public static bool IsValidFunctionName(string functionName)
        {
            return FunctionNameValidationRegex.IsMatch(functionName);
        }

        public static bool FunctionNamesMatch(string functionName, string comparand)
        {
            return string.Equals(functionName, comparand, StringComparison.OrdinalIgnoreCase);
        }

        // "Namespace.Class.Method" --> "Namespace.Class"
        public static string GetFullClassName(string fullFunctionName)
        {
            int i = fullFunctionName.LastIndexOf('.');
            var typeName = fullFunctionName.Substring(0, i);
            return typeName;
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

        public static bool GetStateBoolValue(IEnumerable<KeyValuePair<string, object>> state, string key)
        {
            return GetStateValueOrDefault<bool>(state, key);
        }

        public static TValue GetStateValueOrDefault<TValue>(IEnumerable<KeyValuePair<string, object>> state, string key)
        {
            if (state == null)
            {
                return default(TValue);
            }

            // Choose the last one rather than throwing for multiple hits. Since we use our own keys to track
            // this, we shouldn't have conflicts.
            var value = state.LastOrDefault(k => string.Equals(k.Key, key, StringComparison.OrdinalIgnoreCase));
            if (value.Equals(default(KeyValuePair<string, object>)))
            {
                return default(TValue);
            }
            else
            {
                return (TValue)value.Value;
            }
        }

        public static bool TryGetFunctionName(IDictionary<string, object> scopeProps, out string functionName)
        {
            functionName = null;

            object scopeValue = null;
            if ((scopeProps.TryGetValue("functionName", out scopeValue) ||
                 scopeProps.TryGetValue(LogConstants.NameKey, out scopeValue) ||
                 scopeProps.TryGetValue(ScopeKeys.FunctionName, out scopeValue)) && scopeValue != null)
            {
                functionName = scopeValue.ToString();
            }

            return functionName != null;
        }

        public static bool IsFunctionName(KeyValuePair<string, object> p)
        {
            return string.Equals(p.Key, "functionName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Key, LogConstants.NameKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Key, ScopeKeys.FunctionName, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetValueFromScope(IDictionary<string, object> scopeProperties, string key)
        {
            if (scopeProperties != null && scopeProperties.TryGetValue(key, out object value) && value != null)
            {
                return value.ToString();
            }
            return null;
        }

        public static string GetAssemblyNameFromMetadata(FunctionMetadata metadata, string suffix)
        {
            return AssemblyPrefix + metadata.Name + AssemblySeparator + suffix.GetHashCode().ToString();
        }

        internal static void AddFunctionError(IDictionary<string, ICollection<string>> functionErrors, string functionName, string error, bool isFunctionShortName = false)
        {
            functionName = isFunctionShortName ? functionName : Utility.GetFunctionShortName(functionName);

            ICollection<string> functionErrorCollection = new Collection<string>();
            if (!string.IsNullOrEmpty(functionName) && !functionErrors.TryGetValue(functionName, out functionErrorCollection))
            {
                functionErrors[functionName] = functionErrorCollection = new Collection<string>();
            }
            functionErrorCollection.Add(error);
        }

        internal static bool TryReadFunctionConfig(string scriptDir, out string json, IFileSystem fileSystem = null)
        {
            json = null;
            fileSystem = fileSystem ?? FileUtility.Instance;

            // read the function config
            string functionConfigPath = Path.Combine(scriptDir, ScriptConstants.FunctionMetadataFileName);
            try
            {
                json = fileSystem.File.ReadAllText(functionConfigPath);
            }
            catch (IOException ex) when
                (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // not a function directory
                return false;
            }

            return true;
        }

        internal static void VerifyFunctionsMatchSpecifiedLanguage(IEnumerable<FunctionMetadata> functions, string workerRuntime, bool isPlaceholderMode, bool isHttpWorker, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (isPlaceholderMode || isHttpWorker)
            {
                return;
            }

            if (!IsSingleLanguage(functions, workerRuntime))
            {
                if (string.IsNullOrEmpty(workerRuntime))
                {
                    throw new HostInitializationException($"Found functions with more than one language. Select a language for your function app by specifying {RpcWorkerConstants.FunctionWorkerRuntimeSettingName} AppSetting");
                }
                else
                {
                    throw new HostInitializationException($"Did not find functions with language [{workerRuntime}].");
                }
            }
        }

        internal static bool IsSingleLanguage(IEnumerable<FunctionMetadata> functions, string workerRuntime)
        {
            if (functions == null)
            {
                throw new ArgumentNullException(nameof(functions));
            }
            var filteredFunctions = functions.Where(f => !f.IsCodeless()).ToArray();
            if (filteredFunctions.Length == 0)
            {
                return true;
            }
            if (string.IsNullOrEmpty(workerRuntime))
            {
                return filteredFunctions.Select(f => f.Language).Distinct().Count() <= 1;
            }
            return ContainsFunctionWithWorkerRuntime(filteredFunctions, workerRuntime);
        }

        internal static string GetWorkerRuntime(IEnumerable<FunctionMetadata> functions)
        {
            if (IsSingleLanguage(functions, null))
            {
                var filteredFunctions = functions?.Where(f => !f.IsCodeless());
                string functionLanguage = filteredFunctions.FirstOrDefault()?.Language;
                if (string.IsNullOrEmpty(functionLanguage))
                {
                    return null;
                }

                if (IsDotNetLanguageFunction(functionLanguage))
                {
                    return RpcWorkerConstants.DotNetLanguageWorkerName;
                }
                return functionLanguage;
            }
            return null;
        }

        internal static bool IsFunctionMetadataLanguageSupportedByWorkerRuntime(FunctionMetadata functionMetadata, string workerRuntime)
        {
            if (string.IsNullOrEmpty(functionMetadata.Language))
            {
                return false;
            }
            if (string.IsNullOrEmpty(workerRuntime))
            {
                return true;
            }
            return !string.IsNullOrEmpty(functionMetadata.Language) && functionMetadata.Language.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsDotNetLanguageFunction(string functionLanguage)
        {
            return dotNetLanguages.Any(lang => string.Equals(lang, functionLanguage, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsSupportedRuntime(string workerRuntime, IEnumerable<RpcWorkerConfig> workerConfigs)
        {
            return workerConfigs.Any(config => string.Equals(config.Description.Language, workerRuntime, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsFunctionWithWorkerRuntime(IEnumerable<FunctionMetadata> functions, string workerRuntime)
        {
            if (string.Equals(workerRuntime, RpcWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
            {
                return functions.Any(f => dotNetLanguages.Any(l => l.Equals(f.Language, StringComparison.OrdinalIgnoreCase)));
            }
            if (functions != null && functions.Any())
            {
                return functions.Any(f => !string.IsNullOrEmpty(f.Language) && f.Language.Equals(workerRuntime, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        internal static IEnumerable<FunctionMetadata> GetValidFunctions(IEnumerable<FunctionMetadata> indexedFunctions, ICollection<FunctionDescriptor> functionDescriptors)
        {
            if (indexedFunctions == null || !indexedFunctions.Any())
            {
                return indexedFunctions;
            }
            if (functionDescriptors == null)
            {
                // No valid functions
                return null;
            }
            return indexedFunctions.Where(m => functionDescriptors.Select(fd => fd.Metadata.Name).Contains(m.Name) == true);
        }

        public static async Task MarkContainerDisabled(ILogger logger)
        {
            logger.LogDebug("Setting container instance offline");
            var disableContainerFilePath = Path.Combine(Path.GetTempPath(), ScriptConstants.DisableContainerFileName);
            if (!FileUtility.FileExists(disableContainerFilePath))
            {
                await FileUtility.WriteAsync(disableContainerFilePath, "This container instance is offline");
            }
        }

        public static bool IsContainerDisabled()
        {
            return FileUtility.FileExists(Path.Combine(Path.GetTempPath(), ScriptConstants.DisableContainerFileName));
        }

        public static bool CheckAppOffline(IEnvironment environment, string scriptPath)
        {
            // Linux container environments have an additional way of putting a specific worker instance offline.
            if (environment.IsLinuxConsumptionContainerDisabled())
            {
                return true;
            }

            // check if we should be in an offline state
            string offlineFilePath = Path.Combine(scriptPath, ScriptConstants.AppOfflineFileName);
            if (FileUtility.FileExists(offlineFilePath))
            {
                return true;
            }
            return false;
        }

        public static void ExecuteAfterDelay(Action targetAction, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            Task.Delay(delay, cancellationToken).ContinueWith(_ =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    targetAction();
                }
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
        }

        public static void ExecuteAfterColdStartDelay(IEnvironment environment, Action targetAction, CancellationToken cancellationToken = default)
        {
            // for Dynamic SKUs where coldstart is important, we want to delay the action
            if (environment.IsDynamicSku())
            {
                ExecuteAfterDelay(targetAction, TimeSpan.FromMilliseconds(ColdStartDelayMS), cancellationToken);
            }
            else
            {
                targetAction();
            }
        }

        public static bool TryCleanUrl(string url, out string cleaned)
        {
            cleaned = null;

            Uri uri = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                cleaned = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
                if (uri.Query.Length > 0)
                {
                    cleaned += "...";
                }
                return true;
            }

            return false;
        }

        public static bool IsHttporManualTrigger(string triggerType)
        {
            if (string.Compare("httptrigger", triggerType, StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare("manualtrigger", triggerType, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
            return false;
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

        public static string BuildStorageConnectionString(string accountName, string accessKey, string suffix)
        {
            return $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accessKey};EndpointSuffix={suffix}";
        }

        public static bool IsMediaTypeOctetOrMultipart(MediaTypeHeaderValue mediaType)
        {
                return mediaType != null && (string.Equals(mediaType.MediaType, ScriptConstants.MediatypeOctetStream, StringComparison.OrdinalIgnoreCase) ||
                                mediaType.MediaType.IndexOf(ScriptConstants.MediatypeMutipartPrefix, StringComparison.OrdinalIgnoreCase) >= 0);
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