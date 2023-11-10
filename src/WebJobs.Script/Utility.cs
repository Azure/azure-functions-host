// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private const string AssemblyPrefix = "f-";
        private const string AssemblySeparator = "__";
        private const string BlobServiceDomain = "blob";
        private const string SasVersionQueryParam = "sv";
        private const string SasTokenExpirationDate = "se";

        /// <summary>
        /// Gets a value indicating whether the host is running in placeholder simulation mode.
        /// This mode is used for testing placeholder scenarios locally.
        /// Running using either "DebugPlaceholder" or "ReleasePlaceholder" configuration mode will
        /// cause the host to run in placeholder simulation mode.
        /// </summary>
#if PLACEHOLDERSIMULATION
        public const bool IsInPlaceholderSimulationMode = true;
#else
        public const bool IsInPlaceholderSimulationMode = false;
#endif

        private static readonly Regex FunctionNameValidationRegex = new Regex(@"^[a-z][a-z0-9_\-]{0,127}$(?<!^host$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BindingNameValidationRegex = new Regex(string.Format("^([a-zA-Z][a-zA-Z0-9]{{0,127}}|{0})$", Regex.Escape(ScriptConstants.SystemReturnParameterBindingName)));

        private static readonly string UTF8ByteOrderMark = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
        private static readonly FilteredExpandoObjectConverter _filteredExpandoObjectConverter = new FilteredExpandoObjectConverter();

        private static List<string> dotNetLanguages = new List<string>() { DotNetScriptTypes.CSharp, DotNetScriptTypes.DotNetAssembly };

        public static int ColdStartDelayMS { get; set; } = 5000;

        internal static bool TryGetHostService<TService>(IScriptHostManager scriptHostManager, out TService service) where TService : class
        {
            service = null;

            try
            {
                service = (scriptHostManager as IServiceProvider)?.GetService<TService>();
            }
            catch
            {
                // can get exceptions if the host is being disposed
            }

            return service != null;
        }

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

        internal static string GetDebugEngineInfo(RpcWorkerConfig workerConfig, string runtime)
        {
            if (runtime.Equals(RpcWorkerConstants.DotNetIsolatedLanguageWorkerName, StringComparison.CurrentCultureIgnoreCase))
            {
                if (workerConfig.Description.DefaultRuntimeName.Contains(RpcWorkerConstants.DotNetFramework))
                {
                    return RpcWorkerConstants.DotNetFrameworkDebugEngine;
                }
                else
                {
                    return RpcWorkerConstants.DotNetCoreDebugEngine;
                }
            }

            return runtime;
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
        /// <param name="condition">The condition to check.</param>
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
        /// <param name="condition">The async condition to check.</param>
        /// <returns>A Task representing the delay.</returns>
        internal static Task<bool> DelayAsync(int timeoutSeconds, int pollingIntervalMilliseconds, Func<Task<bool>> condition, CancellationToken cancellationToken)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            TimeSpan pollingInterval = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds);
            return DelayAsync(timeout, pollingInterval, condition, cancellationToken);
        }

        internal static async Task<bool> DelayAsync(TimeSpan timeout, TimeSpan pollingInterval, Func<Task<bool>> condition, CancellationToken cancellationToken)
        {
            TimeSpan timeWaited = TimeSpan.Zero;
            bool conditionResult = await condition();
            while (conditionResult && (timeWaited < timeout) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(pollingInterval);
                timeWaited += pollingInterval;
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
            return data.ToDictionary(p => p.Key, p => p.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        public static string GetValueOrNull(this StringDictionary dictionary, string key)
        {
            return dictionary.ContainsKey(key) ? dictionary[key] : null;
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
        /// This binding data then becomes available to the binding process (in the case of late bound bindings).
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
        /// Checks if a given string has a UTF8 BOM.
        /// </summary>
        /// <param name="input">The string to be evaluated.</param>
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
            if (scopeProps != null && scopeProps.Count > 0 &&
                (scopeProps.TryGetValue(ScopeKeys.FunctionName, out scopeValue) ||
                 scopeProps.TryGetValue("functionName", out scopeValue) ||
                 scopeProps.TryGetValue(LogConstants.NameKey, out scopeValue)) && scopeValue != null)
            {
                functionName = scopeValue.ToString();
            }

            return functionName != null;
        }

        public static bool IsFunctionName(KeyValuePair<string, object> p)
        {
            return string.Equals(p.Key, ScopeKeys.FunctionName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Key, "functionName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Key, LogConstants.NameKey, StringComparison.OrdinalIgnoreCase);
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

            ICollection<string> functionErrorCollection = new HashSet<string>();
            if (!string.IsNullOrEmpty(functionName) && !functionErrors.TryGetValue(functionName, out functionErrorCollection))
            {
                functionErrors[functionName] = functionErrorCollection = new HashSet<string>();
            }
            functionErrorCollection.Add(error);
        }

        public static void ValidateBinding(BindingMetadata bindingMetadata)
        {
            if (bindingMetadata.Name == null || !BindingNameValidationRegex.IsMatch(bindingMetadata.Name))
            {
                throw new ArgumentException($"The binding name {bindingMetadata.Name} is invalid. Please assign a valid name to the binding.");
            }

            if (bindingMetadata.IsReturn && bindingMetadata.Direction != BindingDirection.Out)
            {
                throw new ArgumentException($"{ScriptConstants.SystemReturnParameterBindingName} bindings must specify a direction of 'out'.");
            }
        }

        public static void ValidateName(string name)
        {
            if (!IsValidFunctionName(name))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid {1} name.", name, "function"));
            }
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

        internal static string GetWorkerRuntime(IEnumerable<FunctionMetadata> functions, IEnvironment environment = null)
        {
            if (environment != null)
            {
                var workerRuntime = environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);

                if (!string.IsNullOrEmpty(workerRuntime))
                {
                    return workerRuntime;
                }
            }

            if (functions != null && IsSingleLanguage(functions, null))
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

        internal static bool IsFunctionMetadataLanguageSupportedByWorkerRuntime(FunctionMetadata functionMetadata, IList<RpcWorkerConfig> workerConfigs)
        {
            return !string.IsNullOrEmpty(functionMetadata.Language) && workerConfigs.Select(wc => wc.Description.Language).Contains(functionMetadata.Language);
        }

        public static bool IsDotNetLanguageFunction(string functionLanguage)
        {
            return dotNetLanguages.Any(lang => string.Equals(lang, functionLanguage, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsSupportedRuntime(string workerRuntime, IEnumerable<RpcWorkerConfig> workerConfigs)
        {
            return workerConfigs.Any(config => string.Equals(config.Description.Language, workerRuntime, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCodelessDotNetLanguageFunction(FunctionMetadata functionMetadata)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }

            if (functionMetadata.IsCodeless() && !string.IsNullOrEmpty(functionMetadata.Language))
            {
                return IsDotNetLanguageFunction(functionMetadata.Language);
            }
            return false;
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
            return indexedFunctions.Where(m => functionDescriptors.Select(fd => fd.Metadata.Name).Contains(m.Name));
        }

        public static bool CheckAppOffline(string scriptPath)
        {
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
            if (IsInPlaceholderSimulationMode || environment.IsDynamicSku())
            {
                ExecuteAfterDelay(targetAction, TimeSpan.FromMilliseconds(ColdStartDelayMS), cancellationToken);
            }
            else
            {
                targetAction();
            }
        }

        public static bool TryResolveExtensionsMetadataPath(string rootScriptPath, out string extensionsMetadataPath, out string baseProbingPath)
        {
            baseProbingPath = null;

            // Verify if the file exists and apply fallback paths
            // The fallback order is:
            //   1 - Script root
            //       - If the system folder exists with metadata file at the root, use that as the base probing path
            //   2 - System folder
            extensionsMetadataPath = Path.Combine(rootScriptPath, "bin");
            if (!FileUtility.FileExists(Path.Combine(extensionsMetadataPath, ScriptConstants.ExtensionsMetadataFileName)))
            {
                extensionsMetadataPath = null;
                string systemPath = Path.Combine(rootScriptPath, ScriptConstants.AzureFunctionsSystemDirectoryName);

                if (FileUtility.FileExists(Path.Combine(rootScriptPath, ScriptConstants.ExtensionsMetadataFileName)))
                {
                    // As a fallback, allow extensions.json in the root path.
                    extensionsMetadataPath = rootScriptPath;

                    // If the system path exists, that should take precedence as the base probing path
                    if (Directory.Exists(systemPath))
                    {
                        baseProbingPath = systemPath;
                    }
                }
                else if (FileUtility.FileExists(Path.Combine(systemPath, ScriptConstants.ExtensionsMetadataFileName)))
                {
                    extensionsMetadataPath = systemPath;
                }
            }
            bool foundMetadata = !string.IsNullOrEmpty(extensionsMetadataPath);
            return foundMetadata;
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

        public static bool TryGetUriHost(string url, out string host)
        {
            host = null;
            if (Uri.TryCreate(url, UriKind.Absolute, out var resourceUri))
            {
                host = $"{resourceUri.Scheme}://{resourceUri.Host}";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Utility function to validate a blob URL by attempting to retrieve the account name from it.
        /// Borrowed from https://github.com/Azure/azure-sdk-for-net/blob/e0fd1cd415d8339947b20c3565c7adc7d7f60fbe/sdk/storage/Azure.Storage.Common/src/Shared/UriExtensions.cs
        /// </summary>
        private static string GetAccountNameFromDomain(string domain)
        {
            var accountEndIndex = domain.IndexOf(".", StringComparison.InvariantCulture);
            if (accountEndIndex >= 0)
            {
                var serviceStartIndex = domain.IndexOf(BlobServiceDomain, accountEndIndex, StringComparison.InvariantCultureIgnoreCase);
                return serviceStartIndex > -1 ? domain.Substring(0, accountEndIndex) : null;
            }
            return null;
        }

        public static bool IsResourceAzureBlobWithoutSas(Uri resourceUri)
        {
            // Screen out URLs that don't have <name>.blob.core... format
            if (string.IsNullOrEmpty(GetAccountNameFromDomain(resourceUri.Host)))
            {
                return false;
            }

            // Screen out URLs with an SAS token
            var queryParams = HttpUtility.ParseQueryString(resourceUri.Query.ToLower());
            if (queryParams != null && !string.IsNullOrEmpty(queryParams[SasVersionQueryParam]))
            {
                return false;
            }

            return true;
        }

        public static string GetSasTokenExpirationDate(string valueToParse, bool isAzureWebJobsStorage)
        {
            NameValueCollection queryParams = null;
            if (isAzureWebJobsStorage)
            {
                var azureWebJobsStorageSpan = valueToParse.AsSpan();
                var sasToken = azureWebJobsStorageSpan
                            .Slice(azureWebJobsStorageSpan.LastIndexOf(';') + 1).ToString();
                queryParams = HttpUtility.ParseQueryString(sasToken);
            }
            else
            {
                if (!Uri.IsWellFormedUriString(valueToParse, UriKind.Absolute))
                {
                    return null;
                }
                var resourceUri = new Uri(valueToParse);
                queryParams = HttpUtility.ParseQueryString(resourceUri.Query);
            }
            // Parse query params
            if (!string.IsNullOrEmpty(queryParams[SasTokenExpirationDate]))
            {
                return queryParams[SasTokenExpirationDate];
            }

            return null;
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
        /// Computes a stable non-cryptographic hash.
        /// </summary>
        /// <param name="value">The string to use for computation.</param>
        /// <returns>A stable, non-cryptographic, hash.</returns>
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

        public static void ValidateRetryOptions(RetryOptions
            retryOptions)
        {
            if (retryOptions == null)
            {
                return;
            }
            if (!retryOptions.MaxRetryCount.HasValue)
            {
                throw new ArgumentNullException(nameof(retryOptions.MaxRetryCount));
            }
            switch (retryOptions.Strategy)
            {
                case RetryStrategy.FixedDelay:
                    if (!retryOptions.DelayInterval.HasValue)
                    {
                        throw new ArgumentNullException(nameof(retryOptions.DelayInterval));
                    }
                    // ensure values specified to create FixedDelayRetryAttribute are valid
                    _ = new FixedDelayRetryAttribute(retryOptions.MaxRetryCount.Value, retryOptions.DelayInterval.ToString());
                    break;
                case RetryStrategy.ExponentialBackoff:
                    if (!retryOptions.MinimumInterval.HasValue)
                    {
                        throw new ArgumentNullException(nameof(retryOptions.MinimumInterval));
                    }
                    if (!retryOptions.MaximumInterval.HasValue)
                    {
                        throw new ArgumentNullException(nameof(retryOptions.MaximumInterval));
                    }
                    // ensure values specified to create ExponentialBackoffRetryAttribute are valid
                    _ = new ExponentialBackoffRetryAttribute(retryOptions.MaxRetryCount.Value, retryOptions.MinimumInterval.ToString(), retryOptions.MaximumInterval.ToString());
                    break;
            }
        }

        // EnableWorkerIndexing set through AzureWebjobsFeatuerFlag always take precdence
        // if AzureWebjobsFeatuerFlag is not set then WORKER_INDEXING_ENABLED hosting config controls stamplevel enablement
        // if WORKER_INDEXING_ENABLED is set and WORKER_INDEXING_DISABLED contains the customers app name worker indexing is then disabled for that customer only
        // Also Worker indexing is disabled for Logic apps
        public static bool CanWorkerIndex(IEnumerable<RpcWorkerConfig> workerConfigs, IEnvironment environment, FunctionsHostingConfigOptions functionsHostingConfigOptions)
        {
            string appName = environment.GetAzureWebsiteUniqueSlotName();
            bool workerIndexingEnabled = FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableWorkerIndexing, environment)
                                          || (functionsHostingConfigOptions.WorkerIndexingEnabled
                                          && !functionsHostingConfigOptions.WorkerIndexingDisabledApps.ToLowerInvariant().Split("|").Contains(appName)
                                           && !environment.IsLogicApp());

            if (!workerIndexingEnabled)
            {
                return false;
            }

            bool workerIndexingAvailable = false;
            if (workerConfigs != null)
            {
                var workerRuntime = environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime);
                var workerConfig = workerConfigs.FirstOrDefault(c => c.Description?.Language != null && c.Description.Language.Equals(workerRuntime, StringComparison.InvariantCultureIgnoreCase));

                // if feature flag is enabled and workerConfig.WorkerIndexing == true, then return true
                workerIndexingAvailable = workerConfig != null
                        && workerConfig.Description != null
                        && workerConfig.Description.WorkerIndexing != null
                        && workerConfig.Description.WorkerIndexing.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            return workerIndexingEnabled && workerIndexingAvailable;
        }

        public static void LogAutorestGeneratedJsonIfExists(string rootScriptPath, ILogger logger)
        {
            string autorestGeneratedJsonPath = Path.Combine(rootScriptPath, ScriptConstants.AutorestGeenratedMetadataFileName);
            JObject autorestGeneratedJson;

            if (FileUtility.FileExists(autorestGeneratedJsonPath))
            {
                string autorestGeneratedJsonPathContents = FileUtility.ReadAllText(autorestGeneratedJsonPath);
                try
                {
                    autorestGeneratedJson = JObject.Parse(autorestGeneratedJsonPathContents);
                    logger.AutorestGeneratedFunctionApplication(autorestGeneratedJson.ToString());
                }
                catch (JsonException ex)
                {
                    logger.IncorrectAutorestGeneratedJsonFile($"Unable to parse autorest configuration file '{autorestGeneratedJsonPath}'" +
                        $" with content '{autorestGeneratedJsonPathContents}' | exception: {ex.StackTrace}");
                }
                catch (Exception ex)
                {
                    logger.IncorrectAutorestGeneratedJsonFile($"Caught exception while parsing .autorest_generated.json | " +
                        $"exception: {ex.StackTrace}");
                }
            }
            // If we dont find the .autorest_generated.json in the function app, we just don't log anything.
        }

        public static void AccumulateDuplicateHeader(HttpContext httpContext, string headerName)
        {
            // Add duplicate http header to HttpContext.Items. This will be logged later in middleware.
            var previousHeaders = httpContext.Items[ScriptConstants.AzureFunctionsDuplicateHttpHeadersKey] as string ?? string.Empty;
            httpContext.Items[ScriptConstants.AzureFunctionsDuplicateHttpHeadersKey] = $"{previousHeaders} '{headerName}'";
        }

        public static bool IsValidZipSetting(string appSetting)
        {
            // valid values are 1 or an absolute URI
            return string.Equals(appSetting, "1") || IsValidZipUrl(appSetting);
        }

        public static bool IsValidZipUrl(string appSetting)
        {
            return Uri.TryCreate(appSetting, UriKind.Absolute, out Uri result);
        }

        public static FunctionAppContentEditingState GetFunctionAppContentEditingState(IEnvironment environment, IOptions<ScriptApplicationHostOptions> applicationHostOptions)
        {
            // For now, host can determine with certainty if contents are editable only for Linux Consumption apps. Return unknown for other SKUs.
            if (!environment.IsAnyLinuxConsumption())
            {
                return FunctionAppContentEditingState.Unknown;
            }
            if (!applicationHostOptions.Value.IsFileSystemReadOnly && environment.AzureFilesAppSettingsExist())
            {
                return FunctionAppContentEditingState.Allowed;
            }
            else
            {
                return FunctionAppContentEditingState.NotAllowed;
            }
        }

        public static bool TryReadAsBool(IDictionary<string, object> properties, string propertyKey, out bool result)
        {
            if (properties.TryGetValue(propertyKey, out object valueObject))
            {
                if (valueObject is bool boolValue)
                {
                    result = boolValue;
                    return true;
                }
                else if (valueObject is string stringValue)
                {
                    return bool.TryParse(stringValue, out result);
                }
            }

            return result = false;
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