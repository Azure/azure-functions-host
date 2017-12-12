// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHost : JobHost
    {
        internal const int DebugModeTimeoutMinutes = 15;
        private const string HostAssemblyName = "ScriptHost";
        private const string GeneratedTypeNamespace = "Host";
        internal const string GeneratedTypeName = "Functions";
        private readonly IScriptHostEnvironment _scriptHostEnvironment;
        private static readonly string[] WellKnownHostJsonProperties = new[]
        {
            "id", "functionTimeout", "http", "watchDirectories", "functions", "queues", "serviceBus",
            "eventHub", "tracing", "singleton", "logger", "aggregator", "applicationInsights", "healthMonitor"
        };

        private string _instanceId;
        private Func<Task> _restart;
        private Action _shutdown;
        private AutoRecoveringFileSystemWatcher _debugModeFileWatcher;
        private ImmutableArray<string> _directorySnapshot;
        private PrimaryHostCoordinator _blobLeaseManager;
        internal static readonly TimeSpan MinFunctionTimeout = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan DefaultFunctionTimeout = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan MaxFunctionTimeout = TimeSpan.FromMinutes(10);
        private static readonly Regex FunctionNameValidationRegex = new Regex(@"^[a-z][a-z0-9_\-]{0,127}$(?<!^host$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ProxyNameValidationRegex = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly string Version = GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
        private ScriptSettingsManager _settingsManager;
        private bool _shutdownScheduled;
        private ILogger _startupLogger;
        private FileWatcherEventSource _fileEventSource;
        private IDisposable _fileEventsSubscription;
        private ProxyClientExecutor _proxyClient;
        private ILoggerFactory _loggerFactory;
        private TraceMonitor _traceMonitor;
        private JobHostConfiguration _hostConfig;
        private List<FunctionDescriptorProvider> _descriptorProviders;

        protected internal ScriptHost(IScriptHostEnvironment environment,
            IScriptEventManager eventManager,
            ScriptHostConfiguration scriptConfig = null,
            ScriptSettingsManager settingsManager = null,
            ProxyClientExecutor proxyClient = null)
            : base(scriptConfig.HostConfig)
        {
            scriptConfig = scriptConfig ?? new ScriptHostConfiguration();
            _hostConfig = scriptConfig.HostConfig;
            if (!Path.IsPathRooted(scriptConfig.RootScriptPath))
            {
                scriptConfig.RootScriptPath = Path.Combine(Environment.CurrentDirectory, scriptConfig.RootScriptPath);
            }
            ScriptConfig = scriptConfig;
            _scriptHostEnvironment = environment;
            FunctionErrors = new Dictionary<string, Collection<string>>(StringComparer.OrdinalIgnoreCase);
#if FEATURE_NODE
            NodeFunctionInvoker.UnhandledException += OnUnhandledException;
#endif
            TraceWriter = ScriptConfig.TraceWriter;
            EventManager = eventManager;

            _settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            _proxyClient = proxyClient;
        }

        public event EventHandler HostInitializing;

        public event EventHandler HostInitialized;

        public event EventHandler HostStarted;

        public event EventHandler IsPrimaryChanged;

        public string InstanceId
        {
            get
            {
                if (_instanceId == null)
                {
                    _instanceId = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteInstanceId)
                        ?? Environment.MachineName.GetHashCode().ToString("X").PadLeft(32, '0');

                    _instanceId = _instanceId.Substring(0, 32);
                }

                return _instanceId;
            }
        }

        public IScriptEventManager EventManager { get; }

        public TraceWriter TraceWriter { get; internal set; }

        public ILogger Logger { get; internal set; }

        public virtual IFunctionTraceWriterFactory FunctionTraceWriterFactory { get; set; }

        public ScriptHostConfiguration ScriptConfig { get; private set; }

        /// <summary>
        /// Gets the collection of all valid Functions. For functions that are in error
        /// and were unable to load successfully, consult the <see cref="FunctionErrors"/> collection.
        /// </summary>
        public virtual Collection<FunctionDescriptor> Functions { get; private set; }

        // Maps from FunctionName to a set of errors for that function.
        public virtual Dictionary<string, Collection<string>> FunctionErrors { get; private set; }

        public virtual bool IsPrimary
        {
            get
            {
                return _blobLeaseManager?.HasLease ?? false;
            }
        }

        public ScriptSettingsManager SettingsManager
        {
            get
            {
                return _settingsManager;
            }

            private set
            {
                _settingsManager = value;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the host is in debug mode.
        /// </summary>
        public virtual bool InDebugMode
        {
            get
            {
                return (DateTime.UtcNow - LastDebugNotify).TotalMinutes < DebugModeTimeoutMinutes;
            }
        }

        /// <summary>
        /// Gets a value indicating whether logs should be written to disk.
        /// </summary>
        internal virtual bool FileLoggingEnabled
        {
            get
            {
                return ScriptConfig.FileLoggingMode == FileLoggingMode.Always ||
                    (ScriptConfig.FileLoggingMode == FileLoggingMode.DebugOnly && InDebugMode);
            }
        }

        internal DateTime LastDebugNotify { get; set; }

        /// <summary>
        /// Returns true if the specified name is the name of a known function,
        /// regardless of whether the function is in error.
        /// </summary>
        /// <param name="name">The name of the function to check for.</param>
        /// <returns>True if the name matches a function; otherwise, false.</returns>
        public bool IsFunction(string name)
        {
            if (!string.IsNullOrEmpty(name) &&
                (Functions.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                FunctionErrors.ContainsKey(name)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Lookup a function by name
        /// </summary>
        /// <param name="name">name of function</param>
        /// <returns>function or null if not found</returns>
        public FunctionDescriptor GetFunctionOrNull(string name)
        {
            return Functions.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Notifies this host that it should be in debug mode.
        /// </summary>
        public void NotifyDebug()
        {
            // This is redundant, since we're also watching the debug marker
            // file. However, we leave this here for assurances.
            LastDebugNotify = DateTime.UtcNow;

            try
            {
                // create or update the debug sentinel file to trigger a
                // debug timeout update across all instances
                string debugSentinelFileName = Path.Combine(ScriptConfig.RootLogPath, "Host", ScriptConstants.DebugSentinelFileName);
                if (!File.Exists(debugSentinelFileName))
                {
                    File.WriteAllText(debugSentinelFileName, "This is a system managed marker file used to control runtime debug mode behavior.");
                }
                else
                {
                    File.SetLastWriteTimeUtc(debugSentinelFileName, DateTime.UtcNow);
                }
            }
            catch (Exception ex)
            {
                // best effort
                string message = "Unable to update the debug sentinel file.";
                TraceWriter.Error(message, ex);
                Logger?.LogError(0, ex, message);

                if (ex.IsFatal())
                {
                    throw;
                }
            }
        }

        internal static void AddFunctionError(Dictionary<string, Collection<string>> functionErrors, string functionName, string error, bool isFunctionShortName = false)
        {
            functionName = isFunctionShortName ? functionName : Utility.GetFunctionShortName(functionName);

            Collection<string> functionErrorCollection = new Collection<string>();
            if (!functionErrors.TryGetValue(functionName, out functionErrorCollection))
            {
                functionErrors[functionName] = functionErrorCollection = new Collection<string>();
            }
            functionErrorCollection.Add(error);
        }

        public virtual async Task CallAsync(string method, Dictionary<string, object> arguments, CancellationToken cancellationToken = default(CancellationToken))
        {
            await base.CallAsync(method, arguments, cancellationToken);
        }

        /// <summary>
        /// Performs all required initialization on the host. Must be called before the host is started.
        /// </summary>
        public void Initialize()
        {
            FileUtility.EnsureDirectoryExists(ScriptConfig.RootScriptPath);
            string hostLogPath = Path.Combine(ScriptConfig.RootLogPath, "Host");
            FileUtility.EnsureDirectoryExists(hostLogPath);
            string debugSentinelFileName = Path.Combine(hostLogPath, ScriptConstants.DebugSentinelFileName);
            this.LastDebugNotify = File.GetLastWriteTime(debugSentinelFileName);

            FunctionTraceWriterFactory = new FunctionTraceWriterFactory(ScriptConfig);

            IMetricsLogger metricsLogger = CreateMetricsLogger();

            using (metricsLogger.LatencyEvent(MetricEventNames.HostStartupLatency))
            {
                // read host.json and apply to JobHostConfiguration
                string hostConfigFilePath = Path.Combine(ScriptConfig.RootScriptPath, ScriptConstants.HostMetadataFileName);

                // If it doesn't exist, create an empty JSON file
                if (!File.Exists(hostConfigFilePath))
                {
                    File.WriteAllText(hostConfigFilePath, "{}");
                }

                if (_hostConfig.IsDevelopment || InDebugMode)
                {
                    // If we're in debug/development mode, use optimal debug settings
                    _hostConfig.UseDevelopmentSettings();
                }

                Func<string, FunctionDescriptor> funcLookup = (name) => this.GetFunctionOrNull(name);
                _hostConfig.AddService(funcLookup);

                // Set up a host level TraceMonitor that will receive notification
                // of ALL errors that occur. This allows us to inspect/log errors.
                _traceMonitor = new TraceMonitor()
                    .Filter(p => { return true; })
                    .Subscribe(HandleHostError);
                _hostConfig.Tracing.Tracers.Add(_traceMonitor);

                TraceLevel hostTraceLevel = _hostConfig.Tracing.ConsoleLevel;
                if (ScriptConfig.FileLoggingMode != FileLoggingMode.Never)
                {
                    // Host file logging is only done conditionally
                    string hostLogFilePath = Path.Combine(ScriptConfig.RootLogPath, "Host");
                    TraceWriter fileTraceWriter = new FileTraceWriter(hostLogFilePath, hostTraceLevel).Conditional(p => FileLoggingEnabled);

                    if (TraceWriter != null)
                    {
                        // create a composite writer so our host logs are written to both
                        TraceWriter = new CompositeTraceWriter(new[] { TraceWriter, fileTraceWriter });
                    }
                    else
                    {
                        TraceWriter = fileTraceWriter;
                    }
                }

                if (TraceWriter != null)
                {
                    _hostConfig.Tracing.Tracers.Add(TraceWriter);
                }
                else
                {
                    // if no TraceWriter has been configured, default it to Console
                    TraceWriter = new ConsoleTraceWriter(hostTraceLevel);
                }

                // Before configuration has been fully read, configure a default logger factory
                // to ensure we can log any configuration errors. There's no filters at this point,
                // but that's okay since we can't build filters until we apply configuration below.
                // We'll recreate the loggers after config is read. We initialize the public logger
                // to the startup logger until we've read configuration settings and can create the real logger.
                // The "startup" logger is used in this class for startup related logs. The public logger is used
                // for all other logging after startup.
                ConfigureLoggerFactory();
                Logger = _startupLogger = _hostConfig.LoggerFactory.CreateLogger(LogCategories.Startup);

                string readingFileMessage = string.Format(CultureInfo.InvariantCulture, "Reading host configuration file '{0}'", hostConfigFilePath);
                TraceWriter.Info(readingFileMessage);

                string json = File.ReadAllText(hostConfigFilePath);
                JObject hostConfigObject;
                try
                {
                    hostConfigObject = JObject.Parse(json);
                }
                catch (JsonException ex)
                {
                    // If there's a parsing error, write out the previous messages without filters to ensure
                    // they're logged
                    _startupLogger.LogInformation(readingFileMessage);
                    throw new FormatException(string.Format("Unable to parse {0} file.", ScriptConstants.HostMetadataFileName), ex);
                }

                string sanitizedJson = SanitizeHostJson(hostConfigObject);
                string readFileMessage = $"Host configuration file read:{Environment.NewLine}{sanitizedJson}";
                TraceWriter.Info(readFileMessage);

                ApplyConfiguration(hostConfigObject, ScriptConfig);

                // now the configuration has been read and applied re-create the logger
                // factory and loggers ensuring that filters and settings have been applied
                ConfigureLoggerFactory(recreate: true);
                _startupLogger = _hostConfig.LoggerFactory.CreateLogger(LogCategories.Startup);
                Logger = _hostConfig.LoggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

                // Allow tests to modify anything initialized by host.json
                ScriptConfig.OnConfigurationApplied?.Invoke(ScriptConfig);

                // only after configuration has been applied and loggers have been created, raise the initializing event
                HostInitializing?.Invoke(this, EventArgs.Empty);

                // Do not log these until after all the configuration is done so the proper filters are applied.
                _startupLogger.LogInformation(readingFileMessage);
                _startupLogger.LogInformation(readFileMessage);

                if (string.IsNullOrEmpty(_hostConfig.HostId))
                {
                    _hostConfig.HostId = Utility.GetDefaultHostId(_settingsManager, ScriptConfig);
                }
                if (string.IsNullOrEmpty(_hostConfig.HostId))
                {
                    throw new InvalidOperationException("An 'id' must be specified in the host configuration.");
                }

                _debugModeFileWatcher = new AutoRecoveringFileSystemWatcher(hostLogPath, ScriptConstants.DebugSentinelFileName,
                    includeSubdirectories: false, changeTypes: WatcherChangeTypes.Created | WatcherChangeTypes.Changed);

                _debugModeFileWatcher.Changed += OnDebugModeFileChanged;

                var storageString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
                if (storageString == null)
                {
                    // Disable core storage
                    _hostConfig.StorageConnectionString = null;
                }

                if (ScriptConfig.FileWatchingEnabled)
                {
                    _fileEventSource = new FileWatcherEventSource(EventManager, EventSources.ScriptFiles, ScriptConfig.RootScriptPath);

                    _fileEventsSubscription = EventManager.OfType<FileEvent>()
                        .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                        .Subscribe(e => OnFileChanged(e.FileChangeArguments));
                }

                // If a file change should result in a restart, we debounce the event to
                // ensure that only a single restart is triggered within a specific time window.
                // This allows us to deal with a large set of file change events that might
                // result from a bulk copy/unzip operation. In such cases, we only want to
                // restart after ALL the operations are complete and there is a quiet period.
                _restart = RestartAsync;
                _restart = _restart.Debounce(500);

                _shutdown = Shutdown;
                _shutdown = _shutdown.Debounce(500);

                // take a snapshot so we can detect function additions/removals
                _directorySnapshot = Directory.EnumerateDirectories(ScriptConfig.RootScriptPath).ToImmutableArray();

                // Scan the function.json early to determine the binding extensions used
                var functionMetadata = ReadFunctionMetadata(ScriptConfig, TraceWriter, _startupLogger, FunctionErrors, _settingsManager);
                var extensionLoader = new ExtensionLoader(ScriptConfig, TraceWriter, _startupLogger);
                var usedBindingTypes = extensionLoader.DiscoverBindingTypes(functionMetadata);

                var bindingProviders = LoadBindingProviders(ScriptConfig, hostConfigObject, TraceWriter, _startupLogger, usedBindingTypes);
                ScriptConfig.BindingProviders = bindingProviders;

                var coreBinder = bindingProviders.OfType<CoreExtensionsScriptBindingProvider>().First();
                coreBinder.AppDirectory = ScriptConfig.RootScriptPath;

                // Allow BindingProviders to initialize
                foreach (var bindingProvider in ScriptConfig.BindingProviders)
                {
                    try
                    {
                        bindingProvider.Initialize();
                    }
                    catch (Exception ex)
                    {
                        // If we're unable to initialize a binding provider for any reason, log the error
                        // and continue
                        string errorMsg = string.Format("Error initializing binding provider '{0}'", bindingProvider.GetType().FullName);
                        TraceWriter.Error(errorMsg, ex);
                        _startupLogger?.LogError(0, ex, errorMsg);
                    }
                }
                extensionLoader.LoadBuiltinExtensions(usedBindingTypes);

                var directTypes = GetDirectTypes(functionMetadata);
                extensionLoader.LoadDirectlyReferencedExtensions(directTypes);
                extensionLoader.LoadCustomExtensions();

                // Now all extensions have been loaded, the metadata is finalized.
                // There's a single script binding instance that services all extensions.
                // give that script binding the metadata for all loaded extensions so it can dispatch to them.
                var generalProvider = ScriptConfig.BindingProviders.OfType<GeneralScriptBindingProvider>().First();
                var metadataProvider = this.CreateMetadataProvider();
                generalProvider.CompleteInitialization(metadataProvider);

                // Do this after we've loaded the custom extensions. That gives an extension an opportunity to plug in their own implementations.
                if (storageString != null)
                {
                    var lockManager = (IDistributedLockManager)Services.GetService(typeof(IDistributedLockManager));
                    _blobLeaseManager = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), _hostConfig.HostId, InstanceId, TraceWriter, _hostConfig.LoggerFactory);
                }

                // Create the lease manager that will keep handle the primary host blob lease acquisition and renewal
                // and subscribe for change notifications.
                if (_blobLeaseManager != null)
                {
                    _blobLeaseManager.HasLeaseChanged += BlobLeaseManagerHasLeaseChanged;
                }

                _descriptorProviders = new List<FunctionDescriptorProvider>()
                {
                    new ScriptFunctionDescriptorProvider(this, ScriptConfig),
#if FEATURE_NODE
                    new NodeFunctionDescriptorProvider(this, ScriptConfig),
#endif
                    new DotNetFunctionDescriptorProvider(this, ScriptConfig),
#if FEATURE_POWERSHELL
                    new PowerShellFunctionDescriptorProvider(this, ScriptConfig)
#endif
                };

                // read all script functions and apply to JobHostConfiguration
                Collection<FunctionDescriptor> functions = GetFunctionDescriptors(functionMetadata);
                Collection<CustomAttributeBuilder> typeAttributes = CreateTypeAttributes(ScriptConfig);
                string typeName = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GeneratedTypeNamespace, GeneratedTypeName);

                string generatingMsg = string.Format(CultureInfo.InvariantCulture, "Generating {0} job function(s)", functions.Count);
                TraceWriter.Info(generatingMsg);
                _startupLogger?.LogInformation(generatingMsg);

                Type type = FunctionGenerator.Generate(HostAssemblyName, typeName, typeAttributes, functions);
                List<Type> types = new List<Type>();
                types.Add(type);

                types.AddRange(directTypes);

                _hostConfig.TypeLocator = new TypeLocator(types);

                Functions = functions;

                if (ScriptConfig.FileLoggingMode != FileLoggingMode.Never)
                {
                    PurgeOldLogDirectories();
                }
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

        // Validate that for any precompiled assembly, all functions have the same configuration precedence.
        private void VerifyPrecompileStatus(IEnumerable<FunctionDescriptor> functions)
        {
            HashSet<string> illegalScriptAssemblies = new HashSet<string>();

            Dictionary<string, bool> mapAssemblySettings = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in functions)
            {
                var metadata = function.Metadata;
                var scriptFile = metadata.ScriptFile;
                if (scriptFile != null && scriptFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    bool isDirect = metadata.IsDirect;
                    bool prevIsDirect;
                    if (mapAssemblySettings.TryGetValue(scriptFile, out prevIsDirect))
                    {
                        if (prevIsDirect != isDirect)
                        {
                            illegalScriptAssemblies.Add(scriptFile);
                        }
                    }
                    mapAssemblySettings[scriptFile] = isDirect;
                }
            }

            foreach (var function in functions)
            {
                var metadata = function.Metadata;
                var scriptFile = metadata.ScriptFile;

                if (illegalScriptAssemblies.Contains(scriptFile))
                {
                    // Error. All entries pointing to the same dll must have the same value for IsDirect
                    string msg = string.Format(CultureInfo.InvariantCulture, "Configuration error: all functions in {0} must have the same value for 'configurationSource'.",
                        scriptFile);

                    // Adding a function error will cause this function to get ignored
                    AddFunctionError(this.FunctionErrors, metadata.Name, msg);

                    TraceWriter.Info(msg);
                    _startupLogger?.LogInformation(msg);
                }

                return;
            }
        }

        /// <summary>
        /// Get the set of types that should be directly loaded. These have the "configurationSource" : "attributes" set.
        /// They will be indexed and invoked directly by the WebJobs SDK and skip the IL generator and invoker paths.
        /// </summary>
        private IEnumerable<Type> GetDirectTypes(IEnumerable<FunctionMetadata> functionMetadataList)
        {
            HashSet<Type> visitedTypes = new HashSet<Type>();

            foreach (var metadata in functionMetadataList)
            {
                if (!metadata.IsDirect)
                {
                    continue;
                }

                string path = metadata.ScriptFile;
                var typeName = Utility.GetFullClassName(metadata.EntryPoint);

                Assembly assembly = Assembly.LoadFrom(path);
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    visitedTypes.Add(type);
                }
                else
                {
                    // This likely means the function.json and dlls are out of sync. Perhaps a badly generated function.json?
                    string msg = $"Failed to load type '{typeName}' from '{path}'";
                    TraceWriter.Warning(msg);
                    _startupLogger?.LogWarning(msg);
                }
            }
            return visitedTypes;
        }

        private IMetricsLogger CreateMetricsLogger()
        {
            IMetricsLogger metricsLogger = ScriptConfig.HostConfig.GetService<IMetricsLogger>();
            if (metricsLogger == null)
            {
                metricsLogger = new MetricsLogger();
                ScriptConfig.HostConfig.AddService<IMetricsLogger>(metricsLogger);
            }
            return metricsLogger;
        }

        private void ConfigureLoggerFactory(bool recreate = false)
        {
            // Ensure we always have an ILoggerFactory,
            // regardless of whether AppInsights is registered or not
            if (recreate || _hostConfig.LoggerFactory == null)
            {
                _hostConfig.LoggerFactory = new LoggerFactory();

                // If we've created the LoggerFactory, then we are responsible for
                // disposing. Store this locally for disposal later. We can't rely
                // on accessing this directly from ScriptConfig.HostConfig as the
                // ScriptConfig is re-used for every host.
                _loggerFactory = _hostConfig.LoggerFactory;
            }

            ConfigureLoggerFactory(ScriptConfig, FunctionTraceWriterFactory, _settingsManager, () => FileLoggingEnabled);
        }

        internal static void ConfigureLoggerFactory(ScriptHostConfiguration scriptConfig, IFunctionTraceWriterFactory traceWriteFactory,
            ScriptSettingsManager settingsManager, Func<bool> isFileLoggingEnabled)
        {
            // Register a file logger that only logs user logs and only if file logging is enabled.
            // We don't allow this to be replaced; if you want to disable it, you can use host.json to do so.
            scriptConfig.HostConfig.LoggerFactory.AddProvider(new FileLoggerProvider(traceWriteFactory,
                (category, level) => (category == LogCategories.Function) && isFileLoggingEnabled()));

            scriptConfig.LoggerFactoryBuilder.AddLoggerProviders(scriptConfig.HostConfig.LoggerFactory, scriptConfig, settingsManager);
        }

        private void TraceFileChangeRestart(string changeDescription, string changeType, string path, bool isShutdown)
        {
            string fileChangeMsg = string.Format(CultureInfo.InvariantCulture, "{0} change of type '{1}' detected for '{2}'", changeDescription, changeType, path);
            TraceWriter.Info(fileChangeMsg);
            Logger?.LogInformation(fileChangeMsg);

            string action = isShutdown ? "shutdown" : "restart";
            string signalMessage = $"Host configuration has changed. Signaling {action}";
            TraceWriter.Info(signalMessage);
            Logger?.LogInformation(signalMessage);
        }

        internal static Collection<CustomAttributeBuilder> CreateTypeAttributes(ScriptHostConfiguration scriptConfig)
        {
            Collection<CustomAttributeBuilder> customAttributes = new Collection<CustomAttributeBuilder>();

            // apply the timeout settings to our type
            if (scriptConfig.FunctionTimeout != null)
            {
                Type timeoutType = typeof(TimeoutAttribute);
                ConstructorInfo ctorInfo = timeoutType.GetConstructor(new[] { typeof(string) });

                PropertyInfo[] propertyInfos = new[]
                {
                    timeoutType.GetProperty("ThrowOnTimeout"),
                    timeoutType.GetProperty("TimeoutWhileDebugging")
                };

                // Hard-code these for now. Eventually elevate to config
                object[] propertyValues = new object[]
                {
                    true,
                    true
                };

                CustomAttributeBuilder timeoutBuilder = new CustomAttributeBuilder(
                    ctorInfo,
                    new object[] { scriptConfig.FunctionTimeout.ToString() },
                    propertyInfos,
                    propertyValues);

                customAttributes.Add(timeoutBuilder);
            }

            return customAttributes;
        }

        internal async Task RestartAsync()
        {
            if (_shutdownScheduled)
            {
                // If a shutdown was scheduled, skip the restart
                return;
            }

            // Request a host restart
            _scriptHostEnvironment.RestartHost();

#if FEATURE_NODE
            // whenever we're restarting the host, we want to let the Node
            // invoker know so it can clear the require cache, etc.
            await NodeFunctionInvoker.OnHostRestartAsync();
#endif
        }

        internal void Shutdown()
        {
            _scriptHostEnvironment.Shutdown();
        }

        /// <summary>
        /// Whenever the debug marker file changes we update our debug timeout
        /// </summary>
        private void OnDebugModeFileChanged(object sender, FileSystemEventArgs e)
        {
            LastDebugNotify = DateTime.UtcNow;
        }

        private void BlobLeaseManagerHasLeaseChanged(object sender, EventArgs e)
        {
            IsPrimaryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Iterate through all function log directories and remove any that don't
        /// correspond to a function.
        /// </summary>
        private void PurgeOldLogDirectories()
        {
            try
            {
                if (!Directory.Exists(this.ScriptConfig.RootScriptPath))
                {
                    return;
                }

                // Create a lookup of all potential functions (whether they're valid or not)
                // It is important that we determine functions based on the presence of a folder,
                // not whether we've identified a valid function from that folder. This ensures
                // that we don't delete logs/secrets for functions that transition into/out of
                // invalid unparsable states.
                var functionLookup = Directory.EnumerateDirectories(this.ScriptConfig.RootScriptPath).ToLookup(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                string rootLogFilePath = Path.Combine(this.ScriptConfig.RootLogPath, "Function");
                if (!Directory.Exists(rootLogFilePath))
                {
                    return;
                }

                var logFileDirectory = new DirectoryInfo(rootLogFilePath);
                foreach (var logDir in logFileDirectory.GetDirectories())
                {
                    if (!functionLookup.Contains(logDir.Name))
                    {
                        // the directory no longer maps to a running function
                        // so delete it
                        try
                        {
                            // destructive operation, thus log
                            string removeLogMessage = $"Deleting log directory '{logDir.FullName}'";
                            TraceWriter.Verbose(removeLogMessage);
                            _startupLogger?.LogDebug(removeLogMessage);
                            logDir.Delete(recursive: true);
                        }
                        catch
                        {
                            // Purge is best effort
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Purge is best effort
                string errorMsg = "An error occurred while purging log files";
                TraceWriter.Error(errorMsg, ex);
                _startupLogger?.LogError(0, ex, errorMsg);
            }
        }

        private static Collection<ScriptBindingProvider> LoadBindingProviders(ScriptHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter, ILogger logger, IEnumerable<string> usedBindingTypes)
        {
            JobHostConfiguration hostConfig = config.HostConfig;

            // Register our built in extensions
            var bindingProviderTypes = new Collection<Type>()
            {
                // binding providers defined in this assembly
                typeof(WebJobsCoreScriptBindingProvider),

                // binding providers defined in known extension assemblies
                typeof(CoreExtensionsScriptBindingProvider),
            };

            HashSet<Type> existingTypes = new HashSet<Type>();

            // Add custom providers for any other types being used from function.json
            foreach (var usedType in usedBindingTypes)
            {
                var type = ExtensionLoader.GetScriptBindingProvider(usedType);
                if (type != null && existingTypes.Add(type))
                {
                    bindingProviderTypes.Add(type);
                }
            }

            bindingProviderTypes.Add(typeof(GeneralScriptBindingProvider));
            bindingProviderTypes.Add(typeof(BuiltinExtensionBindingProvider));

            // Create the binding providers
            var bindingProviders = new Collection<ScriptBindingProvider>();
            foreach (var bindingProviderType in bindingProviderTypes)
            {
                try
                {
                    var provider = (ScriptBindingProvider)Activator.CreateInstance(bindingProviderType, new object[] { hostConfig, hostMetadata, traceWriter });
                    bindingProviders.Add(provider);
                }
                catch (Exception ex)
                {
                    // If we're unable to load create a binding provider for any reason, log
                    // the error and continue
                    string errorMsg = string.Format("Unable to create binding provider '{0}'", bindingProviderType.FullName);
                    traceWriter.Error(errorMsg, ex);
                    logger?.LogError(0, ex, errorMsg);
                }
            }

            return bindingProviders;
        }

        private static FunctionMetadata ParseFunctionMetadata(string functionName, JObject configMetadata, string scriptDirectory, ScriptSettingsManager settingsManager)
        {
            FunctionMetadata functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = scriptDirectory
            };

            JValue triggerDisabledValue = null;
            JArray bindingArray = (JArray)configMetadata["bindings"];
            if (bindingArray == null || bindingArray.Count == 0)
            {
                throw new FormatException("At least one binding must be declared.");
            }

            if (bindingArray != null)
            {
                foreach (JObject binding in bindingArray)
                {
                    BindingMetadata bindingMetadata = BindingMetadata.Create(binding);
                    functionMetadata.Bindings.Add(bindingMetadata);
                    if (bindingMetadata.IsTrigger)
                    {
                        triggerDisabledValue = (JValue)binding["disabled"];
                    }
                }
            }

            // A function can be disabled at the trigger or function level
            if (IsDisabled(triggerDisabledValue, settingsManager) ||
                IsDisabled((JValue)configMetadata["disabled"], settingsManager))
            {
                functionMetadata.IsDisabled = true;
            }

            JToken value = null;
            if (configMetadata.TryGetValue("excluded", StringComparison.OrdinalIgnoreCase, out value) &&
                value.Type == JTokenType.Boolean)
            {
                functionMetadata.IsExcluded = (bool)value;
            }

            JToken isDirect;
            if (configMetadata.TryGetValue("configurationSource", StringComparison.OrdinalIgnoreCase, out isDirect))
            {
                var isDirectValue = isDirect.ToString();
                if (string.Equals(isDirectValue, "attributes", StringComparison.OrdinalIgnoreCase))
                {
                    functionMetadata.IsDirect = true;
                }
                else if (!string.Equals(isDirectValue, "config", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException($"Illegal value '{isDirectValue}' for 'configurationSource' property in {functionMetadata.Name}'.");
                }
            }

            return functionMetadata;
        }

        public static Collection<FunctionMetadata> ReadFunctionMetadata(ScriptHostConfiguration config, TraceWriter traceWriter, ILogger logger, Dictionary<string, Collection<string>> functionErrors, ScriptSettingsManager settingsManager = null)
        {
            var functions = new Collection<FunctionMetadata>();
            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;

            if (config.Functions != null)
            {
                traceWriter.Info($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", config.Functions)}]");
            }

            foreach (var scriptDir in Directory.EnumerateDirectories(config.RootScriptPath))
            {
                string functionName = null;

                try
                {
                    // read the function config
                    string functionConfigPath = Path.Combine(scriptDir, ScriptConstants.FunctionMetadataFileName);
                    if (!File.Exists(functionConfigPath))
                    {
                        // not a function directory
                        continue;
                    }

                    functionName = Path.GetFileName(scriptDir);

                    if (config.Functions != null &&
                        !config.Functions.Contains(functionName, StringComparer.OrdinalIgnoreCase))
                    {
                        // a functions filter has been specified and the current function is
                        // not in the filter list
                        continue;
                    }

                    ValidateName(functionName);

                    string json = File.ReadAllText(functionConfigPath);
                    JObject functionConfig = JObject.Parse(json);

                    string functionError = null;
                    FunctionMetadata functionMetadata = null;
                    if (!TryParseFunctionMetadata(functionName, functionConfig, traceWriter, logger, scriptDir, settingsManager, out functionMetadata, out functionError))
                    {
                        // for functions in error, log the error and don't
                        // add to the functions collection
                        AddFunctionError(functionErrors, functionName, functionError);
                        continue;
                    }
                    else if (functionMetadata != null)
                    {
                        functions.Add(functionMetadata);
                    }
                }
                catch (Exception ex)
                {
                    // log any unhandled exceptions and continue
                    AddFunctionError(functionErrors, functionName, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
                }
            }

            return functions;
        }

        internal Collection<FunctionMetadata> ReadProxyMetadata(ScriptHostConfiguration config, ScriptSettingsManager settingsManager = null)
        {
            // read the proxy config
            string proxyConfigPath = Path.Combine(config.RootScriptPath, ScriptConstants.ProxyMetadataFileName);
            if (!File.Exists(proxyConfigPath))
            {
                return null;
            }

            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;

            var proxyAppSettingValue = settingsManager.GetSetting(EnvironmentSettingNames.ProxySiteExtensionEnabledKey);

            // This is for backward compatibility only, if the file is present but the value of proxy app setting(ROUTING_EXTENSION_VERSION) is explicitly set to 'disabled' we will ignore loading the proxies.
            if (!string.IsNullOrWhiteSpace(proxyAppSettingValue) && proxyAppSettingValue.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string proxiesJson = File.ReadAllText(proxyConfigPath);

            if (!string.IsNullOrWhiteSpace(proxiesJson))
            {
                return LoadProxyRoutes(proxiesJson);
            }

            return null;
        }

        private Collection<FunctionMetadata> LoadProxyRoutes(string proxiesJson)
        {
            var proxies = new Collection<FunctionMetadata>();

            if (_proxyClient == null)
            {
                var rawProxyClient = ProxyClientFactory.Create(proxiesJson, _startupLogger);
                if (rawProxyClient != null)
                {
                    _proxyClient = new ProxyClientExecutor(rawProxyClient);
                }
            }

            if (_proxyClient == null)
            {
                return proxies;
            }

            var routes = _proxyClient.GetProxyData();

            foreach (var route in routes.Routes)
            {
                try
                {
                    // Proxy names should follow the same naming restrictions as in function names. If not, invalid characters will be removed.
                    var proxyName = NormalizeProxyName(route.Name);

                    var proxyMetadata = new FunctionMetadata();

                    var json = new JObject
                    {
                        { "authLevel", "anonymous" },
                        { "name", "req" },
                        { "type", "httptrigger" },
                        { "direction", "in" },
                        { "Route", route.UrlTemplate.TrimStart('/') },
                        { "Methods",  new JArray(route.Methods.Select(m => m.Method.ToString()).ToArray()) }
                    };

                    BindingMetadata bindingMetadata = BindingMetadata.Create(json);

                    proxyMetadata.Bindings.Add(bindingMetadata);

                    proxyMetadata.Name = proxyName;
                    proxyMetadata.ScriptType = ScriptType.Unknown;
                    proxyMetadata.IsProxy = true;

                    proxies.Add(proxyMetadata);
                }
                catch (Exception ex)
                {
                    // log any unhandled exceptions and continue
                    AddFunctionError(FunctionErrors, route.Name, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
                }
            }

            return proxies;
        }

        internal static bool TryParseFunctionMetadata(string functionName, JObject functionConfig, TraceWriter traceWriter, ILogger logger, string scriptDirectory,
        ScriptSettingsManager settingsManager, out FunctionMetadata functionMetadata, out string error, IFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? new FileSystem();

            error = null;
            functionMetadata = ParseFunctionMetadata(functionName, functionConfig, scriptDirectory, settingsManager);

            if (functionMetadata.IsExcluded)
            {
                string message = $"Function '{functionName}' is marked as excluded";
                traceWriter.Info(message);
                logger?.LogInformation(message);
                functionMetadata = null;
                return true;
            }

            if (functionMetadata.IsDisabled)
            {
                string message = $"Function '{functionName}' is disabled";
                traceWriter.Info(message);
                logger?.LogInformation(message);
            }

            try
            {
                functionMetadata.ScriptFile = DeterminePrimaryScriptFile(functionConfig, scriptDirectory, fileSystem);
            }
            catch (ConfigurationErrorsException exc)
            {
                error = exc.Message;
                return false;
            }

            // determine the script type based on the primary script file extension
            functionMetadata.ScriptType = ParseScriptType(functionMetadata.ScriptFile);
            functionMetadata.EntryPoint = (string)functionConfig["entryPoint"];

            return true;
        }

        // A route is in conflict if the route matches any other existing
        // route and there is intersection in the http methods of the two functions
        internal static bool HttpRoutesConflict(HttpTriggerAttribute httpTrigger, HttpTriggerAttribute otherHttpTrigger)
        {
            if (string.Compare(httpTrigger.Route.Trim('/'), otherHttpTrigger.Route.Trim('/'), StringComparison.OrdinalIgnoreCase) != 0)
            {
                // routes differ, so no conflict
                return false;
            }

            if (httpTrigger.Methods == null || httpTrigger.Methods.Length == 0 ||
                otherHttpTrigger.Methods == null || otherHttpTrigger.Methods.Length == 0)
            {
                // if either methods collection is null or empty that means
                // "all methods", which will intersect with any method collection
                return true;
            }

            return httpTrigger.Methods.Intersect(otherHttpTrigger.Methods).Any();
        }

        internal static void ValidateName(string name, bool isProxy = false)
        {
            if (!FunctionNameValidationRegex.IsMatch(name))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid {1} name.", name, isProxy ? "proxy" : "function"));
            }
        }

        internal static string NormalizeProxyName(string name)
        {
            return ProxyNameValidationRegex.Replace(name, string.Empty);
        }

        /// <summary>
        /// Determines which script should be considered the "primary" entry point script.
        /// </summary>
        /// <exception cref="ConfigurationErrorsException">Thrown if the function metadata points to an invalid script file, or no script files are present.</exception>
        internal static string DeterminePrimaryScriptFile(JObject functionConfig, string scriptDirectory, IFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? new FileSystem();

            // First see if there is an explicit primary file indicated
            // in config. If so use that.
            string functionPrimary = null;
            string scriptFile = (string)functionConfig["scriptFile"];

            if (!string.IsNullOrEmpty(scriptFile))
            {
                string scriptPath = fileSystem.Path.Combine(scriptDirectory, scriptFile);
                if (!fileSystem.File.Exists(scriptPath))
                {
                    throw new ConfigurationErrorsException("Invalid script file name configuration. The 'scriptFile' property is set to a file that does not exist.");
                }

                functionPrimary = scriptPath;
            }
            else
            {
                string[] functionFiles = fileSystem.Directory.EnumerateFiles(scriptDirectory)
                    .Where(p => fileSystem.Path.GetFileName(p).ToLowerInvariant() != ScriptConstants.FunctionMetadataFileName)
                    .ToArray();

                if (functionFiles.Length == 0)
                {
                    throw new ConfigurationErrorsException("No function script files present.");
                }

                if (functionFiles.Length == 1)
                {
                    // if there is only a single file, that file is primary
                    functionPrimary = functionFiles[0];
                }
                else
                {
                    // if there is a "run" file, that file is primary,
                    // for Node, any index.js file is primary
                    functionPrimary = functionFiles.FirstOrDefault(p =>
                        fileSystem.Path.GetFileNameWithoutExtension(p).ToLowerInvariant() == "run" ||
                        fileSystem.Path.GetFileName(p).ToLowerInvariant() == "index.js");
                }
            }

            if (string.IsNullOrEmpty(functionPrimary))
            {
                throw new ConfigurationErrorsException("Unable to determine the primary function script. Try renaming your entry point script to 'run' (or 'index' in the case of Node), " +
                    "or alternatively you can specify the name of the entry point script explicitly by adding a 'scriptFile' property to your function metadata.");
            }

            return Path.GetFullPath(functionPrimary);
        }

        private static ScriptType ParseScriptType(string scriptFilePath)
        {
            string extension = Path.GetExtension(scriptFilePath).ToLowerInvariant().TrimStart('.');

            switch (extension)
            {
                case "csx":
                case "cs":
                    return ScriptType.CSharp;
                case "js":
                    return ScriptType.Javascript;
                case "ts":
                    return ScriptType.TypeScript;
                case "ps1":
                    return ScriptType.PowerShell;
                case "cmd":
                case "bat":
                    return ScriptType.WindowsBatch;
                case "py":
                    return ScriptType.Python;
                case "php":
                    return ScriptType.PHP;
                case "sh":
                    return ScriptType.Bash;
                case "fsx":
                    return ScriptType.FSharp;
                case "dll":
                    return ScriptType.DotNetAssembly;
                default:
                    return ScriptType.Unknown;
            }
        }

        private Collection<FunctionDescriptor> GetFunctionDescriptors(Collection<FunctionMetadata> functions)
        {
            var proxies = ReadProxyMetadata(ScriptConfig, _settingsManager);
            IEnumerable<FunctionMetadata> combinedFunctionMetadata = null;
            if (proxies != null && proxies.Any())
            {
                // Proxy routes will take precedence over http trigger functions and http trigger routes so they will be added first to the list of function descriptors.
                combinedFunctionMetadata = proxies.Concat(functions);
                _descriptorProviders.Add(new ProxyFunctionDescriptorProvider(this, ScriptConfig, _proxyClient));
            }
            else
            {
                combinedFunctionMetadata = functions;
            }

            return GetFunctionDescriptors(combinedFunctionMetadata, _descriptorProviders);
        }

        internal Collection<FunctionDescriptor> GetFunctionDescriptors(IEnumerable<FunctionMetadata> functions, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();
            var httpFunctions = new Dictionary<string, HttpTriggerAttribute>();

            foreach (FunctionMetadata metadata in functions)
            {
                try
                {
                    FunctionDescriptor descriptor = null;
                    foreach (var provider in descriptorProviders)
                    {
                        if (provider.TryCreate(metadata, out descriptor))
                        {
                            break;
                        }
                    }

                    ValidateFunction(descriptor, httpFunctions);

                    if (descriptor != null)
                    {
                        functionDescriptors.Add(descriptor);
                    }
                }
                catch (Exception ex)
                {
                    // log any unhandled exceptions and continue
                    AddFunctionError(FunctionErrors, metadata.Name, Utility.FlattenException(ex, includeSource: false));
                }
            }

            VerifyPrecompileStatus(functionDescriptors);

            return functionDescriptors;
        }

        internal static void ValidateFunction(FunctionDescriptor function, Dictionary<string, HttpTriggerAttribute> httpFunctions)
        {
            var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
            if (httpTrigger != null)
            {
                bool isProxy = function.Metadata != null && function.Metadata.IsProxy;

                ValidateHttpFunction(function.Name, httpTrigger, isProxy);

                if (!isProxy)
                {
                    // prevent duplicate/conflicting routes for functions
                    // proxy routes check is done in the proxy dll itself and proxies do not use routePrefix so should not check conflict with functions
                    foreach (var pair in httpFunctions)
                    {
                        if (HttpRoutesConflict(httpTrigger, pair.Value))
                        {
                            throw new InvalidOperationException($"The route specified conflicts with the route defined by function '{pair.Key}'.");
                        }
                    }
                }

                if (httpFunctions.ContainsKey(function.Name))
                {
                    throw new InvalidOperationException($"The function or proxy name '{function.Name}' must be unique within the function app.");
                }

                httpFunctions.Add(function.Name, httpTrigger);
            }
        }

        internal static void ValidateHttpFunction(string functionName, HttpTriggerAttribute httpTrigger, bool isProxy = false)
        {
            if (string.IsNullOrWhiteSpace(httpTrigger.Route) && !isProxy)
            {
                // if no explicit route is provided, default to the function name
                httpTrigger.Route = functionName;
            }

            // disallow custom routes in our own reserved route space
            string httpRoute = httpTrigger.Route.Trim('/').ToLowerInvariant();
            if (httpRoute.StartsWith("admin"))
            {
                throw new InvalidOperationException("The specified route conflicts with one or more built in routes.");
            }
        }

        internal static void ApplyConfiguration(JObject config, ScriptHostConfiguration scriptConfig)
        {
            var hostConfig = scriptConfig.HostConfig;

            hostConfig.HostConfigMetadata = config;

            JArray functions = (JArray)config["functions"];
            if (functions != null && functions.Count > 0)
            {
                scriptConfig.Functions = new Collection<string>();
                foreach (var function in functions)
                {
                    scriptConfig.Functions.Add((string)function);
                }
            }
            else
            {
                scriptConfig.Functions = null;
            }

            // We may already have a host id, but the one from the JSON takes precedence
            JToken hostId = (JToken)config["id"];
            if (hostId != null)
            {
                hostConfig.HostId = (string)hostId;
            }

            JToken fileWatchingEnabled = (JToken)config["fileWatchingEnabled"];
            if (fileWatchingEnabled != null && fileWatchingEnabled.Type == JTokenType.Boolean)
            {
                scriptConfig.FileWatchingEnabled = (bool)fileWatchingEnabled;
            }

            // Configure the set of watched directories, adding the standard built in
            // set to any the user may have specified
            if (scriptConfig.WatchDirectories == null)
            {
                scriptConfig.WatchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            scriptConfig.WatchDirectories.Add("node_modules");
            JToken watchDirectories = config["watchDirectories"];
            if (watchDirectories != null && watchDirectories.Type == JTokenType.Array)
            {
                foreach (JToken directory in watchDirectories.Where(p => p.Type == JTokenType.String))
                {
                    scriptConfig.WatchDirectories.Add((string)directory);
                }
            }

            // Apply Singleton configuration
            JObject configSection = (JObject)config["singleton"];
            JToken value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("lockPeriod", out value))
                {
                    hostConfig.Singleton.LockPeriod = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
                if (configSection.TryGetValue("listenerLockPeriod", out value))
                {
                    hostConfig.Singleton.ListenerLockPeriod = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
                if (configSection.TryGetValue("listenerLockRecoveryPollingInterval", out value))
                {
                    hostConfig.Singleton.ListenerLockRecoveryPollingInterval = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
                if (configSection.TryGetValue("lockAcquisitionTimeout", out value))
                {
                    hostConfig.Singleton.LockAcquisitionTimeout = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
                if (configSection.TryGetValue("lockAcquisitionPollingInterval", out value))
                {
                    hostConfig.Singleton.LockAcquisitionPollingInterval = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
            }

            // Apply Host Health Montitor configuration
            configSection = (JObject)config["healthMonitor"];
            value = null;
            if (configSection != null)
            {
                if (configSection.TryGetValue("enabled", out value) && value.Type == JTokenType.Boolean)
                {
                    scriptConfig.HostHealthMonitor.Enabled = (bool)value;
                }
                if (configSection.TryGetValue("healthCheckInterval", out value))
                {
                    scriptConfig.HostHealthMonitor.HealthCheckInterval = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
                if (configSection.TryGetValue("healthCheckWindow", out value))
                {
                    scriptConfig.HostHealthMonitor.HealthCheckWindow = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);
                }
                if (configSection.TryGetValue("healthCheckThreshold", out value))
                {
                    scriptConfig.HostHealthMonitor.HealthCheckThreshold = (int)value;
                }
                if (configSection.TryGetValue("counterThreshold", out value))
                {
                    scriptConfig.HostHealthMonitor.CounterThreshold = (float)value;
                }
            }

            // Apply Tracing/Logging configuration
            configSection = (JObject)config["tracing"];
            if (configSection != null)
            {
                if (configSection.TryGetValue("consoleLevel", out value))
                {
                    TraceLevel consoleLevel;
                    if (Enum.TryParse<TraceLevel>((string)value, true, out consoleLevel))
                    {
                        hostConfig.Tracing.ConsoleLevel = consoleLevel;
                    }
                }

                if (configSection.TryGetValue("fileLoggingMode", out value))
                {
                    FileLoggingMode fileLoggingMode;
                    if (Enum.TryParse<FileLoggingMode>((string)value, true, out fileLoggingMode))
                    {
                        scriptConfig.FileLoggingMode = fileLoggingMode;
                    }
                }
            }

            if (config.TryGetValue("functionTimeout", out value))
            {
                TimeSpan requestedTimeout = TimeSpan.Parse((string)value, CultureInfo.InvariantCulture);

                // Only apply limits if this is Dynamic.
                if (ScriptSettingsManager.Instance.IsDynamicSku && (requestedTimeout < MinFunctionTimeout || requestedTimeout > MaxFunctionTimeout))
                {
                    string message = $"{nameof(scriptConfig.FunctionTimeout)} must be between {MinFunctionTimeout} and {MaxFunctionTimeout}.";
                    throw new ArgumentException(message);
                }

                scriptConfig.FunctionTimeout = requestedTimeout;
            }
            else if (ScriptSettingsManager.Instance.IsDynamicSku)
            {
                // Apply a default if this is running on Dynamic.
                scriptConfig.FunctionTimeout = DefaultFunctionTimeout;
            }

            // apply swagger configuration
            scriptConfig.SwaggerEnabled = false;

            configSection = (JObject)config["swagger"];
            JToken swaggerEnabled;

            if (configSection != null &&
                configSection.TryGetValue("enabled", out swaggerEnabled) &&
                swaggerEnabled.Type == JTokenType.Boolean)
            {
                scriptConfig.SwaggerEnabled = (bool)swaggerEnabled;
            }

            ApplyLoggerConfig(config, scriptConfig);
            ApplyApplicationInsightsConfig(config, scriptConfig);
        }

        internal static void ApplyLoggerConfig(JObject configJson, ScriptHostConfiguration scriptConfig)
        {
            scriptConfig.LogFilter = new LogCategoryFilter();
            JObject configSection = (JObject)configJson["logger"];
            JToken value;
            if (configSection != null)
            {
                JObject filterSection = (JObject)configSection["categoryFilter"];
                if (filterSection != null)
                {
                    if (filterSection.TryGetValue("defaultLevel", out value))
                    {
                        LogLevel level;
                        if (Enum.TryParse(value.ToString(), out level))
                        {
                            scriptConfig.LogFilter.DefaultLevel = level;
                        }
                    }

                    if (filterSection.TryGetValue("categoryLevels", out value))
                    {
                        scriptConfig.LogFilter.CategoryLevels.Clear();
                        foreach (var prop in ((JObject)value).Properties())
                        {
                            LogLevel level;
                            if (Enum.TryParse(prop.Value.ToString(), out level))
                            {
                                scriptConfig.LogFilter.CategoryLevels[prop.Name] = level;
                            }
                        }
                    }
                }

                JObject aggregatorSection = (JObject)configSection["aggregator"];
                if (aggregatorSection != null)
                {
                    if (aggregatorSection.TryGetValue("batchSize", out value))
                    {
                        scriptConfig.HostConfig.Aggregator.BatchSize = (int)value;
                    }

                    if (aggregatorSection.TryGetValue("flushTimeout", out value))
                    {
                        scriptConfig.HostConfig.Aggregator.FlushTimeout = TimeSpan.Parse(value.ToString());
                    }
                }
            }
        }

        internal static void ApplyApplicationInsightsConfig(JObject configJson, ScriptHostConfiguration scriptConfig)
        {
            scriptConfig.ApplicationInsightsSamplingSettings = new SamplingPercentageEstimatorSettings();
            JObject configSection = (JObject)configJson["applicationInsights"];
            JToken value;
            if (configSection != null)
            {
                JObject samplingSection = (JObject)configSection["sampling"];
                if (samplingSection != null)
                {
                    if (samplingSection.TryGetValue("isEnabled", out value))
                    {
                        bool isEnabled;
                        if (bool.TryParse(value.ToString(), out isEnabled) && !isEnabled)
                        {
                            scriptConfig.ApplicationInsightsSamplingSettings = null;
                        }
                    }

                    if (scriptConfig.ApplicationInsightsSamplingSettings != null)
                    {
                        if (samplingSection.TryGetValue("maxTelemetryItemsPerSecond", out value))
                        {
                            double itemsPerSecond;
                            if (double.TryParse(value.ToString(), out itemsPerSecond))
                            {
                                scriptConfig.ApplicationInsightsSamplingSettings.MaxTelemetryItemsPerSecond = itemsPerSecond;
                            }
                        }
                    }
                }
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            HandleHostError((Exception)e.ExceptionObject);
        }

        private void HandleHostError(Microsoft.Azure.WebJobs.Extensions.TraceFilter traceFilter)
        {
            var events = traceFilter.GetEvents().Where(p => p != null).ToArray();

            foreach (TraceEvent traceEvent in events)
            {
                var exception = traceEvent.Exception ?? new InvalidOperationException(traceEvent.Message);
                HandleHostError(exception);
            }
        }

        private void HandleHostError(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

            // First, ensure that we've logged to the host log
            // Also ensure we flush immediately to ensure any buffered logs
            // are written
            string message = "A ScriptHost error has occurred";
            TraceWriter.Error(message, exception);
            TraceWriter.Flush();

            // Note: We do not log to ILogger here as any error has already been logged.

            if (exception is FunctionInvocationException)
            {
                // For all function invocation errors, we notify the invoker so it can
                // log the error as needed to its function specific logs.
                FunctionInvocationException invocationException = exception as FunctionInvocationException;
                NotifyInvoker(invocationException.MethodName, invocationException);
            }
            else if (exception is FunctionIndexingException || exception is FunctionListenerException)
            {
                // For all startup time indexing/listener errors, we accumulate them per function
                FunctionException functionException = exception as FunctionException;
                string formattedError = Utility.FlattenException(functionException);
                AddFunctionError(FunctionErrors, functionException.MethodName, formattedError);

                // Also notify the invoker so the error can also be written to the function
                // log file
                NotifyInvoker(functionException.MethodName, functionException);

                // Mark the error as handled so execution will continue with this function disabled
                functionException.Handled = true;
            }
            else
            {
                // See if we can identify which function caused the error, and if we can
                // log the error as needed to its function specific logs.
                FunctionDescriptor function = null;
                if (TryGetFunctionFromException(Functions, exception, out function))
                {
                    NotifyInvoker(function.Name, exception);
                }
            }
        }

        internal static bool TryGetFunctionFromException(Collection<FunctionDescriptor> functions, Exception exception, out FunctionDescriptor function)
        {
            function = null;

            string errorStack = exception.ToString().ToLowerInvariant();
            foreach (var currFunction in functions)
            {
                // For each function, we search the entire error stack trace to see if it contains
                // the function entry/primary script path. If it does, we're virtually certain that
                // that function caused the error (e.g. as in the case of global unhandled exceptions
                // coming from Node.js scripts).
                // We use the directory name for the script rather than the full script path itself to ensure
                // that we handle cases where the error might be coming from some other script (e.g. an NPM
                // module) that is part of the function.
                string absoluteScriptPath = Path.GetFullPath(currFunction.Metadata.ScriptFile).ToLowerInvariant();
                string functionDirectory = Path.GetDirectoryName(absoluteScriptPath);
                if (errorStack.Contains(functionDirectory))
                {
                    function = currFunction;
                    return true;
                }
            }

            return false;
        }

        private void NotifyInvoker(string functionName, Exception ex)
        {
            functionName = Utility.GetFunctionShortName(functionName);

            FunctionDescriptor functionDescriptor = this.Functions.SingleOrDefault(p => string.Compare(functionName, p.Name, StringComparison.OrdinalIgnoreCase) == 0);
            if (functionDescriptor != null)
            {
                functionDescriptor.Invoker.OnError(ex);
            }
        }

        private void OnFileChanged(FileSystemEventArgs e)
        {
            // We will perform a host restart in the following cases:
            // - the file change was under one of the configured watched directories (e.g. node_modules, shared code directories, etc.)
            // - the host.json file was changed
            // - a function.json file was changed
            // - a proxies.json file was changed
            // - a function directory was added/removed/renamed
            // A full host shutdown is performed when an assembly (.dll, .exe) in a watched directory is modified

            string changeDescription = string.Empty;
            string directory = GetRelativeDirectory(e.FullPath, ScriptConfig.RootScriptPath);
            string fileName = Path.GetFileName(e.Name);

            if (ScriptConfig.WatchDirectories.Contains(directory))
            {
                changeDescription = "Watched directory";
            }
            else if (string.Compare(fileName, ScriptConstants.HostMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(fileName, ScriptConstants.FunctionMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0 ||
                string.Compare(fileName, ScriptConstants.ProxyMetadataFileName, StringComparison.OrdinalIgnoreCase) == 0)
            {
                changeDescription = "File";
            }
            else if ((e.ChangeType == WatcherChangeTypes.Deleted || Directory.Exists(e.FullPath))
                && !_directorySnapshot.SequenceEqual(Directory.EnumerateDirectories(ScriptConfig.RootScriptPath)))
            {
                // Check directory spashot only if "Deleted" change or if directory changed
                changeDescription = "Directory";
            }

            if (!string.IsNullOrEmpty(changeDescription))
            {
                bool shutdown = false;
                string fileExtension = Path.GetExtension(fileName);
                if (!string.IsNullOrEmpty(fileExtension) && ScriptConstants.AssemblyFileTypes.Contains(fileExtension, StringComparer.OrdinalIgnoreCase))
                {
                    shutdown = true;
                }

                TraceFileChangeRestart(changeDescription, e.ChangeType.ToString(), e.FullPath, shutdown);
                ScheduleRestartAsync(shutdown).ContinueWith(t => TraceWriter.Error($"Error restarting host (full shutdown: {shutdown})", t.Exception),
                    TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private async Task ScheduleRestartAsync(bool shutdown)
        {
            if (shutdown)
            {
                _shutdownScheduled = true;
                _shutdown();
            }
            else
            {
                await _restart();
            }
        }

        internal static string GetRelativeDirectory(string path, string scriptRoot)
        {
            if (path.StartsWith(scriptRoot))
            {
                string directory = path.Substring(scriptRoot.Length).TrimStart(Path.DirectorySeparatorChar);
                int idx = directory.IndexOf(Path.DirectorySeparatorChar);
                if (idx != -1)
                {
                    directory = directory.Substring(0, idx);
                }

                return directory;
            }

            return string.Empty;
        }

        private static bool IsDisabled(JToken isDisabledValue, ScriptSettingsManager settingsManager)
        {
            if (isDisabledValue != null)
            {
                if (isDisabledValue.Type == JTokenType.Boolean)
                {
                    return (bool)isDisabledValue;
                }
                else
                {
                    string settingName = (string)isDisabledValue;
                    string value = settingsManager.GetSetting(settingName);
                    if (!string.IsNullOrEmpty(value) &&
                        (string.Compare(value, "1", StringComparison.OrdinalIgnoreCase) == 0 ||
                         string.Compare(value, "true", StringComparison.OrdinalIgnoreCase) == 0))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? "Unknown";
        }

        protected override void OnHostInitialized()
        {
            HostInitialized?.Invoke(this, EventArgs.Empty);

            base.OnHostInitialized();
        }

        protected override void OnHostStarted()
        {
            HostStarted?.Invoke(this, EventArgs.Empty);

            base.OnHostStarted();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (TraceWriter as IDisposable)?.Dispose();
#if FEATURE_NODE
                NodeFunctionInvoker.UnhandledException -= OnUnhandledException;
#endif
                _fileEventsSubscription?.Dispose();
                _fileEventSource?.Dispose();

                if (_debugModeFileWatcher != null)
                {
                    _debugModeFileWatcher.Changed -= OnDebugModeFileChanged;
                    _debugModeFileWatcher.Dispose();
                }

                DisposePrimaryHostCoordinator();

                foreach (var function in Functions)
                {
                    (function.Invoker as IDisposable)?.Dispose();
                }

                if (_traceMonitor != null)
                {
                    _hostConfig.Tracing.Tracers.Remove(_traceMonitor);
                }

                if (_descriptorProviders != null)
                {
                    foreach (var provider in _descriptorProviders)
                    {
                        (provider as IDisposable)?.Dispose();
                    }
                }
            }

            // dispose base here to ensure that errors there don't
            // cause us to not dispose ourselves
            base.Dispose(disposing);

            if (disposing)
            {
                // The LoggerFactory can be used by the host up until it's disposed,
                // so make sure that it's disposed last.
                _loggerFactory?.Dispose();
            }
        }

        public void DisposePrimaryHostCoordinator()
        {
            if (_blobLeaseManager != null)
            {
                _blobLeaseManager.HasLeaseChanged -= BlobLeaseManagerHasLeaseChanged;
                _blobLeaseManager.Dispose();
            }
        }
    }
}
