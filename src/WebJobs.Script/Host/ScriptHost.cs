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
using Microsoft.Azure.WebJobs.Extensions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Dispatch;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Eventing.File;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.AppService.Proxy.Client.Contract;
using Microsoft.Azure.WebJobs.Script.Abstractions.Rpc;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Models;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHost : JobHost
    {
        internal const int DebugModeTimeoutMinutes = 15;
        private const string HostAssemblyName = "ScriptHost";
        private const string GeneratedTypeNamespace = "Host";
        internal const string GeneratedTypeName = "Functions";
        private readonly IScriptHostEnvironment _scriptHostEnvironment;
        private readonly ILoggerFactoryBuilder _loggerFactoryBuilder;
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
        public static readonly string Version = GetAssemblyFileVersion(typeof(ScriptHost).Assembly);
        private ScriptSettingsManager _settingsManager;
        private bool _shutdownScheduled;
        private ILogger _startupLogger;
        private FileWatcherEventSource _fileEventSource;
        private IList<IDisposable> _subscriptions = new List<IDisposable>();
        private ProxyClientExecutor _proxyClient;
        private IFunctionDispatcher _functionDispatcher;
        private ILoggerFactory _loggerFactory;

        // Specify the "builtin binding types". These are types that are directly accesible without needing an explicit load gesture.
        // This is the set of bindings we shipped prior to binding extensibility.
        // Map from BindingType to the Assembly Qualified Type name for its IExtensionConfigProvider object.
        // TODO: Re-add built in providers
        private static IReadOnlyDictionary<string, string> _builtinBindingTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            //{ "bot", "Microsoft.Azure.WebJobs.Extensions.BotFramework.Config.BotFrameworkConfiguration, Microsoft.Azure.WebJobs.Extensions.BotFramework" },
            //{ "sendgrid", "Microsoft.Azure.WebJobs.Extensions.SendGrid.SendGridConfiguration, Microsoft.Azure.WebJobs.Extensions.SendGrid" },
            //{ "eventGridTrigger", "Microsoft.Azure.WebJobs.Extensions.EventGrid.EventGridExtensionConfig, Microsoft.Azure.WebJobs.Extensions.EventGrid" }
        };

        private static IReadOnlyDictionary<string, string> _builtinScriptBindingTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "twilioSms", "Microsoft.Azure.WebJobs.Script.Binding.TwilioScriptBindingProvider" },
            { "notificationHub", "Microsoft.Azure.WebJobs.Script.Binding.NotificationHubScriptBindingProvider" },
            { "cosmosDBTrigger", "Microsoft.Azure.WebJobs.Script.Binding.DocumentDBScriptBindingProvider" },
            { "documentDB", "Microsoft.Azure.WebJobs.Script.Binding.DocumentDBScriptBindingProvider" },
            { "mobileTable", "Microsoft.Azure.WebJobs.Script.Binding.MobileAppsScriptBindingProvider" },
            { "apiHubFileTrigger", "Microsoft.Azure.WebJobs.Script.Binding.ApiHubScriptBindingProvider" },
            { "apiHubFile", "Microsoft.Azure.WebJobs.Script.Binding.ApiHubScriptBindingProvider" },
            { "apiHubTable", "Microsoft.Azure.WebJobs.Script.Binding.ApiHubScriptBindingProvider" },
            { "serviceBusTrigger", "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
            { "serviceBus", "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
            { "eventHubTrigger", "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
            { "eventHub", "Microsoft.Azure.WebJobs.Script.Binding.ServiceBusScriptBindingProvider" },
        };

        // For backwards compat, we support a #r directly to these assemblies.
        private static HashSet<string> _assemblyWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Twilio.Api" },
            { "Microsoft.Azure.WebJobs.Extensions.Twilio" },
            { "Microsoft.Azure.NotificationHubs" },
            { "Microsoft.WindowsAzure.Mobile" },
            { "Microsoft.Azure.WebJobs.Extensions.MobileApps" },
            { "Microsoft.Azure.WebJobs.Extensions.NotificationHubs" },
            { "Microsoft.WindowsAzure.Mobile" },
            { "Microsoft.Azure.WebJobs.Extensions.MobileApps" },
            { "Microsoft.Azure.Documents.Client" },
            { "Microsoft.Azure.WebJobs.Extensions.DocumentDB" },
            { "Microsoft.Azure.ApiHub.Sdk" },
            { "Microsoft.Azure.WebJobs.Extensions.ApiHub" },
            { "Microsoft.ServiceBus" },
            { "Sendgrid" },
        };

        protected internal ScriptHost(IScriptHostEnvironment environment,
            IScriptEventManager eventManager,
            ScriptHostConfiguration scriptConfig = null,
            ScriptSettingsManager settingsManager = null,
            ILoggerFactoryBuilder loggerFactoryBuilder = null,
            ProxyClientExecutor proxyClient = null)
            : base(scriptConfig.HostConfig)
        {
            scriptConfig = scriptConfig ?? new ScriptHostConfiguration();
            _loggerFactoryBuilder = loggerFactoryBuilder ?? new DefaultLoggerFactoryBuilder();
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

        protected virtual void Initialize()
        {
            FileUtility.EnsureDirectoryExists(ScriptConfig.RootScriptPath);
            string hostLogPath = Path.Combine(ScriptConfig.RootLogPath, "Host");
            FileUtility.EnsureDirectoryExists(hostLogPath);
            string debugSentinelFileName = Path.Combine(hostLogPath, ScriptConstants.DebugSentinelFileName);

            LastDebugNotify = File.Exists(debugSentinelFileName)
                ? File.GetLastWriteTimeUtc(debugSentinelFileName)
                : DateTime.MinValue;

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

                var hostConfig = ScriptConfig.HostConfig;
                if (hostConfig.IsDevelopment || InDebugMode)
                {
                    // If we're in debug/development mode, use optimal debug settings
                    hostConfig.UseDevelopmentSettings();
                }

                // Ensure we always have an ILoggerFactory,
                // regardless of whether AppInsights is registered or not
                if (hostConfig.LoggerFactory == null)
                {
                    hostConfig.LoggerFactory = new LoggerFactory();

                    // If we've created the LoggerFactory, then we are responsible for
                    // disposing. Store this locally for disposal later. We can't rely
                    // on accessing this directly from ScriptConfig.HostConfig as the
                    // ScriptConfig is re-used for every host.
                    _loggerFactory = hostConfig.LoggerFactory;
                }

                Func<string, FunctionDescriptor> funcLookup = (name) => this.GetFunctionOrNull(name);
                hostConfig.AddService(funcLookup);

                // Set up a host level TraceMonitor that will receive notification
                // of ALL errors that occur. This allows us to inspect/log errors.
                // TODO: FACAVAL
                var traceMonitor = new TraceMonitor()
                    .Filter(p => { return true; })
                    .Subscribe(HandleHostError);
                hostConfig.Tracing.Tracers.Add(traceMonitor);

                System.Diagnostics.TraceLevel hostTraceLevel = hostConfig.Tracing.ConsoleLevel;
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
                    hostConfig.Tracing.Tracers.Add(TraceWriter);
                }
                else
                {
                    // if no TraceWriter has been configured, default it to Console
                    TraceWriter = new ConsoleTraceWriter(hostTraceLevel);
                }

                string readingFileMessage = string.Format(CultureInfo.InvariantCulture, "Reading host configuration file '{0}'", hostConfigFilePath);
                TraceWriter.Info(readingFileMessage);

                string json = File.ReadAllText(hostConfigFilePath);

                string readFileMessage = $"Host configuration file read:{Environment.NewLine}{json}";
                TraceWriter.Info(readFileMessage);

                JObject hostConfigObject;
                try
                {
                    hostConfigObject = JObject.Parse(json);
                }
                catch (JsonException ex)
                {
                    // If there's a parsing error, set up the logger and write out the previous messages so that they're
                    // discoverable in Application Insights. There's no filter, but that's okay since we cannot parse host.json to
                    // determine how to build the filtler.
                    ConfigureDefaultLoggerFactory();
                    ILogger startupErrorLogger = hostConfig.LoggerFactory.CreateLogger(LogCategories.Startup);
                    startupErrorLogger.LogInformation(readingFileMessage);
                    startupErrorLogger.LogInformation(readFileMessage);

                    throw new FormatException(string.Format("Unable to parse {0} file.", ScriptConstants.HostMetadataFileName), ex);
                }

                try
                {
                    ApplyConfiguration(hostConfigObject, ScriptConfig);
                }
                catch (Exception)
                {
                    // If we have an error applying the configuration (for example, a value is invalid),
                    // make sure we have a default LoggerFactory so that the error log can be written.
                    ConfigureDefaultLoggerFactory();
                    throw;
                }

                // Allow tests to modify anything initialized by host.json
                ScriptConfig.OnConfigurationApplied?.Invoke(ScriptConfig);

                ConfigureDefaultLoggerFactory();

                // Use the startupLogger in this class as it is concerned with startup. The public Logger is used
                // for all other logging after startup.
                _startupLogger = hostConfig.LoggerFactory.CreateLogger(LogCategories.Startup);
                Logger = hostConfig.LoggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

                // Do not log these until after all the configuration is done so the proper filters are applied.
                _startupLogger.LogInformation(readingFileMessage);
                _startupLogger.LogInformation(readFileMessage);

                if (string.IsNullOrEmpty(hostConfig.HostId))
                {
                    hostConfig.HostId = Utility.GetDefaultHostId(_settingsManager, ScriptConfig);
                }
                if (string.IsNullOrEmpty(hostConfig.HostId))
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
                    hostConfig.StorageConnectionString = null;
                }

                var serverImpl = new FunctionRpcImpl(EventManager);
                var server = new GrpcServer(serverImpl);
                server.Start();
                var processFactory = new DefaultWorkerProcessFactory();

                CreateChannel channelFactory = (config, registrations) =>
                {
                    return new LanguageWorkerChannel(ScriptConfig, EventManager, processFactory, registrations, config, server.Uri, hostConfig.LoggerFactory);
                };

                _functionDispatcher = new FunctionDispatcher(EventManager, server, channelFactory, TraceWriter, new List<WorkerConfig>()
                {
                    new NodeLanguageWorkerConfig(),
                    new JavaLanguageWorkerConfig()
                });

                _subscriptions.Add(EventManager.OfType<WorkerErrorEvent>()
                    .Subscribe(evt =>
                    {
                        HandleHostError(evt.Exception);
                    }));

                _subscriptions.Add(EventManager.OfType<HostRestartEvent>()
                    .Subscribe((msg) => ScheduleRestartAsync(false)));

                if (ScriptConfig.FileWatchingEnabled)
                {
                    _fileEventSource = new FileWatcherEventSource(EventManager, EventSources.ScriptFiles, ScriptConfig.RootScriptPath);

                    _subscriptions.Add(EventManager.OfType<FileEvent>()
                        .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                        .Subscribe(e => OnFileChanged(e.FileChangeArguments)));
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

                // Scan the function.json early to determine the requirements.
                var functionMetadata = ReadFunctionMetadata(ScriptConfig, TraceWriter, _startupLogger, FunctionErrors, _settingsManager);
                var usedBindingTypes = DiscoverBindingTypes(functionMetadata);

                bool useLazyLoad = false; // todo - https://github.com/Azure/azure-webjobs-sdk-script/issues/1637
                if (!useLazyLoad)
                {
                    usedBindingTypes = _builtinBindingTypes.Keys.Concat(_builtinScriptBindingTypes.Keys).ToArray();
                }

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
                LoadBuiltinBindings(usedBindingTypes);

                var directTypes = GetDirectTypes(functionMetadata);

                LoadDirectlyReferencesExtensions(directTypes);

                LoadCustomExtensions();

                // Do this after we've loaded the custom extensions. That gives an extension an opportunity to plug in their own implementations.
                if (storageString != null)
                {
                    var lockManager = (IDistributedLockManager)Services.GetService(typeof(IDistributedLockManager));
                    _blobLeaseManager = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), hostConfig.HostId, InstanceId, TraceWriter, hostConfig.LoggerFactory);
                }

                // Create the lease manager that will keep handle the primary host blob lease acquisition and renewal
                // and subscribe for change notifications.
                if (_blobLeaseManager != null)
                {
                    _blobLeaseManager.HasLeaseChanged += BlobLeaseManagerHasLeaseChanged;
                }

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

                hostConfig.TypeLocator = new TypeLocator(types);

                Functions = functions;

                if (ScriptConfig.FileLoggingMode != FileLoggingMode.Never)
                {
                    PurgeOldLogDirectories();
                }
            }
        }

        private void LoadBuiltinBindings(IEnumerable<string> bindingTypes)
        {
            foreach (var bindingType in bindingTypes)
            {
                string assemblyQualifiedTypeName;
                if (_builtinBindingTypes.TryGetValue(bindingType, out assemblyQualifiedTypeName))
                {
                    Type typeExtension = Type.GetType(assemblyQualifiedTypeName);
                    if (typeExtension == null)
                    {
                        string errorMsg = $"Can't find builtin provider '{assemblyQualifiedTypeName}' for '{bindingType}'";
                        TraceWriter.Error(errorMsg);
                        _startupLogger?.LogError(errorMsg);
                    }
                    else
                    {
                        IExtensionConfigProvider extension = (IExtensionConfigProvider)Activator.CreateInstance(typeExtension);
                        LoadExtension(extension);
                    }
                }
            }
        }

        private static IEnumerable<string> DiscoverBindingTypes(IEnumerable<FunctionMetadata> functions)
        {
            HashSet<string> bindingTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var function in functions)
            {
                foreach (var binding in function.InputBindings.Concat(function.OutputBindings))
                {
                    string bindingType = binding.Type;
                    bindingTypes.Add(bindingType);
                }
            }
            return bindingTypes;
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

        private void LoadCustomExtensions()
        {
            string binPath = Path.Combine(ScriptConfig.RootScriptPath, "bin");
            string metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);
            if (File.Exists(metadataFilePath))
            {
                var extensionMetadata = JObject.Parse(File.ReadAllText(metadataFilePath));

                var extensionItems = extensionMetadata["extensions"]?.ToObject<List<ExtensionReference>>();
                if (extensionItems == null)
                {
                    TraceWriter.Warning("Invalid extensions metadata file. Unable to load custom extensions");
                    return;
                }

                foreach (var item in extensionItems)
                {
                    string extensionName = item.Name ?? item.TypeName;
                    TraceWriter.Info($"Loading custom extension '{extensionName}'");

                    Type extensionType = Type.GetType(item.TypeName,
                        assemblyName =>
                        {
                            string path = item.HintPath;

                            if (string.IsNullOrEmpty(path))
                            {
                                path = assemblyName.Name + ".dll";
                            }

                            var hintUri = new Uri(path, UriKind.RelativeOrAbsolute);
                            if (!hintUri.IsAbsoluteUri)
                            {
                                path = Path.Combine(binPath, path);
                            }

                            if (File.Exists(path))
                            {
                                return Assembly.LoadFrom(path);
                            }

                            return null;
                        },
                        (assembly, typeName, ignoreCase) =>
                        {
                            return assembly?.GetType(typeName, false, ignoreCase);
                        }, false, true);

                    if (extensionType == null ||
                        !LoadIfExtensionType(extensionType, extensionType.Assembly.Location))
                    {
                        TraceWriter.Warning($"Unable to load custom extension type for extension '{extensionName}' (Type: `{item.TypeName}`)." +
                                $"The type does not exist or is not a valid extension. Please validate the type and assembly names.");
                    }
                }
            }

            // Now all extensions have been loaded, the metadata is finalized.
            // There's a single script binding instance that services all extensions.
            // give that script binding the metadata for all loaded extensions so it can dispatch to them.
            var generalProvider = ScriptConfig.BindingProviders.OfType<GeneralScriptBindingProvider>().First();
            var metadataProvider = this.CreateMetadataProvider();
            generalProvider.CompleteInitialization(metadataProvider);
        }

        // Load extensions that are directly references by the user types.
        private void LoadDirectlyReferencesExtensions(IEnumerable<Type> userTypes)
        {
            var possibleExtensionAssemblies = UserTypeScanner.GetPossibleExtensionAssemblies(userTypes);

            foreach (var kv in possibleExtensionAssemblies)
            {
                var assembly = kv.Key;
                var locationHint = kv.Value;
                LoadExtensions(assembly, locationHint);
            }
        }

        private void LoadExtensions(Assembly assembly, string locationHint)
        {
            foreach (var type in assembly.ExportedTypes)
            {
                LoadIfExtensionType(type, locationHint);
            }
        }

        private bool LoadIfExtensionType(Type extensionType, string locationHint)
        {
            if (!typeof(IExtensionConfigProvider).IsAssignableFrom(extensionType))
            {
                return false;
            }

            if (IsExtensionLoaded(extensionType))
            {
                return false;
            }

            IExtensionConfigProvider instance = (IExtensionConfigProvider)Activator.CreateInstance(extensionType);
            LoadExtension(instance, locationHint);

            return true;
        }

        private bool IsExtensionLoaded(Type type)
        {
            var registry = ScriptConfig.HostConfig.GetService<IExtensionRegistry>();
            var extensions = registry.GetExtensions<IExtensionConfigProvider>();
            foreach (var extension in extensions)
            {
                var loadedExtentionType = extension.GetType();
                if (loadedExtentionType == type)
                {
                    return true;
                }
            }
            return false;
        }

        // Load a single extension
        private void LoadExtension(IExtensionConfigProvider instance, string locationHint = null)
        {
            JobHostConfiguration config = this.ScriptConfig.HostConfig;

            var type = instance.GetType();
            string name = type.Name;

            string msg = $"Loaded custom extension: {name} from '{locationHint}'";
            TraceWriter.Info(msg);
            _startupLogger.LogInformation(msg);
            config.AddExtension(instance);
        }

        private void ConfigureDefaultLoggerFactory()
        {
            ConfigureLoggerFactory(ScriptConfig, FunctionTraceWriterFactory, _settingsManager, _loggerFactoryBuilder, () => FileLoggingEnabled);
        }

        internal static void ConfigureLoggerFactory(ScriptHostConfiguration scriptConfig, IFunctionTraceWriterFactory traceWriteFactory,
            ScriptSettingsManager settingsManager, ILoggerFactoryBuilder builder, Func<bool> isFileLoggingEnabled)
        {
            // Register a file logger that only logs user logs and only if file logging is enabled.
            // We don't allow this to be replaced; if you want to disable it, you can use host.json to do so.
            scriptConfig.HostConfig.LoggerFactory.AddProvider(new FileLoggerProvider(traceWriteFactory,
                (category, level) => isFileLoggingEnabled()));

            // Allow a way to plug in custom LoggerProviders.
            builder.AddLoggerProviders(scriptConfig.HostConfig.LoggerFactory, scriptConfig, settingsManager);
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
#else
            await Task.CompletedTask;
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

        public static ScriptHost Create(IScriptHostEnvironment environment, IScriptEventManager eventManager,
            ScriptHostConfiguration scriptConfig = null, ScriptSettingsManager settingsManager = null, ILoggerFactoryBuilder loggerFactoryBuilder = null, ProxyClientExecutor proxyClient = null)
        {
            ScriptHost scriptHost = new ScriptHost(environment, eventManager, scriptConfig, settingsManager, loggerFactoryBuilder, proxyClient);
            try
            {
                scriptHost.Initialize();
            }
            catch (Exception ex)
            {
                string errorMsg = "ScriptHost initialization failed";
                scriptHost.TraceWriter?.Error(errorMsg, ex);

                ILogger logger = scriptConfig?.HostConfig?.LoggerFactory?.CreateLogger(LogCategories.Startup);
                logger?.LogError(0, ex, errorMsg);

                throw;
            }

            return scriptHost;
        }

        // Get the ScriptBindingProviderType for a given binding type.
        // Null if no match.
        private static Type GetScriptBindingProvider(string bindingType)
        {
            if (_builtinScriptBindingTypes.TryGetValue(bindingType, out string assemblyQualifiedTypeName))
            {
                var type = Type.GetType(assemblyQualifiedTypeName);
                return type;
            }
            return null;
        }

        private static Collection<ScriptBindingProvider> LoadBindingProviders(ScriptHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter, ILogger logger, IEnumerable<string> usedBindingTypes)
        {
            JobHostConfiguration hostConfig = config.HostConfig;

            // Register our built in extensions
            var bindingProviderTypes = new Collection<Type>()
            {
                // binding providers defined in this assembly
                typeof(WebJobsCoreScriptBindingProvider),

                // TODO: FACAVAL
                // typeof(ServiceBusScriptBindingProvider),

                // binding providers defined in known extension assemblies
                // TODO: FACAVAL - These will be re-enabled as we migrate the extensions
                typeof(CoreExtensionsScriptBindingProvider),
            };

            HashSet<Type> existingTypes = new HashSet<Type>();

            // Add custom providers for any other types being used from function.json
            foreach (var usedType in usedBindingTypes)
            {
                var type = GetScriptBindingProvider(usedType);
                if (type != null && existingTypes.Add(type))
                {
                    bindingProviderTypes.Add(type);
                }
            }

            // General purpose binder that works directly against SDK.
            // This should eventually replace all other ScriptBindingProvider
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

                    ValidateFunctionName(functionName);

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

            var proxies = LoadProxyRoutes(proxiesJson);

            return proxies;
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

                proxyMetadata.Name = route.Name;
                proxyMetadata.ScriptType = ScriptType.Unknown;
                proxyMetadata.IsProxy = true;

                proxies.Add(proxyMetadata);
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

        internal static void ValidateFunctionName(string functionName)
        {
            if (!FunctionNameValidationRegex.IsMatch(functionName))
            {
                throw new InvalidOperationException(string.Format("'{0}' is not a valid function name.", functionName));
            }
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
                case "jar":
                    return ScriptType.JavaArchive;
                default:
                    return ScriptType.Unknown;
            }
        }

        private Collection<FunctionDescriptor> GetFunctionDescriptors(Collection<FunctionMetadata> functions)
        {
            var proxies = ReadProxyMetadata(ScriptConfig, _settingsManager);

            var descriptorProviders = new List<FunctionDescriptorProvider>()
                {
                    new ScriptFunctionDescriptorProvider(this, ScriptConfig),
                    new DotNetFunctionDescriptorProvider(this, ScriptConfig),
#if FEATURE_POWERSHELL
                    new PowerShellFunctionDescriptorProvider(this, ScriptConfig),
#endif
                    new WorkerFunctionDescriptorProvider(this, ScriptConfig, _functionDispatcher),
                };

            IEnumerable<FunctionMetadata> combinedFunctionMetadata = null;
            if (proxies != null && proxies.Any())
            {
                // Proxy routes will take precedence over http trigger functions and http trigger routes so they will be added first to the list of function descriptors.
                combinedFunctionMetadata = proxies.Concat(functions);

                descriptorProviders.Add(new ProxyFunctionDescriptorProvider(this, ScriptConfig, _proxyClient));
            }
            else
            {
                combinedFunctionMetadata = functions;
            }

            return GetFunctionDescriptors(combinedFunctionMetadata, descriptorProviders);
        }

        internal Collection<FunctionDescriptor> GetFunctionDescriptors(IEnumerable<FunctionMetadata> functions, IEnumerable<FunctionDescriptorProvider> descriptorProviders)
        {
            Collection<FunctionDescriptor> functionDescriptors = new Collection<FunctionDescriptor>();

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

                    // TODO: FACAVAL
                    // ValidateFunction(descriptor, httpFunctions);

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

        // TODO: FACAVAL
        //internal static void ValidateFunction(FunctionDescriptor function, Dictionary<string, HttpTriggerAttribute> httpFunctions)
        //{
        //    var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
        //    if (httpTrigger != null)
        //    {
        //        bool isProxy = function.Metadata != null && function.Metadata.IsProxy;

        //        ValidateHttpFunction(function.Name, httpTrigger, isProxy);

        //        if (!isProxy)
        //        {
        //            // prevent duplicate/conflicting routes for functions
        //            // proxy routes check is done in the proxy dll itself and proxies do not use routePrefix so should not check conflict with functions
        //            foreach (var pair in httpFunctions)
        //            {
        //                if (HttpRoutesConflict(httpTrigger, pair.Value))
        //                {
        //                    throw new InvalidOperationException($"The route specified conflicts with the route defined by function '{pair.Key}'.");
        //                }
        //            }
        //        }

        //        httpFunctions.Add(function.Name, httpTrigger);
        //    }
        //}

        //internal static void ValidateHttpFunction(string functionName, HttpTriggerAttribute httpTrigger, bool isProxy = false)
        //{
        //    if (string.IsNullOrWhiteSpace(httpTrigger.Route) && !isProxy)
        //    {
        //        // if no explicit route is provided, default to the function name
        //        httpTrigger.Route = functionName;
        //    }

        //    // disallow custom routes in our own reserved route space
        //    string httpRoute = httpTrigger.Route.Trim('/').ToLowerInvariant();
        //    if (httpRoute.StartsWith("admin"))
        //    {
        //        throw new InvalidOperationException("The specified route conflicts with one or more built in routes.");
        //    }
        //}

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

            // Apply Tracing/Logging configuration
            configSection = (JObject)config["tracing"];
            if (configSection != null)
            {
                if (configSection.TryGetValue("consoleLevel", out value))
                {
                    System.Diagnostics.TraceLevel consoleLevel;
                    if (Enum.TryParse<System.Diagnostics.TraceLevel>((string)value, true, out consoleLevel))
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
            // TODO: SamplingPercentageEstimatorSettings is not currently available.
            // Working with the team/brettsam to find an alternative
            // There's also an internal out-of-band option if needed.
#if false
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
#endif
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (TraceWriter as IDisposable)?.Dispose();
                foreach (var subscription in _subscriptions)
                {
                    subscription.Dispose();
                }
                _fileEventSource?.Dispose();
                _debugModeFileWatcher?.Dispose();
                _blobLeaseManager?.Dispose();
                _functionDispatcher?.Dispose();

                foreach (var function in Functions)
                {
                    (function.Invoker as IDisposable)?.Dispose();
                }

                _loggerFactory?.Dispose();
            }

            // dispose base last to ensure that errors there don't
            // cause us to not dispose ourselves
            base.Dispose(disposing);
        }
    }
}