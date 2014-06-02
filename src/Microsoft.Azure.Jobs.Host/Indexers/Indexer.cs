using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Queues.Bindings;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.Azure.Jobs.Host.Tables;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Go down and build an index
    internal class Indexer
    {
        private static readonly BindingFlags _publicStaticMethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly string _azureJobsFileName = typeof(TableAttribute).Assembly.ManifestModule.Name;

        private static readonly IEnumerable<IStaticBindingProvider> _staticBindingProviders =
            new IStaticBindingProvider[]
            {
                new CancellationTokenStaticBindingProvider(),
                new CloudStorageAccountStaticBindingProvider(),
                // The console output binder below will handle all remaining TextWriter parameters. It must come after
                // the Attribute binder; otherwise bindings like Do([Blob("a/b")] TextWriter blob) wouldn't work.
                new ConsoleOutputStaticBindingProvider()
            };

        private readonly IFunctionTable _functionTable;
        private readonly INameResolver _nameResolver;
        private readonly IConfiguration _configuration;
        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;

        // Account for where index lives
        public Indexer(IFunctionTable functionTable, INameResolver nameResolver, IConfiguration configuration)
        {
            _functionTable = functionTable;
            _nameResolver = nameResolver;
            _configuration = configuration;
            _triggerBindingProvider = CreateTriggerBindingProvider(configuration);
            _bindingProvider = CreateBindingProvider(configuration);
        }

        public static string AzureJobsFileName
        {
            get { return _azureJobsFileName; }
        }

        public IBindingProvider BindingProvider
        {
            get { return _bindingProvider; }
        }

        public INameResolver NameResolver
        {
            get { return _nameResolver; }
        }

        private static ITriggerBindingProvider CreateTriggerBindingProvider(IConfiguration configuration)
        {
            List<ITriggerBindingProvider> innerProviders = new List<ITriggerBindingProvider>();
            innerProviders.Add(new QueueTriggerAttributeBindingProvider());

            IEnumerable<Type> cloudBlobStreamBinderTypes = GetCloudBlobStreamBinderTypes(configuration);
            innerProviders.Add(new BlobTriggerAttributeBindingProvider(cloudBlobStreamBinderTypes));

            Type serviceBusProviverType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.Jobs.ServiceBus.Triggers.ServiceBusTriggerAttributeBindingProvider");

            if (serviceBusProviverType != null)
            {
                ITriggerBindingProvider serviceBusAttributeBindingProvider =
                    (ITriggerBindingProvider)Activator.CreateInstance(serviceBusProviverType);
                innerProviders.Add(serviceBusAttributeBindingProvider);
            }

            return new CompositeTriggerBindingProvider(innerProviders);
        }

        private static IBindingProvider CreateBindingProvider(IConfiguration configuration)
        {
            List<IBindingProvider> innerProviders = new List<IBindingProvider>();
            innerProviders.Add(new QueueAttributeBindingProvider());

            IEnumerable<Type> cloudBlobStreamBinderTypes = GetCloudBlobStreamBinderTypes(configuration);
            innerProviders.Add(new BlobAttributeBindingProvider(cloudBlobStreamBinderTypes));

            innerProviders.Add(new TableAttributeBindingProvider());

            Type serviceBusProviderType = ServiceBusExtensionTypeLoader.Get(
                "Microsoft.Azure.Jobs.ServiceBus.Bindings.ServiceBusAttributeBindingProvider");

            if (serviceBusProviderType != null)
            {
                IBindingProvider serviceBusAttributeBindingProvider =
                    (IBindingProvider)Activator.CreateInstance(serviceBusProviderType);
                innerProviders.Add(serviceBusAttributeBindingProvider);
            }

            innerProviders.Add(new RuntimeBindingProvider());

            return new CompositeBindingProvider(innerProviders);
        }

        private static IEnumerable<Type> GetCloudBlobStreamBinderTypes(IConfiguration configuration)
        {
            IEnumerable<Type> types;

            if (configuration != null)
            {
                types = configuration.CloudBlobStreamBinderTypes;
            }
            else
            {
                types = null;
            }

            return types;
        }

        private static MethodInfo ResolveMethod(Type type, string name)
        {
            var method = type.GetMethod(name, _publicStaticMethodFlags);
            if (method == null)
            {
                string msg = string.Format("A public static method '{0}' could not be found on type '{1}'.", name, type.FullName);
                throw new InvalidOperationException(msg);
            }
            return method;
        }

        public void IndexType(Func<MethodInfo, FunctionLocation> funcApplyLocation, Type type, string storageConnectionString, string serviceBusConnectionString)
        {
            var context = InvokeInitMethodOnType(type, GetCloudStorageAccount(storageConnectionString), serviceBusConnectionString, funcApplyLocation);

            // Now register any declaritive methods
            foreach (MethodInfo method in type.GetMethods(_publicStaticMethodFlags))
            {
                IndexMethod(funcApplyLocation, method, context);
            }

            EnsureNoDuplicateFunctions();
        }

        private static CloudStorageAccount GetCloudStorageAccount(string connectionString)
        {
            if (connectionString == null)
            {
                return null;
            }

            return CloudStorageAccount.Parse(connectionString);
        }

        // Check for duplicate names. Indexing doesn't support overloads.
        private void EnsureNoDuplicateFunctions()
        {
            HashSet<string> locations = new HashSet<string>();

            foreach (FunctionDefinition func in _functionTable.ReadAll())
            {
                var locationKey = func.Location.ToString();
                if (!locations.Add(locationKey))
                {
                    // Dup found!
                    string msg = string.Format("Method overloads are not supported. There are multiple methods with the name '{0}'.", locationKey);
                    throw new InvalidOperationException(msg);
                }
            }
        }

        // Invoke the Initialize(IConfiguration) hook on a type in the assembly we're indexing.
        // Register any functions provided by code-configuration.
        private IndexTypeContext InvokeInitMethodOnType(Type type, CloudStorageAccount storageAccount, string serviceBusConnectionString, Func<MethodInfo, FunctionLocation> funcApplyLocation)
        {
            IConfiguration configuration;

            if (_configuration != null)
            {
                configuration = _configuration;
            }
            else
            {
                // Test-only shortcut
                configuration = CreateTestConfiguration(type);
            }

            return new IndexTypeContext
            {
                Config = configuration,
                StorageAccount = storageAccount,
                ServiceBusConnectionString = serviceBusConnectionString
            };
        }

        private IConfiguration CreateTestConfiguration(Type type)
        {
            var config = new Configuration();
            RunnerProgram.ApplyHooks(type, config);
            return config;
        }

        // Helper to convert delegates.
        private Func<MethodDescriptor, FunctionLocation> Convert(Func<string, MethodInfo> fpFuncLookup, Func<MethodInfo, FunctionLocation> funcApplyLocation)
        {
            Func<MethodDescriptor, FunctionLocation> funcApplyLocation2 =
             (descr) =>
             {
                 MethodInfo method = fpFuncLookup(descr.Name);
                 return funcApplyLocation(method);
             };

            return funcApplyLocation2;
        }

        // Entry-point from reflection-based configuration. This is looking at inline attributes.
        public void IndexMethod(Func<MethodInfo, FunctionLocation> funcApplyLocation, MethodInfo method, IndexTypeContext context)
        {
            MethodDescriptor descr = GetMethodDescriptor(method);

            Func<string, MethodInfo> fpFuncLookup = name => ResolveMethod(method.DeclaringType, name);
            Func<MethodDescriptor, FunctionLocation> funcApplyLocation2 = Convert(fpFuncLookup, funcApplyLocation);

            IndexMethod(funcApplyLocation2, descr, context);
        }

        // Container is where the method lived on the cloud.
        // Common path for both attribute-cased and code-based configuration.
        public void IndexMethod(Func<MethodDescriptor, FunctionLocation> funcApplyLocation, MethodDescriptor descr, IndexTypeContext context)
        {
            FunctionDefinition index = GetFunctionDefinition(descr, context);
            if (index != null)
            {
                FunctionLocation loc = funcApplyLocation(descr);
                index.Location = loc;

                _functionTable.Add(index);
            }
        }

        // Get any bindings that can be explicitly deduced.
        // This always returns a non-null array, but array may have null elements
        // for bindings that can't be determined.
        // - either those bindings are user supplied parameters (which means this function
        //   can't be invoked by an automatic trigger)
        // - or the function shouldn't be indexed at all.
        // Caller will make that distinction.
        public ParameterStaticBinding[] CreateExplicitBindings(MethodDescriptor descr, IEnumerable<string> triggerParameterNames)
        {
            ParameterInfo[] ps = descr.Parameters;

            ParameterStaticBinding[] flows = Array.ConvertAll(ps, BindParameter);

            // Populate input names
            HashSet<string> paramNames = new HashSet<string>(triggerParameterNames);

            // Take a second pass to bind params directly to {key} in the attributes above,.
            // So if we have p1 with attr [BlobInput(@"daas-test-input2/{name}.csv")],
            // then we'll bind 'string name' to the {name} value.
            for (int pos = 0; pos < ps.Length; pos++)
            {
                if (flows[pos] == null)
                {
                    var parameterName = ps[pos].Name;
                    if (paramNames.Contains(parameterName))
                    {
                        flows[pos] = new NameParameterStaticBinding { Name = parameterName };
                    }
                }
            }

            return flows;
        }

        private ParameterStaticBinding BindParameter(ParameterInfo parameter)
        {
            foreach (IStaticBindingProvider provider in _staticBindingProviders)
            {
                ParameterStaticBinding binding = provider.TryBind(parameter, _nameResolver);

                if (binding != null)
                {
                    return binding;
                }
            }

            return null;
        }

        public static void AddInvokeBindings(MethodDescriptor descr, ParameterStaticBinding[] flows)
        {
            ParameterInfo[] ps = descr.Parameters;

            for (int i = 0; i < flows.Length; i++)
            {
                if (flows[i] == null)
                {
                    string name = ps[i].Name;
                    flows[i] = new InvokeParameterStaticBinding { Name = name };
                }
            }
        }

        private static MethodDescriptor GetMethodDescriptor(MethodInfo method)
        {
            var descr = new MethodDescriptor();
            descr.Name = method.Name;
            descr.MethodAttributes = Array.ConvertAll(method.GetCustomAttributes(true), attr => (Attribute)attr);
            descr.Parameters = method.GetParameters();

            return descr;
        }

        // Test hook. 
        static public FunctionDefinition GetFunctionDefinitionTest(MethodInfo method, IndexTypeContext context)
        {
            Indexer idx = new Indexer(null, null, null);
            return idx.GetFunctionDefinition(method, context);
        }

        public FunctionDefinition GetFunctionDefinition(MethodInfo method, IndexTypeContext context)
        {
            MethodDescriptor descr = GetMethodDescriptor(method);
            return GetFunctionDefinition(descr, context);
        }

        // Returns a partially instantiated FunctionIndexEntity.
        // Caller must add Location information.
        public FunctionDefinition GetFunctionDefinition(MethodDescriptor descr, IndexTypeContext context)
        {
            try
            {
                return GetDescriptionForMethodInternal(descr, context);
            }
            catch (Exception e)
            {
                if (e is IndexException)
                {
                    throw;
                }
                throw IndexException.NewMethod(descr.Name, e);
            }
        }

        private FunctionDefinition GetDescriptionForMethodInternal(MethodDescriptor descr, IndexTypeContext context)
        {
            FunctionDefinition index = CreateFunctionDefinition(descr, context);

            string description = null;

            NoAutomaticTriggerAttribute noAutomaticTrigger = null;

            foreach (var attr in descr.MethodAttributes)
            {
                noAutomaticTrigger = noAutomaticTrigger ?? (attr as NoAutomaticTriggerAttribute);

                var descriptionAttr = attr as DescriptionAttribute;
                if (descriptionAttr != null)
                {
                    description = descriptionAttr.Description;
                }
            }

            // $$$ Lots of other static checks to add.

            IEnumerable<string> triggerParameterNames;

            if (index.TriggerBinding != null && index.TriggerBinding.BindingDataContract != null)
            {
                triggerParameterNames = index.TriggerBinding.BindingDataContract.Keys;
            }
            else
            {
                triggerParameterNames = Enumerable.Empty<string>();
            }

            // Look at parameters.
            ParameterStaticBinding[] parameterBindings = CreateExplicitBindings(descr, triggerParameterNames);

            bool hasAnyBindings = Array.Find(parameterBindings, x => x != null) != null || index.NonTriggerBindings.Count > 0;
            AddInvokeBindings(descr, parameterBindings);

            //
            // We now have all the explicitly provided information. Put it together.
            //

            if (index.TriggerBinding == null && noAutomaticTrigger == null && !hasAnyBindings && description == null)
            {
                // No trigger, binding, NoAutomaticTrigger attribute or Description attribute.
                // Ignore this function completely.
                return null;
            }

            index.Flow = new FunctionFlow { Bindings = parameterBindings };

            if (context.Config != null)
            {
                ValidateParameters(parameterBindings, descr.Parameters, context.Config);
            }

            if (index.TriggerBinding != null && noAutomaticTrigger != null)
            {
                throw new InvalidOperationException("Can't have a trigger and NoAutomaticTrigger on the same function.");
            }

            Validate(index);

            return index;
        }

        private FunctionDefinition CreateFunctionDefinition(MethodDescriptor method, IndexTypeContext context)
        {
            ITriggerBinding triggerBinding = null;
            ParameterInfo triggerParameter = null;
            ParameterInfo[] parameters = method.Parameters;
            foreach (ParameterInfo parameter in parameters)
            {
                ITriggerBinding possibleTriggerBinding = _triggerBindingProvider.TryCreate(new TriggerBindingProviderContext
                {
                    Parameter = parameter,
                    NameResolver = context != null && context.Config != null ? context.Config.NameResolver : _nameResolver,
                    StorageAccount = context != null ? context.StorageAccount : null,
                    ServiceBusConnectionString = context != null ? context.ServiceBusConnectionString : null
                });

                if (possibleTriggerBinding != null)
                {
                    if (triggerBinding == null)
                    {
                        triggerBinding = possibleTriggerBinding;
                        triggerParameter = parameter;
                    }
                    else
                    {
                        throw new InvalidOperationException("More than one trigger per function is not allowed.");
                    }
                }
            }

            Dictionary<string, IBinding> nonTriggerBindings = new Dictionary<string, IBinding>();
            IReadOnlyDictionary<string, Type> bindingDataContract;

            if (triggerBinding != null)
            {
                bindingDataContract = triggerBinding.BindingDataContract;
            }
            else
            {
                bindingDataContract = null;
            }

            foreach (ParameterInfo parameter in parameters)
            {
                if (parameter == triggerParameter)
                {
                    continue;
                }

                IBinding binding = _bindingProvider.TryCreate(new BindingProviderContext
                {
                    Parameter = parameter,
                    NameResolver = context != null && context.Config != null ? context.Config.NameResolver : _nameResolver,
                    BindingDataContract = bindingDataContract,
                    StorageAccount = context != null ? context.StorageAccount : null,
                    ServiceBusConnectionString = context != null ? context.ServiceBusConnectionString : null
                });

                if (binding == null)
                {
                    if (triggerBinding != null)
                    {
                        // throw new InvalidOperationException("Cannot bind parameter '" + parameter.Name + "' when using this trigger.");
                        // Until all bindings are migrated, skip extra parameters.
                        continue;
                    }
                    else
                    {
                        // Host.Call-only parameter
                        continue;
                    }
                }

                nonTriggerBindings.Add(parameter.Name, binding);
            }

            string triggerParameterName = triggerParameter != null ? triggerParameter.Name : null;

            return new FunctionDefinition
            {
                TriggerParameterName = triggerParameterName,
                TriggerBinding = triggerBinding,
                NonTriggerBindings = nonTriggerBindings
            };
        }

        // Do static checking on parameter bindings. 
        // Throw if we detect an error. 
        private static void ValidateParameters(ParameterStaticBinding[] parameterBindings, ParameterInfo[] parameters, IConfiguration config)
        {
            for (int i = 0; i < parameterBindings.Length; i++)
            {
                var binding = parameterBindings[i];
                var param = parameters[i];
                binding.Validate(config, param);
            }
        }

        private static void Validate(FunctionDefinition index)
        {
            // $$$ This should share policy code with Orchestrator where it builds the listening map. 

            // Throw on multiple ConsoleOutputs
            if (index.Flow.Bindings.OfType<ConsoleOutputParameterStaticBinding>().Count() > 1)
            {
                throw new InvalidOperationException(
                    "Can't have multiple console output TextWriter parameters on a single function.");
            }
        }
    }
}
