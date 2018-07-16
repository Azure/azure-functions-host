// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal class ScriptHostOptionsSetup : IConfigureOptions<ScriptHostOptions>
    {
        private readonly IConfiguration _configuration;

        public ScriptHostOptionsSetup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Configure(ScriptHostOptions options)
        {
            // Bind to all top level properties
            _configuration.GetSection(ConfigurationSectionNames.JobHost)
                ?.Bind(options);
        }

        ///// <summary>
        ///// Read and apply host.json configuration.
        ///// </summary>
        //private JObject ApplyHostConfiguration()
        //{
        //    // Before configuration has been fully read, configure a default logger factory
        //    // to ensure we can log any configuration errors. There's no filters at this point,
        //    // but that's okay since we can't build filters until we apply configuration below.
        //    // We'll recreate the loggers after config is read. We initialize the public logger
        //    // to the startup logger until we've read configuration settings and can create the real logger.
        //    // The "startup" logger is used in this class for startup related logs. The public logger is used
        //    // for all other logging after startup.
        //    // TODO: DI (FACAVAL) Fix this
        //    //ConfigureLoggerFactory();

        //    // TODO: DI (FACAVAL) Logger configuration to move to startup:
        //    // Logger = _startupLogger = _hostOptions.LoggerFactory.CreateLogger(LogCategories.Startup);

        //    string readingFileMessage = string.Format(CultureInfo.InvariantCulture, "Reading host configuration file '{0}'", _hostConfigFilePath);
        //    JObject hostConfigObject = LoadHostConfig(_hostConfigFilePath, _startupLogger);
        //    string sanitizedJson = SanitizeHostJson(hostConfigObject);
        //    string readFileMessage = $"Host configuration file read:{Environment.NewLine}{sanitizedJson}";

        //    // TODO: DI (FACAVAL) See method comments.
        //    //ApplyConfiguration(hostConfigObject, ScriptConfig, _startupLogger);

        //    if (_settingsManager.FileSystemIsReadOnly)
        //    {
        //        // we're in read-only mode so source files can't change
        //        ScriptConfig.FileWatchingEnabled = false;
        //    }

        //    // now the configuration has been read and applied re-create the logger
        //    // factory and loggers ensuring that filters and settings have been applied
        //    // TODO: DI (FACAVAL) TODO
        //    //ConfigureLoggerFactory(recreate: true);

        //    // TODO: DI (FACAVAL) Logger configuration to move to startup
        //    //_startupLogger = _hostOptions.LoggerFactory.CreateLogger(LogCategories.Startup);
        //    //Logger = _hostOptions.LoggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

        //    // Allow tests to modify anything initialized by host.json
        //    ScriptConfig.OnConfigurationApplied?.Invoke(ScriptConfig);
        //    _startupLogger.LogTrace("Host configuration applied.");

        //    // Do not log these until after all the configuration is done so the proper filters are applied.
        //    _startupLogger.LogInformation(readingFileMessage);
        //    _startupLogger.LogInformation(readFileMessage);

        //    // TODO: DI (FACAVAL) Move this to a more appropriate place
        //    // If they set the host id in the JSON, emit a warning that this could cause issues and they shouldn't do it.
        //    //if (ScriptConfig.HostOptions?.HostConfigMetadata?["id"] != null)
        //    //{
        //    //    _startupLogger.LogWarning("Host id explicitly set in the host.json. It is recommended that you remove the \"id\" property in your host.json.");
        //    //}

        //    if (string.IsNullOrEmpty(_hostOptions.HostId))
        //    {
        //        _hostOptions.HostId = Utility.GetDefaultHostId(_settingsManager, ScriptConfig);
        //    }
        //    if (string.IsNullOrEmpty(_hostOptions.HostId))
        //    {
        //        throw new InvalidOperationException("An 'id' must be specified in the host configuration.");
        //    }

        //    // TODO: DI (FACAVAL) Disabling core storage is now just a matter of
        //    // registering the appropriate services.
        //    //if (_storageConnectionString == null)
        //    //{
        //    //    // Disable core storage
        //    //    _hostOptions.StorageConnectionString = null;
        //    //}

        //    // only after configuration has been applied and loggers
        //    // have been created, raise the initializing event
        //    HostInitializing?.Invoke(this, EventArgs.Empty);

        //    return hostConfigObject;
        //}
    }
}
