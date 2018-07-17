// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Environment;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class HostJsonFileConfigurationSource : IConfigurationSource
    {
        public HostJsonFileConfigurationSource(IOptions<ScriptWebHostOptions> scriptHostOptions)
        {
            HostOptions = scriptHostOptions;
        }

        public IOptions<ScriptWebHostOptions> HostOptions { get; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new HostJsonFileConfigurationProvider(this);
        }

        private class HostJsonFileConfigurationProvider : ConfigurationProvider
        {
            private static readonly string[] WellKnownHostJsonProperties = new[]
            {
                "id", "functionTimeout", "functions", "http", "watchDirectories", "queues", "serviceBus",
                "eventHub", "tracing", "singleton", "logger", "aggregator", "applicationInsights", "healthMonitor"
            };

            private readonly HostJsonFileConfigurationSource _configurationSource;
            private readonly Stack<string> _path;

            public HostJsonFileConfigurationProvider(HostJsonFileConfigurationSource configurationSource)
            {
                _configurationSource = configurationSource;
                _path = new Stack<string>();
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
                // TODO: DI (FACAVAL) Fix this
                //ConfigureLoggerFactory();

                // TODO: DI (FACAVAL) Logger configuration to move to startup:
                // Logger = _startupLogger = _hostOptions.LoggerFactory.CreateLogger(LogCategories.Startup);
                ScriptWebHostOptions options = _configurationSource.HostOptions.Value;
                string hostFilePath = Path.Combine(options.ScriptPath, ScriptConstants.HostMetadataFileName);
                string readingFileMessage = string.Format(CultureInfo.InvariantCulture, "Reading host configuration file '{0}'", hostFilePath);
                JObject hostConfigObject = LoadHostConfig(hostFilePath);
                string sanitizedJson = SanitizeHostJson(hostConfigObject);
                string readFileMessage = $"Host configuration file read:{NewLine}{sanitizedJson}";

                // TODO: DI (FACAVAL) See method comments in ScriptHost
                //ApplyConfiguration(hostConfigObject, ScriptConfig, _startupLogger);

                // TODO: DI (FACAVAL) Review - this should likely move and not be just config
                //if (_settingsManager.FileSystemIsReadOnly)
                //{
                //    // we're in read-only mode so source files can't change
                //    ScriptConfig.FileWatchingEnabled = false;
                //}

                // now the configuration has been read and applied re-create the logger
                // factory and loggers ensuring that filters and settings have been applied
                // TODO: DI (FACAVAL) TODO
                //ConfigureLoggerFactory(recreate: true);

                // TODO: DI (FACAVAL) Logger configuration to move to startup
                //_startupLogger = _hostOptions.LoggerFactory.CreateLogger(LogCategories.Startup);
                //Logger = _hostOptions.LoggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

                // Allow tests to modify anything initialized by host.json
                //ScriptConfig.OnConfigurationApplied?.Invoke(ScriptConfig);
                //_startupLogger.LogTrace("Host configuration applied.");

                // Do not log these until after all the configuration is done so the proper filters are applied.
                //_startupLogger.LogInformation(readingFileMessage);
                //_startupLogger.LogInformation(readFileMessage);

                // TODO: DI (FACAVAL) Move this to a more appropriate place
                // If they set the host id in the JSON, emit a warning that this could cause issues and they shouldn't do it.
                if (hostConfigObject["id"] != null)
                {
                    // TODO: DI (FACAVAL) log
                    //_startupLogger.LogWarning("Host id explicitly set in the host.json. It is recommended that you remove the \"id\" property in your host.json.");
                }

                // TODO: DI (FACAVAL) Move to options setup
                //if (string.IsNullOrEmpty(_hostOptions.HostId))
                //{
                //    _hostOptions.HostId = Utility.GetDefaultHostId(_settingsManager, ScriptConfig);
                //}

                // TODO: DI (FACAVAL) Move to setup
                //if (string.IsNullOrEmpty(_hostOptions.HostId))
                //{
                //    throw new InvalidOperationException("An 'id' must be specified in the host configuration.");
                //}

                // TODO: DI (FACAVAL) Disabling core storage is now just a matter of
                // registering the appropriate services.
                //if (_storageConnectionString == null)
                //{
                //    // Disable core storage
                //    _hostOptions.StorageConnectionString = null;
                //}

                // only after configuration has been applied and loggers
                // have been created, raise the initializing event

                return hostConfigObject;
            }

            internal static JObject LoadHostConfig(string configFilePath)
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
                    // TODO: DI (FACAVAL) Log
                    //logger.LogInformation("No host configuration file found. Using default.");
                    hostConfigObject = new JObject();
                }

                return hostConfigObject;
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
