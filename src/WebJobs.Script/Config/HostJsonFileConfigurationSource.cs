// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class HostJsonFileConfigurationSource : IConfigurationSource
    {
        private readonly ILogger _logger;
        private readonly IMetricsLogger _metricsLogger;

        public HostJsonFileConfigurationSource(ScriptApplicationHostOptions applicationHostOptions, IEnvironment environment, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            HostOptions = applicationHostOptions;
            Environment = environment;
            _metricsLogger = metricsLogger;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        public ScriptApplicationHostOptions HostOptions { get; }

        public IEnvironment Environment { get; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new HostJsonFileConfigurationProvider(this, _logger, _metricsLogger);
        }

        public class HostJsonFileConfigurationProvider : ConfigurationProvider
        {
            private static readonly string[] WellKnownHostJsonProperties = new[]
            {
                "version", "functionTimeout", "retry", "functions", "http", "watchDirectories", "watchFiles", "queues", "serviceBus",
                "eventHub", "singleton", "logging", "aggregator", "healthMonitor", "extensionBundle", "managedDependencies",
                "customHandler", "httpWorker", "extensions", "concurrency"
            };

            private static readonly string[] CredentialNameFragments = new[] { "password", "pwd", "key", "secret", "token", "sas" };

            private readonly HostJsonFileConfigurationSource _configurationSource;
            private readonly Stack<string> _path;
            private readonly ILogger _logger;
            private readonly IMetricsLogger _metricsLogger;

            public HostJsonFileConfigurationProvider(HostJsonFileConfigurationSource configurationSource, ILogger logger, IMetricsLogger metricsLogger)
            {
                _configurationSource = configurationSource;
                _path = new Stack<string>();
                _logger = logger;
                _metricsLogger = metricsLogger;
            }

            public override void Load()
            {
                JObject hostJson = LoadHostConfigurationFile();
                ProcessObject(hostJson);
            }

            private void ProcessObject(JObject hostJson)
            {
                foreach (var property in hostJson.Properties())
                {
                    _path.Push(property.Name);
                    ProcessProperty(property);
                    _path.Pop();
                }
            }

            private void ProcessProperty(JProperty property)
            {
                ProcessToken(property.Value);
            }

            private void ProcessToken(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        ProcessObject(token.Value<JObject>());
                        break;
                    case JTokenType.Array:
                        ProcessArray(token.Value<JArray>());
                        break;

                    case JTokenType.Integer:
                    case JTokenType.Float:
                    case JTokenType.String:
                    case JTokenType.Boolean:
                    case JTokenType.Null:
                    case JTokenType.Date:
                    case JTokenType.Raw:
                    case JTokenType.Bytes:
                    case JTokenType.TimeSpan:
                        string key = ConfigurationSectionNames.JobHost + ConfigurationPath.KeyDelimiter + ConfigurationPath.Combine(_path.Reverse());
                        Data[key] = token.Value<JValue>().ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        break;
                }
            }

            private void ProcessArray(JArray jArray)
            {
                for (int i = 0; i < jArray.Count; i++)
                {
                    _path.Push(i.ToString());
                    ProcessToken(jArray[i]);
                    _path.Pop();
                }
            }

            /// <summary>
            /// Read and apply host.json configuration.
            /// </summary>
            private JObject LoadHostConfigurationFile()
            {
                using (_metricsLogger.LatencyEvent(MetricEventNames.LoadHostConfigurationSource))
                {
                    // Before configuration has been fully read, configure a default logger factory
                    // to ensure we can log any configuration errors. There's no filters at this point,
                    // but that's okay since we can't build filters until we apply configuration below.
                    // We'll recreate the loggers after config is read. We initialize the public logger
                    // to the startup logger until we've read configuration settings and can create the real logger.
                    // The "startup" logger is used in this class for startup related logs. The public logger is used
                    // for all other logging after startup.

                    ScriptApplicationHostOptions options = _configurationSource.HostOptions;
                    string hostFilePath = Path.Combine(options.ScriptPath, ScriptConstants.HostMetadataFileName);
                    JObject hostConfigObject = LoadHostConfig(hostFilePath);
                    hostConfigObject = InitializeHostConfig(hostFilePath, hostConfigObject);
                    string sanitizedJson = SanitizeHostJson(hostConfigObject);

                    _logger.HostConfigApplied();

                    // Do not log these until after all the configuration is done so the proper filters are applied.
                    _logger.HostConfigReading(hostFilePath);
                    _logger.HostConfigRead(sanitizedJson);

                    return hostConfigObject;
                }
            }

            private JObject InitializeHostConfig(string hostJsonPath, JObject hostConfigObject)
            {
                using (_metricsLogger.LatencyEvent(MetricEventNames.InitializeHostConfiguration))
                {
                    // If the object is empty, initialize it to include the version and write the file.
                    if (!hostConfigObject.HasValues)
                    {
                        _logger.HostConfigEmpty();

                        hostConfigObject = GetDefaultHostConfigObject();
                        TryWriteHostJson(hostJsonPath, hostConfigObject);
                    }

                    string hostJsonVersion = hostConfigObject["version"]?.Value<string>();
                    if (string.IsNullOrEmpty(hostJsonVersion))
                    {
                        throw new HostConfigurationException($"The {ScriptConstants.HostMetadataFileName} file is missing the required 'version' property. See https://aka.ms/functions-hostjson for steps to migrate the configuration file.");
                    }

                    if (!hostJsonVersion.Equals("2.0"))
                    {
                        StringBuilder errorMsg = new StringBuilder($"'{hostJsonVersion}' is an invalid value for {ScriptConstants.HostMetadataFileName} 'version' property. We recommend you set the 'version' property to '2.0'. ");
                        if (hostJsonVersion.StartsWith("3", StringComparison.OrdinalIgnoreCase))
                        {
                            // In case the customer has confused host.json version with the fact that they're running Functions v3
                            errorMsg.Append($"This does not correspond to the function runtime version, only to the schema version of the {ScriptConstants.HostMetadataFileName} file. ");
                        }
                        errorMsg.Append($"See https://aka.ms/functions-hostjson for more information on this configuration file.");
                        throw new HostConfigurationException(errorMsg.ToString());
                    }
                }
                return hostConfigObject;
            }

            internal JObject LoadHostConfig(string configFilePath)
            {
                using (_metricsLogger.LatencyEvent(MetricEventNames.LoadHostConfiguration))
                {
                    JObject hostConfigObject;
                    try
                    {
                        string json = FileUtility.Instance.File.ReadAllText(configFilePath);
                        hostConfigObject = JObject.Parse(json);
                    }
                    catch (JsonException ex)
                    {
                        var message = $"Unable to parse host configuration file '{configFilePath}'.";
                        var formatException = new FormatException(message, ex);

                        _logger.LogDiagnosticEventError(
                            DiagnosticEventConstants.UnableToParseHostConfigurationFileErrorCode,
                            message,
                            DiagnosticEventConstants.UnableToParseHostConfigurationFileHelpLink,
                            formatException);

                        throw formatException;
                    }
                    catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                    {
                        // if no file exists we default the config
                        _logger.HostConfigNotFound();

                        // There isn't a clean way to create a new function app resource with host.json as initial payload.
                        // So a newly created function app from the portal would have no host.json. In that case we need to
                        // create a new function app with host.json that includes a matching extension bundle based on the app kind.
                        hostConfigObject = GetDefaultHostConfigObject();
                        string bundleId = _configurationSource.Environment.IsLogicApp() ?
                                            ScriptConstants.WorkFlowExtensionBundleId :
                                            ScriptConstants.DefaultExtensionBundleId;

                        string bundleVersion = _configurationSource.Environment.IsLogicApp() ?
                                            ScriptConstants.LogicAppDefaultExtensionBundleVersion :
                                            ScriptConstants.DefaultExtensionBundleVersion;

                        hostConfigObject = TryAddBundleConfiguration(hostConfigObject, bundleId, bundleVersion);
                        // Add bundle configuration if no file exists and file system is not read only
                        TryWriteHostJson(configFilePath, hostConfigObject);
                    }

                    return hostConfigObject;
                }
            }

            private JObject GetDefaultHostConfigObject()
            {
                var hostJsonJObj = JObject.Parse("{'version': '2.0'}");
                if (string.Equals(_configurationSource.Environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName), "powershell", StringComparison.InvariantCultureIgnoreCase)
                    && !_configurationSource.HostOptions.IsFileSystemReadOnly)
                {
                    hostJsonJObj.Add("managedDependency", JToken.Parse("{'Enabled': true}"));
                }

                return hostJsonJObj;
            }

            private void TryWriteHostJson(string filePath, JObject content)
            {
                if (!_configurationSource.HostOptions.IsFileSystemReadOnly)
                {
                    try
                    {
                        File.WriteAllText(filePath, content.ToString(Formatting.Indented));
                    }
                    catch
                    {
                        _logger.HostConfigCreationFailed();
                    }
                }
                else
                {
                    _logger.HostConfigFileSystemReadOnly();
                }
            }

            private JObject TryAddBundleConfiguration(JObject content, string bundleId, string bundleVersion)
            {
                if (!_configurationSource.HostOptions.IsFileSystemReadOnly)
                {
                    string bundleConfiguration = "{ 'id': '" + bundleId + "', 'version': '" + bundleVersion + "'}";
                    content.Add("extensionBundle", JToken.Parse(bundleConfiguration));
                    _logger.AddingExtensionBundleConfiguration(bundleConfiguration);
                }
                return content;
            }

            internal static string SanitizeHostJson(JObject hostJsonObject)
            {
                static bool IsPotentialCredential(string name)
                {
                    foreach (string fragment in CredentialNameFragments)
                    {
                        if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                static JToken Sanitize(JToken token)
                {
                    if (token is JObject obj)
                    {
                        JObject sanitized = new JObject();
                        foreach (var prop in obj)
                        {
                            if (IsPotentialCredential(prop.Key))
                            {
                                sanitized[prop.Key] = Sanitizer.SecretReplacement;
                            }
                            else
                            {
                                sanitized[prop.Key] = Sanitize(prop.Value);
                            }
                        }

                        return sanitized;
                    }

                    if (token is JArray arr)
                    {
                        JArray sanitized = new JArray();
                        foreach (var value in arr)
                        {
                            sanitized.Add(Sanitize(value));
                        }

                        return sanitized;
                    }

                    if (token.Type == JTokenType.String)
                    {
                        return Sanitizer.Sanitize(token.ToString());
                    }

                    return token;
                }

                JObject sanitizedObject = new JObject();
                foreach (var propName in WellKnownHostJsonProperties)
                {
                    var propValue = hostJsonObject[propName];
                    if (propValue != null)
                    {
                        sanitizedObject[propName] = Sanitize(propValue);
                    }
                }

                return sanitizedObject.ToString();
            }
        }
    }
}
