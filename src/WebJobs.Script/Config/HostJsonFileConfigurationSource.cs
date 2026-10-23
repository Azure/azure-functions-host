// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
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

        public HostJsonFileConfigurationSource(
            HostJsonFileConfigurationOptions options,
            ILoggerFactory loggerFactory,
            IMetricsLogger metricsLogger)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(metricsLogger);

            Options = options;
            _logger = loggerFactory.CreateLogger(LogCategories.Startup);
            _metricsLogger = metricsLogger;
        }

        private HostJsonFileConfigurationOptions Options { get; }

        private ScriptApplicationHostOptions HostOptions => Options.Host;

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new HostJsonFileConfigurationProvider(this, _logger, _metricsLogger);
        }

        public class HostJsonFileConfigurationProvider : ConfigurationProvider
        {
            private static readonly string[] WellKnownHostJsonProperties =
            [
                "version", "functionTimeout", "retry", "functions", "http", "watchDirectories", "watchFiles", "queues", "serviceBus",
                "eventHub", "singleton", "logging", "aggregator", "healthMonitor", "extensionBundle", "managedDependencies",
                "customHandler", "httpWorker", "extensions", "concurrency", "telemetryMode", ConfigurationSectionNames.SendCanceledInvocationsToWorker,
                ConfigurationSectionNames.MetadataProviderTimeout, "isDefaultHostConfig", "configurationProfile"
            ];

            private readonly HostJsonFileConfigurationSource _configurationSource;
            private readonly ILogger _logger;
            private readonly IMetricsLogger _metricsLogger;

            public HostJsonFileConfigurationProvider(
                HostJsonFileConfigurationSource configurationSource, ILogger logger, IMetricsLogger metricsLogger)
            {
                _configurationSource = configurationSource;
                _logger = logger;
                _metricsLogger = metricsLogger;
            }

            private HostJsonFileConfigurationOptions Options => _configurationSource.Options;

            public override void Load()
            {
                JObject hostJson = LoadHostConfigurationFile();

                // Apply profile settings first, so that they can be overridden by host.json settings.
                HostConfigurationProfile profile = GetConfigProfile(hostJson);
                _logger.LogDebug("Loading host configuration profile '{profileName}'.", profile.Name);
                foreach ((string key, string value) in HostConfigurationSnapshot.Flatten(hostJson, profile))
                {
                    Data[key] = value;
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
                    string hostFilePath = Path.Combine(Options.Host.ScriptPath, ScriptConstants.HostMetadataFileName);
                    JObject hostConfigObject = LoadHostConfig(hostFilePath);
                    hostConfigObject = InitializeHostConfig(hostFilePath, hostConfigObject);

                    Func<string, bool> selector = (name) => WellKnownHostJsonProperties.Contains(name);
                    var sanitizedHostConfigObject = Sanitizer.Sanitize(hostConfigObject, selector);
                    string sanitizedJson = sanitizedHostConfigObject.ToString();

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

                    HostConfigurationSnapshot.ValidateVersion(hostConfigObject);
                }

                return hostConfigObject;
            }

            private JObject LoadHostConfig(string configFilePath)
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
                        hostConfigObject = HostConfigurationSnapshot.CreateDefault(Options, includeBundle: true);
                        if (hostConfigObject["extensionBundle"] is JObject bundle)
                        {
                            string bundleConfiguration = "{ 'id': '" + bundle["id"] + "', 'version': '" + bundle["version"] + "'}";
                            _logger.AddingExtensionBundleConfiguration(bundleConfiguration);
                        }

                        // Add bundle configuration if no file exists and file system is not read only
                        TryWriteHostJson(configFilePath, hostConfigObject);
                    }

                    return hostConfigObject;
                }
            }

            private JObject GetDefaultHostConfigObject()
            {
                return HostConfigurationSnapshot.CreateDefault(Options);
            }

            private HostConfigurationProfile GetConfigProfile(JObject hostFile)
            {
                ArgumentNullException.ThrowIfNull(hostFile);

                return HostConfigurationSnapshot.GetProfile(hostFile, Options);
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
        }
    }
}