// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.Configuration
{
    public class HostJsonFileConfigurationSource : IConfigurationSource
    {
        private readonly ILogger _logger;

        public HostJsonFileConfigurationSource(ScriptApplicationHostOptions applicationHostOptions, IEnvironment environment, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            HostOptions = applicationHostOptions;
            Environment = environment;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        public ScriptApplicationHostOptions HostOptions { get; }

        public IEnvironment Environment { get; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new HostJsonFileConfigurationProvider(this, _logger);
        }

        public class HostJsonFileConfigurationProvider : ConfigurationProvider
        {
            private static readonly string[] WellKnownHostJsonProperties = new[]
            {
                "version", "functionTimeout", "functions", "http", "watchDirectories", "queues", "serviceBus",
                "eventHub", "singleton", "logging", "aggregator", "healthMonitor", "extensionBundle", "managedDependencies"
            };

            private readonly HostJsonFileConfigurationSource _configurationSource;
            private readonly Stack<string> _path;
            private readonly ILogger _logger;

            public HostJsonFileConfigurationProvider(HostJsonFileConfigurationSource configurationSource, ILogger logger)
            {
                _configurationSource = configurationSource;
                _path = new Stack<string>();
                _logger = logger;
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
                InitializeHostConfig(hostFilePath, hostConfigObject);
                string sanitizedJson = SanitizeHostJson(hostConfigObject);

                _logger.HostConfigApplied();

                // Do not log these until after all the configuration is done so the proper filters are applied.
                _logger.HostConfigReading(hostFilePath);
                _logger.HostConfigRead(sanitizedJson);

                return hostConfigObject;
            }

            private void InitializeHostConfig(string hostJsonPath, JObject hostConfigObject)
            {
                // If the object is empty, initialize it to include the version and write the file.
                if (!hostConfigObject.HasValues)
                {
                    _logger.HostConfigEmpty();

                    hostConfigObject = GetDefaultHostConfigObject();
                    TryWriteHostJson(hostJsonPath, hostConfigObject);
                }

                if (hostConfigObject["version"]?.Value<string>() != "2.0")
                {
                    throw new HostConfigurationException($"The {ScriptConstants.HostMetadataFileName} file is missing the required 'version' property. See https://aka.ms/functions-hostjson for steps to migrate the configuration file.");
                }
            }

            internal JObject LoadHostConfig(string configFilePath)
            {
                JObject hostConfigObject;
                try
                {
                    string json = File.ReadAllText(configFilePath);
                    hostConfigObject = JObject.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new FormatException($"Unable to parse host configuration file '{configFilePath}'.", ex);
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    // if no file exists we default the config
                    _logger.HostConfigNotFound();

                    hostConfigObject = GetDefaultHostConfigObject();
                    TryWriteHostJson(configFilePath, hostConfigObject);
                }

                return hostConfigObject;
            }

            private JObject GetDefaultHostConfigObject()
            {
                var hostJsonJObj = JObject.Parse("{'version': '2.0'}");
                if (string.Equals(_configurationSource.Environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName), "powershell", StringComparison.InvariantCultureIgnoreCase)
                    && !_configurationSource.Environment.FileSystemIsReadOnly())
                {
                    hostJsonJObj.Add("managedDependency", JToken.Parse("{'Enabled': true}"));
                }

                return hostJsonJObj;
            }

            private void TryWriteHostJson(string filePath, JObject content)
            {
                if (!_configurationSource.Environment.FileSystemIsReadOnly())
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

            internal static string SanitizeHostJson(JObject hostJsonObject)
            {
                JObject sanitizedObject = new JObject();

                foreach (var propName in WellKnownHostJsonProperties)
                {
                    var propValue = hostJsonObject[propName];
                    if (propValue != null)
                    {
                        sanitizedObject[propName] = propValue;
                    }
                }

                return sanitizedObject.ToString();
            }
        }
    }
}
