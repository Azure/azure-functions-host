﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
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
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel.Implementation;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHost : JobHost, IScriptJobHost
    {
        internal const int DebugModeTimeoutMinutes = 15;
        private const string HostAssemblyName = "ScriptHost";
        private const string GeneratedTypeNamespace = "Host";
        internal const string GeneratedTypeName = "Functions";
        private readonly IScriptHostEnvironment _scriptHostEnvironment;
        private readonly ILoggerProviderFactory _loggerProviderFactory;
        private readonly string _storageConnectionString;
        private readonly IDistributedLockManager _distributedLockManager;
        private readonly IMetricsLogger _metricsLogger = null;
        private readonly string _hostLogPath;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly string _language;
        private readonly IOptions<JobHostOptions> _hostOptions;

        private string _instanceId;
        private Func<Task> _restart;
        private Action _shutdown;
        private AutoRecoveringFileSystemWatcher _debugModeFileWatcher;
        private ImmutableArray<string> _directorySnapshot;
        // TODO: DI (FACAVAL) Review
        private PrimaryHostCoordinator _primaryHostCoordinator = null;
        internal static readonly TimeSpan MinFunctionTimeout = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan DefaultFunctionTimeout = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan MaxFunctionTimeout = TimeSpan.FromMinutes(10);
        private static readonly Regex FunctionNameValidationRegex = new Regex(@"^[a-z][a-z0-9_\-]{0,127}$(?<!^host$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ProxyNameValidationRegex = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly string Version = GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
        internal static readonly int DefaultMaxMessageLengthBytesDynamicSku = 32 * 1024 * 1024;
        internal static readonly int DefaultMaxMessageLengthBytes = 128 * 1024 * 1024;
        private ScriptSettingsManager _settingsManager;
        private bool _shutdownScheduled;
        // TODO: DI (FACAVAL) Review
        private ILogger _startupLogger = null;
        private FileWatcherEventSource _fileEventSource;
        private IList<IDisposable> _eventSubscriptions = new List<IDisposable>();
        private ProxyClientExecutor _proxyClient;
        private IFunctionRegistry _functionDispatcher;
        // TODO: DI (FACAVAL) Review
        private ILoggerFactory _loggerFactory = null;
        private List<FunctionDescriptorProvider> _descriptorProviders;
        private IProcessRegistry _processRegistry = new EmptyProcessRegistry();

        // Specify the "builtin binding types". These are types that are directly accesible without needing an explicit load gesture.
        // This is the set of bindings we shipped prior to binding extensibility.
        // Map from BindingType to the Assembly Qualified Type name for its IExtensionConfigProvider object.

        public ScriptHost(IScriptHostEnvironment environment,
            IOptions<JobHostOptions> options,
            IJobHostContextFactory jobHostContextFactory,
            IConnectionStringProvider connectionStringProvider,
            IDistributedLockManager distributedLockManager,
            IScriptEventManager eventManager,
            ILoggerFactory loggerFactory,
            IOptions<ScriptHostOptions> scriptHostOptions = null,
            ScriptSettingsManager settingsManager = null,
            ILoggerProviderFactory loggerProviderFactory = null,
            ProxyClientExecutor proxyClient = null)
            : base(options, jobHostContextFactory)
        {
            _instanceId = Guid.NewGuid().ToString();
            _hostOptions = options;
            _storageConnectionString = connectionStringProvider.GetConnectionString(ConnectionStringNames.Storage);
            _distributedLockManager = distributedLockManager;

            // TODO: DI (FACAVAL) Move this to config setup
            //if (!Path.IsPathRooted(scriptConfig.RootScriptPath))
            //{
            //    scriptConfig.RootScriptPath = Path.Combine(Environment.CurrentDirectory, scriptConfig.RootScriptPath);
            //}

            ScriptConfig = scriptHostOptions.Value;
            _scriptHostEnvironment = environment;
            FunctionErrors = new Dictionary<string, Collection<string>>(StringComparer.OrdinalIgnoreCase);

            EventManager = eventManager;

            _settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            _proxyClient = proxyClient;

            // TODO: DI (FACAVAL) See comment on method
            //_metricsLogger = CreateMetricsLogger();

            _hostLogPath = Path.Combine(ScriptConfig.RootLogPath, "Host");

            _language = _settingsManager.Configuration[LanguageWorkerConstants.FunctionWorkerRuntimeSettingName];

            _loggerProviderFactory = loggerProviderFactory ?? new DefaultLoggerProviderFactory();
            _loggerFactory = loggerFactory;
            _startupLogger = loggerFactory.CreateLogger(LogCategories.Startup);
        }

        // TODO: DI (FACAVAL) Do we still need this event?
        //public event EventHandler HostInitializing;

        public event EventHandler HostInitialized;

        public event EventHandler HostStarted;

        public event EventHandler IsPrimaryChanged;

        public string InstanceId
        {
            get
            {
                if (_instanceId == null)
                {
                    _instanceId = Guid.NewGuid().ToString();
                }

                return _instanceId;
            }
        }

        public IScriptEventManager EventManager { get; }

        public ILogger Logger { get; internal set; }

        public ScriptHostOptions ScriptConfig { get; private set; }

        /// <summary>
        /// Gets the collection of all valid Functions. For functions that are in error
        /// and were unable to load successfully, consult the <see cref="FunctionErrors"/> collection.
        /// </summary>
        public virtual ICollection<FunctionDescriptor> Functions { get; private set; } = new Collection<FunctionDescriptor>();

        // Maps from FunctionName to a set of errors for that function.
        public virtual Dictionary<string, Collection<string>> FunctionErrors { get; private set; }

        public virtual bool IsPrimary
        {
            get
            {
                return _primaryHostCoordinator?.HasLease ?? false;
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
                Logger.LogError(0, ex, message);

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
        /// Performs all required initialization on the host.
        /// Must be called before the host is started.
        /// </summary>
        public void Initialize()
        {
            _stopwatch.Start();
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupLatency))
            {
                PreInitialize();
                InitializeFileWatchers();
                InitializeWorkers();

                var functionMetadata = LoadFunctionMetadata();
                // TODO: DI (FACAVAL) Pass configuration
                var directTypes = LoadBindingExtensions(functionMetadata, new JObject());
                InitializeFunctionDescriptors(functionMetadata);
                GenerateFunctions(directTypes);

                InitializeServices();
                CleanupFileSystem();
            }
        }

        // TODO: DI (FACAVAL) Logger configuration is done on startup
        //private void ConfigureLoggerFactory(bool recreate = false)
        //{
        //    // Ensure we always have an ILoggerFactory,
        //    // regardless of whether AppInsights is registered or not
        //    if (recreate || _hostOptions.LoggerFactory == null)
        //    {
        //        _hostOptions.LoggerFactory = new LoggerFactory(Enumerable.Empty<ILoggerProvider>(), Utility.CreateLoggerFilterOptions());

        //        // If we've created the LoggerFactory, then we are responsible for
        //        // disposing. Store this locally for disposal later. We can't rely
        //        // on accessing this directly from ScriptConfig.HostConfig as the
        //        // ScriptConfig is re-used for every host.
        //        _loggerFactory = _hostOptions.LoggerFactory;
        //    }

        //    ConfigureLoggerFactory(_instanceId, _hostOptions.LoggerFactory, ScriptConfig, _settingsManager, _loggerProviderFactory,
        //        () => FileLoggingEnabled, () => IsPrimary, HandleHostError);
        //}

        internal static void ConfigureLoggerFactory(string instanceId, ILoggerFactory loggerFactory, ScriptHostOptions scriptConfig, ScriptSettingsManager settingsManager,
            ILoggerProviderFactory builder, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary, Action<Exception> handleException)
        {
            // TODO: DI (FACAVAL) Review - BrettSam
            //foreach (ILoggerProvider provider in builder.CreateLoggerProviders(instanceId, scriptConfig, settingsManager, isFileLoggingEnabled, isPrimary))
            //{
            //    loggerFactory.AddProvider(provider);
            //}

            // The LoggerFactory must always have this as there's some functional value (handling exceptions) when handling these errors.
            loggerFactory.AddProvider(new HostErrorLoggerProvider(handleException));
        }

        private void TraceFileChangeRestart(string changeDescription, string changeType, string path, bool isShutdown)
        {
            string fileChangeMsg = string.Format(CultureInfo.InvariantCulture, "{0} change of type '{1}' detected for '{2}'", changeDescription, changeType, path);
            Logger.LogInformation(fileChangeMsg);

            string action = isShutdown ? "shutdown" : "restart";
            string signalMessage = $"Host configuration has changed. Signaling {action}";
            Logger.LogInformation(signalMessage);
        }

        // TODO: DI (FACAVAL) This needs to move to an options config setup
        // Create a TimeoutConfiguration specified by scriptConfig knobs; else null.
        internal static JobHostFunctionTimeoutOptions CreateTimeoutConfiguration(ScriptHostOptions scriptConfig)
        {
            if (scriptConfig.FunctionTimeout == null)
            {
                return null;
            }
            return new JobHostFunctionTimeoutOptions
            {
                Timeout = scriptConfig.FunctionTimeout.Value,
                ThrowOnTimeout = true,
                TimeoutWhileDebugging = true
            };
        }

        internal static Collection<CustomAttributeBuilder> CreateTypeAttributes(ScriptHostOptions scriptConfig)
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

        internal Task RestartAsync()
        {
            if (!_shutdownScheduled)
            {
                _scriptHostEnvironment.RestartHost();
            }

            return Task.CompletedTask;
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

        private void OnHostLeaseChanged(object sender, EventArgs e)
        {
            IsPrimaryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Perform any early initialization operations.
        /// </summary>
        private void PreInitialize()
        {
            InitializeFileSystem();

            string debugSentinelFileName = Path.Combine(_hostLogPath, ScriptConstants.DebugSentinelFileName);
            LastDebugNotify = File.Exists(debugSentinelFileName)
                ? File.GetLastWriteTimeUtc(debugSentinelFileName)
                : DateTime.MinValue;

            // take a startup time function directory snapshot so we can detect function additions/removals
            // we'll also use this snapshot when reading function metadata as part of startup
            // taking this snapshot once and reusing at various points during initialization allows us to
            // minimize disk operations
            _directorySnapshot = Directory.EnumerateDirectories(ScriptConfig.RootScriptPath).ToImmutableArray();
        }

        /// <summary>
        /// Set up any required directories or files.
        /// </summary>
        private void InitializeFileSystem()
        {
            FileUtility.EnsureDirectoryExists(_hostLogPath);

            if (!_settingsManager.FileSystemIsReadOnly)
            {
                FileUtility.EnsureDirectoryExists(ScriptConfig.RootScriptPath);
            }
        }

        /// <summary>
        /// Generate function wrappers from descriptors.
        /// </summary>
        private void GenerateFunctions(IEnumerable<Type> directTypes)
        {
            // generate Type level attributes
            var typeAttributes = CreateTypeAttributes(ScriptConfig);

            string generatingMsg = string.Format(CultureInfo.InvariantCulture, "Generating {0} job function(s)", Functions.Count);
            _startupLogger?.LogInformation(generatingMsg);

            // generate the Type wrapper
            string typeName = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GeneratedTypeNamespace, GeneratedTypeName);
            Type functionWrapperType = FunctionGenerator.Generate(HostAssemblyName, typeName, typeAttributes, Functions);

            // configure the Type locator
            var types = new List<Type>();
            types.Add(functionWrapperType);
            types.AddRange(directTypes);

            // TODO: DI (FACAVAL) Use a custom ITypeLocator implementation that supports the type registration
            // _hostOptions.TypeLocator = new TypeLocator(types);
        }

        /// <summary>
        /// Initialize function descriptors from metadata.
        /// </summary>
        internal void InitializeFunctionDescriptors(Collection<FunctionMetadata> functionMetadata)
        {
            _descriptorProviders = new List<FunctionDescriptorProvider>();
            if (string.IsNullOrEmpty(_language))
            {
                _startupLogger.LogTrace("Adding Function descriptor providers for all languages.");
                _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptConfig, _loggerFactory));
                _descriptorProviders.Add(new WorkerFunctionDescriptorProvider(this, ScriptConfig, _functionDispatcher, _loggerFactory));
            }
            else
            {
                _startupLogger.LogTrace($"Adding Function descriptor provider for language {_language}.");
                if (string.Equals(_language, LanguageWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
                {
                    _descriptorProviders.Add(new DotNetFunctionDescriptorProvider(this, ScriptConfig, _loggerFactory));
                }
                else
                {
                    _descriptorProviders.Add(new WorkerFunctionDescriptorProvider(this, ScriptConfig, _functionDispatcher, _loggerFactory));
                }
            }

            Collection<FunctionDescriptor> functions;
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupGetFunctionDescriptorsLatency))
            {
                functions = GetFunctionDescriptors(functionMetadata);
                _startupLogger.LogTrace("Function descriptors created.");
            }

            Functions = functions;
        }

        /// <summary>
        /// Create and initialize host services.
        /// </summary>
        private void InitializeServices()
        {
            InitializeHostCoordinator();
        }

        private void InitializeHostCoordinator()
        {
            // TODO: DI (FACAVAL) Remove this once validated. Injection no longer relies on this ordering.
            // this must be done ONLY after we've loaded any custom extensions.
            // that gives an extension an opportunity to plug in their own implementations.
            //if (_storageConnectionString != null)
            //{
            //    var lockManager = (IDistributedLockManager)Services.GetService(typeof(IDistributedLockManager));
            //    _primaryHostCoordinator = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), _hostOptions.HostId, _settingsManager.InstanceId, _hostOptions.LoggerFactory);
            //}

            // TODO: DI (FACAVAL) Move to constructor injection, wire up handler.
            // Create the lease manager that will keep handle the primary host blob lease acquisition and renewal
            // and subscribe for change notifications.
            if (_primaryHostCoordinator != null)
            {
                _primaryHostCoordinator.HasLeaseChanged += OnHostLeaseChanged;
            }
        }

        /// <summary>
        /// Read all functions and populate function metadata.
        /// </summary>
        private Collection<FunctionMetadata> LoadFunctionMetadata()
        {
            Collection<FunctionMetadata> functionMetadata;
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupReadFunctionMetadataLatency))
            {
                functionMetadata = ReadFunctionsMetadata(_directorySnapshot, _startupLogger, FunctionErrors, _settingsManager, ScriptConfig.Functions);
                _startupLogger.LogTrace("Function metadata read.");
            }

            return functionMetadata;
        }

        /// <summary>
        /// Initialize file and directory change monitoring.
        /// </summary>
        private void InitializeFileWatchers()
        {
            _debugModeFileWatcher = new AutoRecoveringFileSystemWatcher(_hostLogPath, ScriptConstants.DebugSentinelFileName,
                   includeSubdirectories: false, changeTypes: WatcherChangeTypes.Created | WatcherChangeTypes.Changed);

            _debugModeFileWatcher.Changed += OnDebugModeFileChanged;
            _startupLogger.LogTrace("Debug file watch initialized.");

            if (ScriptConfig.FileWatchingEnabled)
            {
                _fileEventSource = new FileWatcherEventSource(EventManager, EventSources.ScriptFiles, ScriptConfig.RootScriptPath);

                _eventSubscriptions.Add(EventManager.OfType<FileEvent>()
                        .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                        .Subscribe(e => OnFileChanged(e.FileChangeArguments)));

                _startupLogger.LogTrace("File event source initialized.");
            }

            _eventSubscriptions.Add(EventManager.OfType<HostRestartEvent>()
                    .Subscribe((msg) => ScheduleRestartAsync(false)
                    .ContinueWith(t => _startupLogger.LogCritical(t.Exception.Message),
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)));

            // If a file change should result in a restart, we debounce the event to
            // ensure that only a single restart is triggered within a specific time window.
            // This allows us to deal with a large set of file change events that might
            // result from a bulk copy/unzip operation. In such cases, we only want to
            // restart after ALL the operations are complete and there is a quiet period.
            _restart = RestartAsync;
            _restart = _restart.Debounce(500);

            _shutdown = Shutdown;
            _shutdown = _shutdown.Debounce(500);
        }

        private void InitializeWorkers()
        {
            var serverImpl = new FunctionRpcService(EventManager);
            var server = new GrpcServer(serverImpl, ScriptConfig.MaxMessageLengthBytes);

            // TODO: async initialization of script host - hook into startasync method?
            server.StartAsync().GetAwaiter().GetResult();
            var processFactory = new DefaultWorkerProcessFactory();

            try
            {
                _processRegistry = ProcessRegistryFactory.Create();
            }
            catch (Exception e)
            {
                _startupLogger.LogWarning(e, "Unable to create process registry");
            }

            CreateChannel channelFactory = (languageWorkerConfig, registrations) =>
            {
                return new LanguageWorkerChannel(
                    ScriptConfig,
                    EventManager,
                    processFactory,
                    _processRegistry,
                    registrations,
                    languageWorkerConfig,
                    server.Uri,
                    NullLoggerFactory.Instance); // TODO: DI (FACAVAL) Pass appropriate logger. Channel facory should likely be a service.
            };

            var configFactory = new WorkerConfigFactory(ScriptSettingsManager.Instance.Configuration, _startupLogger);
            var providers = new List<IWorkerProvider>();
            if (!string.IsNullOrEmpty(_language))
            {
                if (!string.Equals(_language, LanguageWorkerConstants.DotNetLanguageWorkerName, StringComparison.OrdinalIgnoreCase))
                {
                    _startupLogger.LogInformation($"'{LanguageWorkerConstants.FunctionWorkerRuntimeSettingName}' is specified, only '{_language}' will be enabled");
                    providers.AddRange(configFactory.GetWorkerProviders(_startupLogger, language: _language));
                }
            }
            else
            {
                // load all providers if no specific language is specified
                providers.AddRange(configFactory.GetWorkerProviders(_startupLogger));
            }

            var workerConfigs = configFactory.GetConfigs(providers);

            _functionDispatcher = new FunctionRegistry(EventManager, server, channelFactory, workerConfigs);

            _eventSubscriptions.Add(EventManager.OfType<WorkerErrorEvent>()
                .Subscribe(evt =>
                {
                    HandleHostError(evt.Exception);
                }));
        }

        /// <summary>
        /// Clean up any old files or directories.
        /// </summary>
        private void CleanupFileSystem()
        {
            if (ScriptConfig.FileLoggingMode != FileLoggingMode.Never)
            {
                // initiate the cleanup in a background task so we don't
                // delay startup
                Task.Run(() => PurgeOldLogDirectories());
            }
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
                var functionLookup = Directory.EnumerateDirectories(ScriptConfig.RootScriptPath).ToLookup(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

                string rootLogFilePath = Path.Combine(ScriptConfig.RootLogPath, "Function");
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
                            _startupLogger.LogDebug(removeLogMessage);
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
                _startupLogger.LogError(0, ex, errorMsg);
            }
        }

        private IEnumerable<Type> LoadBindingExtensions(IEnumerable<FunctionMetadata> functionMetadata, JObject hostConfigObject)
        {
            // TODO: DI (FACAVAL) Inject this thing.... :S
            //Func<string, FunctionDescriptor> funcLookup = (name) => GetFunctionOrNull(name);
            //_hostOptions.AddService(funcLookup);
            // TODO: DI (FACAVAL) Inject this (if needed, ideally, remove), remove the instantiation
            var extensionLoader = new ExtensionLoader(ScriptConfig, null, _startupLogger);
            var usedBindingTypes = extensionLoader.DiscoverBindingTypes(functionMetadata);

            var bindingProviders = LoadBindingProviders(ScriptConfig, hostConfigObject, _startupLogger, usedBindingTypes);
            ScriptConfig.BindingProviders = bindingProviders;
            _startupLogger.LogTrace("Binding providers loaded.");

            var coreExtensionsBindingProvider = bindingProviders.OfType<CoreExtensionsScriptBindingProvider>().First();
            coreExtensionsBindingProvider.AppDirectory = ScriptConfig.RootScriptPath;

            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupInitializeBindingProvidersLatency))
            {
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
                        _startupLogger?.LogError(0, ex, errorMsg);
                    }
                }
                _startupLogger.LogTrace("Binding providers initialized.");
            }

            var directTypes = GetDirectTypes(functionMetadata);
            extensionLoader.LoadDirectlyReferencedExtensions(directTypes);
            extensionLoader.LoadCustomExtensions();
            _startupLogger.LogTrace("Extension loading complete.");

            // Now all extensions have been loaded, the metadata is finalized.
            // There's a single script binding instance that services all extensions.
            // give that script binding the metadata for all loaded extensions so it can dispatch to them.
            using (_metricsLogger.LatencyEvent(MetricEventNames.HostStartupCreateMetadataProviderLatency))
            {
                var generalProvider = ScriptConfig.BindingProviders.OfType<GeneralScriptBindingProvider>().First();
                var metadataProvider = this.CreateMetadataProvider();
                generalProvider.CompleteInitialization(metadataProvider);
                _startupLogger.LogTrace("Metadata provider created.");
            }

            return directTypes;
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

                    _startupLogger.LogInformation(msg);
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

                Assembly assembly = FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(path);
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    visitedTypes.Add(type);
                }
                else
                {
                    // This likely means the function.json and dlls are out of sync. Perhaps a badly generated function.json?
                    string msg = $"Failed to load type '{typeName}' from '{path}'";
                    _startupLogger.LogWarning(msg);
                }
            }
            return visitedTypes;
        }

        // TODO: DI (FACAVAL) All of this just gets replaced with a metrics logger registration
        //private IMetricsLogger CreateMetricsLogger()
        //{
        //    IMetricsLogger metricsLogger = ScriptConfig.HostOptions.GetService<IMetricsLogger>();
        //    if (metricsLogger == null)
        //    {
        //        metricsLogger = new MetricsLogger();
        //        ScriptConfig.HostOptions.AddService<IMetricsLogger>(metricsLogger);
        //    }
        //    return metricsLogger;
        //}

        private static Collection<ScriptBindingProvider> LoadBindingProviders(ScriptHostOptions options, JObject hostMetadata, ILogger logger, IEnumerable<string> usedBindingTypes)
        {
            // Register our built in extensions
            var bindingProviderTypes = new Collection<Type>()
            {
                // binding providers defined in this assembly
                typeof(WebJobsCoreScriptBindingProvider),

                // binding providers defined in known extension assemblies
                typeof(CoreExtensionsScriptBindingProvider),
            };

            HashSet<Type> existingTypes = new HashSet<Type>();

            // General purpose binder that works directly against SDK.
            // This should eventually replace all other ScriptBindingProvider
            bindingProviderTypes.Add(typeof(GeneralScriptBindingProvider));

            // Create the binding providers
            var bindingProviders = new Collection<ScriptBindingProvider>();
            foreach (var bindingProviderType in bindingProviderTypes)
            {
                try
                {
                    var provider = (ScriptBindingProvider)Activator.CreateInstance(bindingProviderType, new object[] { options, hostMetadata, logger });
                    bindingProviders.Add(provider);
                }
                catch (Exception ex)
                {
                    // If we're unable to load create a binding provider for any reason, log
                    // the error and continue
                    string errorMsg = string.Format("Unable to create binding provider '{0}'", bindingProviderType.FullName);
                    logger.LogError(0, ex, errorMsg);
                }
            }

            return bindingProviders;
        }

        private static FunctionMetadata ParseFunctionMetadata(string functionName, JObject configMetadata, string scriptDirectory, ScriptSettingsManager settingsManager)
        {
            var functionMetadata = new FunctionMetadata
            {
                Name = functionName,
                FunctionDirectory = scriptDirectory
            };

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
                }
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

        public static Collection<FunctionMetadata> ReadFunctionsMetadata(IEnumerable<string> functionDirectories, ILogger logger, Dictionary<string, Collection<string>> functionErrors, ScriptSettingsManager settingsManager = null, IEnumerable<string> functionWhitelist = null, IFileSystem fileSystem = null)
        {
            var functions = new Collection<FunctionMetadata>();
            settingsManager = settingsManager ?? ScriptSettingsManager.Instance;

            if (functionWhitelist != null)
            {
                logger.LogInformation($"A function whitelist has been specified, excluding all but the following functions: [{string.Join(", ", functionWhitelist)}]");
            }

            foreach (var scriptDir in functionDirectories)
            {
                var function = ReadFunctionMetadata(scriptDir, functionErrors, settingsManager, functionWhitelist, fileSystem);
                if (function != null)
                {
                    functions.Add(function);
                }
            }

            return functions;
        }

        public static FunctionMetadata ReadFunctionMetadata(string scriptDir, Dictionary<string, Collection<string>> functionErrors, ScriptSettingsManager settingsManager = null, IEnumerable<string> functionWhitelist = null, IFileSystem fileSystem = null)
        {
            string functionName = null;

            try
            {
                // read the function config
                string json = null;
                if (!TryReadFunctionConfig(scriptDir, out json, fileSystem))
                {
                    // not a function directory
                    return null;
                }

                functionName = Path.GetFileName(scriptDir);
                if (functionWhitelist != null &&
                    !functionWhitelist.Contains(functionName, StringComparer.OrdinalIgnoreCase))
                {
                    // a functions filter has been specified and the current function is
                    // not in the filter list
                    return null;
                }

                ValidateName(functionName);

                JObject functionConfig = JObject.Parse(json);

                string functionError = null;
                FunctionMetadata functionMetadata = null;
                if (!TryParseFunctionMetadata(functionName, functionConfig, scriptDir, settingsManager, out functionMetadata, out functionError, fileSystem))
                {
                    // for functions in error, log the error and don't
                    // add to the functions collection
                    AddFunctionError(functionErrors, functionName, functionError);
                    return null;
                }
                else if (functionMetadata != null)
                {
                    return functionMetadata;
                }
            }
            catch (Exception ex)
            {
                // log any unhandled exceptions and continue
                AddFunctionError(functionErrors, functionName, Utility.FlattenException(ex, includeSource: false), isFunctionShortName: true);
            }
            return null;
        }

        internal Collection<FunctionMetadata> ReadProxyMetadata(ScriptHostOptions config, ScriptSettingsManager settingsManager = null)
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

        internal static bool TryParseFunctionMetadata(string functionName, JObject functionConfig, string scriptDirectory,
                ScriptSettingsManager settingsManager, out FunctionMetadata functionMetadata, out string error, IFileSystem fileSystem = null)
        {
            fileSystem = fileSystem ?? new FileSystem();

            error = null;
            functionMetadata = ParseFunctionMetadata(functionName, functionConfig, scriptDirectory, settingsManager);

            try
            {
                functionMetadata.ScriptFile = DeterminePrimaryScriptFile(functionConfig, scriptDirectory, fileSystem);
            }
            catch (ScriptConfigurationException exc)
            {
                error = exc.Message;
                return false;
            }

            // determine the script type based on the primary script file extension
            functionMetadata.ScriptType = ParseScriptType(functionMetadata.ScriptFile);
            functionMetadata.EntryPoint = (string)functionConfig["entryPoint"];

            return true;
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
                    throw new ScriptConfigurationException("Invalid script file name configuration. The 'scriptFile' property is set to a file that does not exist.");
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
                    throw new ScriptConfigurationException("No function script files present.");
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
                throw new ScriptConfigurationException("Unable to determine the primary function script. Try renaming your entry point script to 'run' (or 'index' in the case of Node), " +
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
                case "fsx":
                    return ScriptType.FSharp;
                case "dll":
                    return ScriptType.DotNetAssembly;
                case "jar":
                    return ScriptType.JavaArchive;
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
                combinedFunctionMetadata = proxies.Concat(functions);

                _descriptorProviders.Add(new ProxyFunctionDescriptorProvider(this, ScriptConfig, _proxyClient, _loggerFactory));
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

                    if (descriptor != null)
                    {
                        ValidateFunction(descriptor, httpFunctions);
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

        private static void ApplyLanguageWorkersConfig(JObject config, ScriptHostOptions scriptConfig, ILogger logger)
        {
            JToken value = null;
            JObject languageWorkersSection = (JObject)config[$"{LanguageWorkerConstants.LanguageWorkersSectionName}"];
            int requestedGrpcMaxMessageLength = ScriptSettingsManager.Instance.IsDynamicSku ? DefaultMaxMessageLengthBytesDynamicSku : DefaultMaxMessageLengthBytes;
            if (languageWorkersSection != null)
            {
                if (languageWorkersSection.TryGetValue("maxMessageLength", out value))
                {
                    int valueInBytes = int.Parse((string)value) * 1024 * 1024;
                    if (ScriptSettingsManager.Instance.IsDynamicSku)
                    {
                        string message = $"Cannot set {nameof(scriptConfig.MaxMessageLengthBytes)} on Consumption plan. Default MaxMessageLength: {DefaultMaxMessageLengthBytesDynamicSku} will be used";
                        logger?.LogWarning(message);
                    }
                    else
                    {
                        if (valueInBytes < 0 || valueInBytes > 2000 * 1024 * 1024)
                        {
                            // Current grpc max message limits
                            string message = $"MaxMessageLength must be between 4MB and 2000MB.Default MaxMessageLength: {DefaultMaxMessageLengthBytes} will be used";
                            logger?.LogWarning(message);
                        }
                        else
                        {
                            requestedGrpcMaxMessageLength = valueInBytes;
                        }
                    }
                }
            }
            scriptConfig.MaxMessageLengthBytes = requestedGrpcMaxMessageLength;
        }

        internal static void ApplyApplicationInsightsConfig(JObject configJson, ScriptHostOptions scriptConfig)
        {
            scriptConfig.ApplicationInsightsSamplingSettings = new SamplingPercentageEstimatorSettings();
            JObject configSection = (JObject)configJson["applicationInsights"];
            if (configSection != null)
            {
                JObject samplingSection = (JObject)configSection["sampling"];
                if (samplingSection != null)
                {
                    if (samplingSection.TryGetValue("isEnabled", out JToken value))
                    {
                        if (bool.TryParse(value.ToString(), out bool isEnabled) && !isEnabled)
                        {
                            scriptConfig.ApplicationInsightsSamplingSettings = null;
                        }
                    }

                    if (scriptConfig.ApplicationInsightsSamplingSettings != null)
                    {
                        if (samplingSection.TryGetValue("maxTelemetryItemsPerSecond", out value))
                        {
                            if (double.TryParse(value.ToString(), out double itemsPerSecond))
                            {
                                scriptConfig.ApplicationInsightsSamplingSettings.MaxTelemetryItemsPerSecond = itemsPerSecond;
                            }
                        }
                    }
                }
            }
        }

        private void HandleHostError(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException("exception");
            }

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

        internal static bool TryGetFunctionFromException(ICollection<FunctionDescriptor> functions, Exception exception, out FunctionDescriptor function)
        {
            function = null;
            if (functions == null)
            {
                return false;
            }
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

        private void NotifyInvoker(string methodName, Exception ex)
        {
            var functionDescriptor = this.Functions.SingleOrDefault(p =>
                    string.Compare(Utility.GetFunctionShortName(methodName), p.Name, StringComparison.OrdinalIgnoreCase) == 0 ||
                    string.Compare(p.Metadata.EntryPoint, methodName, StringComparison.OrdinalIgnoreCase) == 0);
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
                // Check directory snapshot only if "Deleted" change or if directory changed
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
                ScheduleRestartAsync(shutdown).ContinueWith(t => Logger.LogError($"Error restarting host (full shutdown: {shutdown})", t.Exception),
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

        private void ApplyJobHostMetadata()
        {
            var metadataProvider = this.CreateMetadataProvider();
            foreach (var function in Functions)
            {
                var metadata = metadataProvider.GetFunctionMetadata(function.Metadata.Name);
                if (metadata != null)
                {
                    function.Metadata.IsDisabled = metadata.IsDisabled;
                }
            }
        }

        internal static string GetAssemblyFileVersion(Assembly assembly)
        {
            AssemblyFileVersionAttribute fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
            return fileVersionAttr?.Version ?? "Unknown";
        }

        protected override void OnHostInitialized()
        {
            ApplyJobHostMetadata();

            string message = $"Host initialized ({_stopwatch.ElapsedMilliseconds}ms)";
            _startupLogger.LogInformation(message);

            HostInitialized?.Invoke(this, EventArgs.Empty);

            base.OnHostInitialized();
        }

        protected override void OnHostStarted()
        {
            HostStarted?.Invoke(this, EventArgs.Empty);

            base.OnHostStarted();

            string message = $"Host started ({_stopwatch.ElapsedMilliseconds}ms)";
            _startupLogger.LogInformation(message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var subscription in _eventSubscriptions)
                {
                    subscription.Dispose();
                }
                _fileEventSource?.Dispose();
                _functionDispatcher?.Dispose();
                (_processRegistry as IDisposable)?.Dispose();

                if (_debugModeFileWatcher != null)
                {
                    _debugModeFileWatcher.Changed -= OnDebugModeFileChanged;
                    _debugModeFileWatcher.Dispose();
                }

                if (_primaryHostCoordinator != null)
                {
                    _primaryHostCoordinator.HasLeaseChanged -= OnHostLeaseChanged;
                    _primaryHostCoordinator.Dispose();
                }

                foreach (var function in Functions)
                {
                    (function.Invoker as IDisposable)?.Dispose();
                }

                if (_descriptorProviders != null)
                {
                    foreach (var provider in _descriptorProviders)
                    {
                        (provider as IDisposable)?.Dispose();
                    }
                }
            }

            // dispose base last to ensure that errors there don't
            // cause us to not dispose ourselves
            base.Dispose(disposing);
        }
    }
}
