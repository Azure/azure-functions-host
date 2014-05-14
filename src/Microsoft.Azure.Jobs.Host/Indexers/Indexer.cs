using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders;

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
                new Sdk1CloudStorageAccountStaticBindingProvider()
            };

        private readonly IFunctionTable _functionTable;

        // Account for where index lives
        public Indexer(IFunctionTable functionTable)
        {
            if (functionTable == null)
            {
                throw new ArgumentNullException("functionTable");
            }
            _functionTable = functionTable;
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

        public void IndexType(Func<MethodInfo, FunctionLocation> funcApplyLocation, Type type)
        {
            var context = InvokeInitMethodOnType(type, funcApplyLocation);

            // Now register any declaritive methods
            foreach (MethodInfo method in type.GetMethods(_publicStaticMethodFlags))
            {
                IndexMethod(funcApplyLocation, method, context);
            }

            EnsureNoDuplicateFunctions();
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
        private IndexTypeContext InvokeInitMethodOnType(Type type, Func<MethodInfo, FunctionLocation> funcApplyLocation)
        {
            if (ConfigOverride != null)
            {
                return new IndexTypeContext { Config = ConfigOverride };
            }

            IndexerConfig config = new IndexerConfig();

            RunnerProgram.AddDefaultBinders(config);
            RunnerProgram.ApplyHooks(type, config);

            var context = new IndexTypeContext { Config = config };

            return context;
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
        public static ParameterStaticBinding[] CreateExplicitBindings(MethodDescriptor descr)
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

        private static ParameterStaticBinding BindParameter(ParameterInfo parameter)
        {
            foreach (IStaticBindingProvider provider in _staticBindingProviders)
            {
                ParameterStaticBinding binding = provider.TryBind(parameter);

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

        public static FunctionDefinition GetFunctionDefinition(MethodInfo method, IndexTypeContext context = null)
        {
            MethodDescriptor descr = GetMethodDescriptor(method);
            return GetFunctionDefinition(descr, context);
        }

        // Returns a partially instantiated FunctionIndexEntity.
        // Caller must add Location information.
        public static FunctionDefinition GetFunctionDefinition(MethodDescriptor descr, IndexTypeContext context)
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

        private static FunctionDefinition GetDescriptionForMethodInternal(MethodDescriptor descr, IndexTypeContext context)
        {
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

            if (triggerAttr != null)
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

            FunctionDefinition index = new FunctionDefinition
            {
                Trigger = trigger,
                Flow = new FunctionFlow
                {
                    Bindings = parameterBindings
                }
            };

            if (context != null)
            {
                ValidateParameters(parameterBindings, descr.Parameters, context.Config);
            }

            Validate(index);

            return index;
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

            // Throw on multiple QueueInputs
            int qc = 0;
            foreach (ParameterStaticBinding flow in index.Flow.Bindings)
            {
                var q = flow as QueueParameterStaticBinding;
                if (q != null && q.IsInput)
                {
                    qc++;
                }
            }

            if (qc > 1)
            {
                string msg = string.Format("Can't have multiple QueueInput parameters on a single function.");
                throw new InvalidOperationException(msg);
            }

            if (qc > 0)
            {
                if (!(index.Flow.Bindings[0] is QueueParameterStaticBinding))
                {
                    throw new InvalidOperationException("A QueueInput parameter must be the first parameter.");
                }

                if (!index.Trigger.ListenOnBlobs)
                {
                    // This implies a [NoAutomaticTrigger] attribute. 
                    throw new InvalidOperationException("Can't have QueueInput and NoAutomaticTrigger on the same function.");
                }
            }
        }
    }
}
