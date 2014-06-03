using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.Cancellation;
using Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput;
using Microsoft.Azure.Jobs.Host.Bindings.Data;
using Microsoft.Azure.Jobs.Host.Bindings.Invoke;
using Microsoft.Azure.Jobs.Host.Bindings.Runtime;
using Microsoft.Azure.Jobs.Host.Bindings.StorageAccount;
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

            innerProviders.Add(new CloudStorageAccountBindingProvider());
            innerProviders.Add(new CancellationTokenBindingProvider());

            // The console output binder below will handle all remaining TextWriter parameters. It must come after the
            // Blob binding provider; otherwise bindings like Do([Blob("a/b")] TextWriter blob) wouldn't work.
            innerProviders.Add(new ConsoleOutputBindingProvider());

            innerProviders.Add(new RuntimeBindingProvider());
            innerProviders.Add(new DataBindingProvider());

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
            DescriptionAttribute description = null;
            NoAutomaticTriggerAttribute noAutomaticTrigger = null;

            foreach (var attr in descr.MethodAttributes)
            {
                description = description ?? (attr as DescriptionAttribute);
                noAutomaticTrigger = noAutomaticTrigger ?? (attr as NoAutomaticTriggerAttribute);
            }

            return CreateFunctionDefinition(descr, context, noAutomaticTrigger != null, description != null);
        }

        private FunctionDefinition CreateFunctionDefinition(MethodDescriptor method, IndexTypeContext context,
            bool hasNoAutomaticTrigger, bool hasDescription)
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

            bool hasNonInvokeBinding = false;

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
                    if (triggerBinding != null && !hasNoAutomaticTrigger)
                    {
                        throw new InvalidOperationException("Cannot bind parameter '" + parameter.Name + "' when using this trigger.");
                    }
                    else
                    {
                        // Host.Call-only parameter
                        binding = InvokeBinding.Create(parameter.Name, parameter.ParameterType);
                    }
                }
                else
                {
                    hasNonInvokeBinding = true;
                }

                nonTriggerBindings.Add(parameter.Name, binding);
            }

            if (triggerBinding == null && !hasNonInvokeBinding && !hasNoAutomaticTrigger && !hasDescription)
            {
                // No trigger, binding (other than invoke binding, which always gets created), NoAutomaticTrigger
                // attribute or Description attribute.
                // Ignore this function completely.
                return null;
            }

            // Validation: prevent multiple ConsoleOutputs
            if (nonTriggerBindings.OfType<ConsoleOutputBinding>().Count() > 1)
            {
                throw new InvalidOperationException(
                    "Can't have multiple console output TextWriter parameters on a single function.");
            }

            string triggerParameterName = triggerParameter != null ? triggerParameter.Name : null;

            return new FunctionDefinition
            {
                TriggerParameterName = triggerParameterName,
                TriggerBinding = triggerBinding,
                NonTriggerBindings = nonTriggerBindings
            };
        }
    }
}
