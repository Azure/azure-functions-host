using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Go down and build an index
    internal class Indexer
    {
        private static readonly BindingFlags _publicStaticMethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static readonly string _azureJobsAssemblyName = typeof(TableAttribute).Assembly.GetName().Name;
        private static readonly string _azureJobsFileName = typeof(TableAttribute).Assembly.ManifestModule.Name;

        private readonly IFunctionTable _functionTable;

        private HashSet<Type> _binderTypes = new HashSet<Type>();

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

        public static bool DoesAssemblyReferenceAzureJobs(Assembly a)
        {
            AssemblyName[] referencedAssemblyNames = a.GetReferencedAssemblies();
            foreach (var referencedAssemblyName in referencedAssemblyNames)
            {
                if (String.Equals(referencedAssemblyName.Name, _azureJobsAssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public void IndexAssembly(Func<MethodInfo, FunctionLocation> funcApplyLocation, Assembly a)
        {
            // Only try to index assemblies that reference Azure Jobs.
            // This avoids trying to index through a bunch of FX assemblies that reflection may not be able to load anyways.
            if (!DoesAssemblyReferenceAzureJobs(a))
            {
                return;
            }

            Type[] types = null;

            try
            {
                types = a.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // TODO: Log this somewhere?
                Console.WriteLine("Warning: Only got partial types from assembly: {0}", a.FullName);
                Console.WriteLine("Exception message: {0}", ex.ToString());

                // In case of a type load exception, at least get the types that did succeed in loading
                types = ex.Types;
            }
            catch (Exception ex)
            {
                // TODO: Log this somewhere?
                Console.WriteLine("Warning: Failed to get types from assembly: {0}", a.FullName);
                Console.WriteLine("Exception message: {0}", ex.ToString());
            }

            if (types != null)
            {
                foreach (var type in types)
                {
                    IndexType(funcApplyLocation, type);
                }
            }
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
        public void IndexMethod(Func<MethodInfo, FunctionLocation> funcApplyLocation, MethodInfo method, IndexTypeContext context = null)
        {
            MethodDescriptor descr = GetFromMethod(method);

            Func<string, MethodInfo> fpFuncLookup = name => ResolveMethod(method.DeclaringType, name);
            Func<MethodDescriptor, FunctionLocation> funcApplyLocation2 = Convert(fpFuncLookup, funcApplyLocation);

            IndexMethod(funcApplyLocation2, descr, context);
        }

        // Container is where the method lived on the cloud.
        // Common path for both attribute-cased and code-based configuration.
        public void IndexMethod(Func<MethodDescriptor, FunctionLocation> funcApplyLocation, MethodDescriptor descr, IndexTypeContext context = null)
        {
            FunctionDefinition index = GetDescriptionForMethod(descr, context);
            if (index != null)
            {
                FunctionLocation loc = funcApplyLocation(descr);
                index.Location = loc;

                _functionTable.Add(index);

                // Add custom binders for parameter types
                foreach (var parameter in descr.Parameters)
                {
                    var t = parameter.ParameterType;
                    MaybeAddBinderType(t);
                }
            }
        }

        // Determine if we should check for a custom binder for the given type.
        private void MaybeAddBinderType(Type type)
        {
            if (type.IsPrimitive || type == typeof(string))
            {
                return;
            }
            if (type.IsByRef)
            {
                // T& --> T
                MaybeAddBinderType(type.GetElementType());
                return;
            }

            _binderTypes.Add(type);
        }

        // Get any bindings that can be explicitly deduced.
        // This always returns a non-null array, but array may have null elements
        // for bindings that can't be determined.
        // - either those bindings are user supplied parameters (which means this function
        //   can't be invoked by an automatic trigger)
        // - or the function shouldn't be indexed at all.
        // Caller will make that distinction.
        public static ParameterStaticBinding[] GetExplicitBindings(MethodDescriptor descr)
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
            // So if we have p1 with attr [BlobInput(@"daas-test-input2\{name}.csv")],
            // then we'll bind 'string name' to the {name} value.
            int pos = 0;
            foreach (ParameterInfo p in ps)
            {
                if (paramNames.Contains(p.Name))
                {
                    flows[pos] = new NameParameterStaticBinding { KeyName = p.Name, Name = p.Name };
                }
                pos++;
            }

            return flows;
        }

        private static ParameterStaticBinding BindParameter(ParameterInfo parameter)
        {
            try
            {
                foreach (Attribute attr in parameter.GetCustomAttributes(true))
                {
                    var bind = StaticBinder.DoStaticBind(attr, parameter);
                    if (bind != null)
                    {
                        return bind;
                    }
                }
                return null;
            }
            catch (Exception e)
            {
                throw IndexException.NewParameter(parameter, e);
            }
        }

        // Note any remaining unbound parameters must be provided by the user.
        // Return true if any parameters were unbound. Else false.
        public static bool MarkUnboundParameters(MethodDescriptor descr, ParameterStaticBinding[] flows)
        {
            ParameterInfo[] ps = descr.Parameters;

            bool hasUnboundParams = false;
            for (int i = 0; i < flows.Length; i++)
            {
                if (flows[i] == null)
                {
                    string name = ps[i].Name;
                    flows[i] = new NameParameterStaticBinding { KeyName = name, Name = name, UserSupplied = true };
                    hasUnboundParams = true;
                }
            }
            return hasUnboundParams;
        }

        private static MethodDescriptor GetFromMethod(MethodInfo method)
        {
            var descr = new MethodDescriptor();
            descr.Name = method.Name;
            descr.MethodAttributes = Array.ConvertAll(method.GetCustomAttributes(true), attr => (Attribute)attr);
            descr.Parameters = method.GetParameters();

            return descr;
        }

        public static FunctionDefinition GetDescriptionForMethod(MethodInfo method, IndexTypeContext context = null)
        {
            MethodDescriptor descr = GetFromMethod(method);
            return GetDescriptionForMethod(descr, context);
        }

        // Returns a partially instantiated FunctionIndexEntity.
        // Caller must add Location information.
        public static FunctionDefinition GetDescriptionForMethod(MethodDescriptor descr, IndexTypeContext context = null)
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
            TimeSpan? interval = null;

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
            if ((triggerAttr != null) && (interval.HasValue))
            {
                throw new InvalidOperationException("Illegal trigger binding. Can't have both timer and notrigger attributes");
            }

            // Look at parameters.
            bool required = interval.HasValue;

            ParameterStaticBinding[] parameterBindings = GetExplicitBindings(descr);

            bool hasAnyBindings = Array.Find(parameterBindings, x => x != null) != null;
            bool hasUnboundParams = MarkUnboundParameters(descr, parameterBindings);

            //
            // We now have all the explicitly provided information. Put it together.
            //

            // Get trigger:
            // - (default) listen on blobs. Use this if there are flow attributes present.
            // - Timer
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
                Description = description,
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
                string msg = string.Format("Can't have multiple QueueInputs on a single function definition");
                throw new InvalidOperationException(msg);
            }

            if (qc > 0)
            {
                if (!(index.Flow.Bindings[0] is QueueParameterStaticBinding))
                {
                    throw new InvalidOperationException("A QueueInput parameter must be the first parameter.");
                }

                if (index.Trigger.TimerInterval.HasValue)
                {
                    throw new InvalidOperationException("Can't have a QueueInput and Timer triggers on the same function ");
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
