using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;
using Microsoft.Azure.Jobs.Host.Queues.Triggers;
using Microsoft.Azure.Jobs.Host.Runners;
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
                new AttributeStaticBindingProvider(),
                new CancellationTokenStaticBindingProvider(),
                new CloudStorageAccountStaticBindingProvider(),
                new BinderStaticBindingProvider(),
                // The console output binder below will handle all remaining TextWriter parameters. It must come after
                // the Attribute binder; otherwise bindings like Do([BlobOutput] TextWriter blob) wouldn't work.
                new ConsoleOutputStaticBindingProvider(),
                new Sdk1CloudStorageAccountStaticBindingProvider()
            };

        private static readonly ITriggerBindingProvider _triggerBindingProvider = new QueueTriggerAttributeBindingProvider();

        private static readonly IBindingProvider _bindingProvider = new CompositeBindingProvider();

        private readonly IFunctionTable _functionTable;

        private readonly INameResolver _nameResolver;

        // Account for where index lives
        public Indexer(IFunctionTable functionTable, INameResolver nameResolver)
        {
            _functionTable = functionTable;
            _nameResolver = nameResolver;
        }

        public static string AzureJobsFileName
        {
            get { return _azureJobsFileName; }
        }

        // If this config is set, use it. 
        public IConfiguration ConfigOverride { get; set; }

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
            if (ConfigOverride != null)
            {
                return new IndexTypeContext
                {
                    Config = ConfigOverride,
                    StorageAccount = storageAccount,
                    ServiceBusConnectionString = serviceBusConnectionString
                };
            }

            var config = new Configuration();

            RunnerProgram.AddDefaultBinders(config);
            RunnerProgram.ApplyHooks(type, config);

            return new IndexTypeContext
            {
                Config = config,
                StorageAccount = storageAccount,
                ServiceBusConnectionString = serviceBusConnectionString
            };
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
        public ParameterStaticBinding[] CreateExplicitBindings(MethodDescriptor descr)
        {
            ParameterInfo[] ps = descr.Parameters;

            ParameterStaticBinding[] flows = Array.ConvertAll(ps, BindParameter);

            // Populate input names
            HashSet<string> paramNames = new HashSet<string>();
            foreach (var flow in flows)
            {
                if (flow != null)
                {
                    paramNames.UnionWith(flow.ProducedRouteParameters);
                }
            }

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
            Indexer idx = new Indexer(null, null);
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

            NoAutomaticTriggerAttribute triggerAttr = null;

            foreach (var attr in descr.MethodAttributes)
            {
                triggerAttr = triggerAttr ?? (attr as NoAutomaticTriggerAttribute);

                var descriptionAttr = attr as DescriptionAttribute;
                if (descriptionAttr != null)
                {
                    description = descriptionAttr.Description;
                }
            }

            // $$$ Lots of other static checks to add.

            // Look at parameters.
            ParameterStaticBinding[] parameterBindings = CreateExplicitBindings(descr);

            bool hasAnyBindings = Array.Find(parameterBindings, x => x != null) != null;
            AddInvokeBindings(descr, parameterBindings);

            //
            // We now have all the explicitly provided information. Put it together.
            //

            // Get trigger:
            // - (default) listen on blobs. Use this if there are flow attributes present.
            // - None - if the [NoAutomaticTriggerAttribute] attribute is present.

            FunctionTrigger trigger;

            if (index.TriggerBinding != null)
            {
                trigger = new FunctionTrigger();
            }
            else if (triggerAttr != null)
            {
                // Explicit [NoTrigger] attribute.
                trigger = new FunctionTrigger(); // no triggers
            }
            else if (hasAnyBindings)
            {
                // Can't tell the difference between unbound parameters and modelbound parameters.
                // Assume any unknonw parameters will be solved with model binding, and that if the user didn't
                // want an invoke, they would have used the [NoTrigger] attribute.
                trigger = new FunctionTrigger { ListenOnBlobs = true };
#if false

                // Unbound parameters mean this can't be automatically invoked.
                // The only reason we listen on blob is for automatic invoke.
                trigger = new FunctionTrigger { ListenOnBlobs = !hasUnboundParams };
#endif
            }
            else if (description != null)
            {
                // Only [Description] attribute, no other binding information.
                trigger = new FunctionTrigger();
            }
            else
            {
                // Still no trigger (not even automatic), then ignore this function completely.
                return null;
            }

            index.Trigger = trigger;
            index.Flow = new FunctionFlow { Bindings = parameterBindings };

            if (context != null)
            {
                ValidateParameters(parameterBindings, descr.Parameters, context.Config);
            }

            Validate(index);

            return index;
        }

        private static FunctionDefinition CreateFunctionDefinition(MethodDescriptor method, IndexTypeContext context)
        {
            ITriggerBinding triggerBinding = null;
            ParameterInfo triggerParameter = null;
            ParameterInfo[] parameters = method.Parameters;
            foreach (ParameterInfo parameter in parameters)
            {
                ITriggerBinding possibleTriggerBinding = _triggerBindingProvider.TryCreate(new TriggerBindingProviderContext
                {
                    Parameter = parameter,
                    NameResolver = context != null ? context.Config.NameResolver : null,
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

            if (index.TriggerBinding != null && index.Trigger.ListenOnBlobs)
            {
                throw new InvalidOperationException("Can't have a trigger and NoAutomaticTrigger on the same function.");
            }

            // Throw on multiple ConsoleOutputs
            if (index.Flow.Bindings.OfType<ConsoleOutputParameterStaticBinding>().Count() > 1)
            {
                throw new InvalidOperationException(
                    "Can't have multiple console output TextWriter parameters on a single function.");
            }
        }
    }
}
