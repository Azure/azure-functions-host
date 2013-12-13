using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;

namespace Orchestrator
{
    // Abstraction over a MethodInfo so that we can bind from either
    // attributes or code-config.
    internal class MethodDescriptor
    {
        public string Name;
        public Attribute[] MethodAttributes;
        public ParameterInfo[] Parameters;
    }

    // Context, speciifc to a given type. 
    // Each type can provide its own configuration
    internal class IndexTypeContext
    {
        public IConfiguration Config { get; set; }
    }

    // Go down and build an index
    internal class Indexer
    {
        private readonly IFunctionTable _functionTable;

        // If this config is set, use it. 
        public IConfiguration _configOverride;

        // Account for where index lives
        public Indexer(IFunctionTable functionTable)
        {
            if (functionTable == null)
            {
                throw new ArgumentNullException("functionTable");
            }
            _functionTable = functionTable;
        }

        // Index all things in the container
        // account - account that binderLookupTable paths resolve to. ($$$ move account info int ot he table too?)
        public void IndexContainer(CloudBlobDescriptor containerDescriptor, string localCacheRoot, IAzureTableReader<BinderEntry> binderLookupTable)
        {
            // Locally copy
            using (var helper = new ContainerDownloader(containerDescriptor, localCacheRoot, uploadNewFiles: true))
            {
                string localCache = helper.LocalCachePrivate;

                {
                    // Delete the user's instance of SimpleBatch just to force it to bind against the host instance.
                    string path = Path.Combine(localCache, "SimpleBatch.dll");
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                BinderLookup binderLookup = new BinderLookup(binderLookupTable, localCache);

                RemoveStaleFunctions(containerDescriptor, localCache);

                // Copying funcs out of a container, apply locations to that container.
                Func<MethodInfo, FunctionLocation> funcApplyLocation = method => GetLocationInfoFromContainer(containerDescriptor, method);

                IndexLocalDir(funcApplyLocation, localCache);

                CopyCloudModelBinders(binderLookup);
            }
        }

        private void CopyCloudModelBinders(BinderLookup b)
        {
            int countCustom = 0;

            var types = _binderTypes;

            bool first = true;

            foreach (var t in types)
            {
                bool found = b.Lookup(t);

                if (found)
                {
                    if (first)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Using custom binders:");
                        first = false;
                    }

                    Console.WriteLine("  {0}", t.FullName);
                    countCustom++;
                }
            }

            if (countCustom > 0)
            {
                Console.WriteLine();
                b.WriteManifest("manifest.txt");
            }
        }

        // Remove any functions that are in the container. 
        private void RemoveStaleFunctions(CloudBlobDescriptor containerDescriptor, string localCache)
        {
            FunctionDefinition[] funcs = _functionTable.ReadAll();

            string connection = containerDescriptor.AccountConnectionString;
            string containerName = containerDescriptor.ContainerName;

            foreach (var file in Directory.EnumerateFiles(localCache))
            {
                string name = Path.GetFileName(file);

                foreach (FunctionDefinition func in funcs)
                {
                    var loc = func.Location as RemoteFunctionLocation;
                    if (loc != null)
                    {
                        if ((loc.DownloadSource.BlobName == name) && (loc.DownloadSource.ContainerName == containerName) && (loc.AccountConnectionString == connection))
                        {
                            _functionTable.Delete(func);
                        }
                    }
                }
            }
        }

        // AssemblyName doesn't implement GetHashCode().
        private Dictionary<AssemblyName, string> _fileLocations = new Dictionary<AssemblyName, string>(new AssemblyNameComparer());

        private Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Name is "SimpleBatch, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"

            var name = new AssemblyName(args.Name);

            string file;
            if (_fileLocations.TryGetValue(name, out file))
            {
                return Assembly.LoadFrom(file);
            }
            else
            {
                return Assembly.Load(args.Name);
            }
        }

        private HashSet<Type> _binderTypes = new HashSet<Type>();

        // Look at each assembly
        public void IndexLocalDir(Func<MethodInfo, FunctionLocation> funcApplyLocation, string localDirectory)
        {
            // See http://blogs.msdn.com/b/jmstall/archive/2006/11/22/reflection-type-load-exception.aspx

            // Use live loading (not just reflection-only) so that we can invoke teh Initialization method.
            var handler = new ResolveEventHandler(CurrentDomain_ReflectionOnlyAssemblyResolve);
            AppDomain.CurrentDomain.AssemblyResolve += handler;
            try
            {
                IndexLocalDirWorker(funcApplyLocation, localDirectory);
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= handler;
            }
        }

        private string[] GetFileList(string localDirectory)
        {
            var filesDll = Directory.EnumerateFiles(localDirectory, "*.dll");
            var filesExe = Directory.EnumerateFiles(localDirectory, "*.exe");
                        
            List<string> list = new List<string>();
            foreach (var file in filesExe.Concat(filesDll))
            {
                // Omit anything with .vshost.exe:
                // 1. It won't have SB functions anyways.
                // 2. we often can't index it, so it produces noisy errors even trying
                // 3. they all have the same AssemblyName, and so trying to load can produce naming collisions. 
                if (file.EndsWith(".vshost.exe", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                list.Add(file);
            }
            return list.ToArray();
        }

        [DebuggerNonUserCode]
        private AssemblyName GetAssemblyName(string file)
        {
            try
            {
                var name = AssemblyName.GetAssemblyName(file);
                return name;
            }
            catch (BadImageFormatException)
            {
                // Can happen normally for Native dlls. 
                return null;
            }
        }

        private void IndexLocalDirWorker(Func<MethodInfo, FunctionLocation> funcApplyLocation, string localDirectory)
        {
            string[] fileList = GetFileList(localDirectory);

            foreach (string file in fileList)
            {
                var name = GetAssemblyName(file);
                if (name != null)
                {
                    _fileLocations[name] = file;
                }
            }

            foreach (string file in fileList)
            {
                Assembly a = null;
                try
                {
                    a = Assembly.LoadFrom(file);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("Warning: The assembly '{0}' has been skipped. This is ok if there are no simplebatch entry point functions in that file.  Exception Type: '{1}', Exception Message: {2}", file, exception.GetType(), exception.Message);
                    continue;
                }

                if (a != null && string.Compare(a.Location, file, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // $$$
                    // Stupid loader, loaded the assembly from the wrong spot.
                    // This is important when an assembly has been updated and recompiled,
                    // but it still has the same identity, and so the loader foolishly pulls the old
                    // assembly.
                    // This goes away when the process is recycled.
                    // Get a warning now so that we don't have subtle bugs from processing the wrong assembly.
                    bool isGacDll = a.Location.Contains(@"\GAC_MSIL\");

                    if (isGacDll)
                    {
                        // Stupid loader will forcibly resolve dlls against the GAC. That's ok here since GAC
                        // dlls are framework and won't contain user code and so can't be simple batch dlls.
                        // So don't need to even index them.
                        continue;
                    }

                    // One way this can happen is if 2 assemblies have the same assembly name but different filenames.
                    // The loader will match on assembly name and reuse. 
                    // This is the case with Vshost.exe. 
                    string msg = string.Format("CLR loaded wrong assembly. Tried to load {0} but actually loaded {1}.", file, a.Location);
                    throw new InvalidOperationException(msg);
                }

                // The hosts and binders are IL-only and running in 64-bit environments.
                // So the entry point can't require 32-bit.
                {
                    var mainModule = a.GetLoadedModules(false)[0];
                    PortableExecutableKinds peKind;
                    ImageFileMachine machine;
                    mainModule.GetPEKind(out peKind, out machine);

                    // Net.45 adds new flags for preference, which can be a superset of IL Only.
                    if ((peKind & PortableExecutableKinds.ILOnly) != PortableExecutableKinds.ILOnly)
                    {
                        throw new InvalidOperationException("Indexing must be in IL-only entry points.");
                    }
                }

                IndexAssembly(funcApplyLocation, a);
            }
        }

        public static bool DoesAssemblyReferenceSimpleBatch(Assembly a)
        {
            var names = a.GetReferencedAssemblies();
            foreach (var name in names)
            {
                if (string.Compare(name.Name, "SimpleBatch", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void IndexAssembly(Func<MethodInfo, FunctionLocation> funcApplyLocation, Assembly a)
        {
            // Only try to index assemblies that reference SimpleBatch.
            // This avoids trying to index through a bunch of FX assemblies that reflection may not be able to load anyways.
            bool skip = !DoesAssemblyReferenceSimpleBatch(a);
            if (skip)
            {
                return;
            }            

            Type[] types;

            try
            {
                types = a.GetTypes();
            }
            catch
            {
                // This is bad. The assembly refers to SimpleBatch.dll, so it ought to be indexable.
                // But we can't read the types.
                // This could be because it refers to a stale/corrupted version of SimpleBatch.dll (or maybe 
                // even a dll that has the same name but is totally different).
                Console.WriteLine("Warning: Failed to get types from assembly: {0}", a.FullName);
                return;
            }

            foreach (var type in types)
            {
                IndexType(funcApplyLocation, type);
            }
        }

        private static BindingFlags MethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static MethodInfo ResolveMethod(Type type, string name)
        {
            var method = type.GetMethod(name, MethodFlags);
            if (method == null)
            {
                string msg = string.Format("No method '{0}' found on type '{1}'", name, type.FullName);
                throw new InvalidOperationException(msg);
            }
            return method;
        }

        public void IndexType(Func<MethodInfo, FunctionLocation> funcApplyLocation, Type type)
        {
            var context = InvokeInitMethodOnType(type, funcApplyLocation);

            // Now register any declaritive methods
            foreach (MethodInfo method in type.GetMethods(MethodFlags))
            {
                IndexMethod(funcApplyLocation, method, context);
            }
            
            CheckDups();            
        }

        // Check for duplicate names. Indexing doesn't support overloads.
        void CheckDups()
        {
            HashSet<string> locs = new HashSet<string>();

            foreach (var func in _functionTable.ReadAll())
            {
                var key = func.Location.ToString();
                if (!locs.Add(key))
                {
                    // Dup found!
                    string msg = string.Format("SimpleBatch doesn't support function overloads. There are multiple overloads for: {0}", key);
                    throw new InvalidOperationException(msg);
                }
            }
        }

        // Invoke the Initialize(IConfiguration) hook on a type in the assembly we're indexing.
        // Register any functions provided by code-configuration.
        private IndexTypeContext InvokeInitMethodOnType(Type type, Func<MethodInfo, FunctionLocation> funcApplyLocation)
        {
            if (_configOverride != null)
            {
                return new IndexTypeContext { Config = _configOverride };
            }

            // Invoke initialization function on this type.
            // This may register functions imperatively.
            Func<string, MethodInfo> fpFuncLookup = name => ResolveMethod(type, name);

            IndexerConfig config = new IndexerConfig(fpFuncLookup);
            
            RunnerHost.Program.AddDefaultBinders(config);
            RunnerHost.Program.ApplyHooks(type, config);

            var context = new IndexTypeContext { Config = config };

            Func<MethodDescriptor, FunctionLocation> funcApplyLocation2 = Convert(fpFuncLookup, funcApplyLocation);


            foreach (var descr in config.GetRegisteredMethods())
            {
                IndexMethod(funcApplyLocation2, descr, context);
            }
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

        // Default policy, assumes that the function originally came from the container.
        private FunctionLocation GetLocationInfoFromContainer(CloudBlobDescriptor container, MethodInfo method)
        {
            Type type = method.DeclaringType;


            string containerName = container.ContainerName;
            string blobName = Path.GetFileName(type.Assembly.Location);

            // This is effectively serializing out a MethodInfo.
            return new RemoteFunctionLocation
            {
                AccountConnectionString = container.AccountConnectionString,
                DownloadSource = new CloudBlobPath(containerName, blobName),                
                MethodName = method.Name,
                TypeName = type.FullName
            };
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
            catch (Exception  e)
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

                var timerAttr = attr as TimerAttribute;
                if (timerAttr != null)
                {
                    interval = timerAttr.TimeSpan;
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
            if (interval.HasValue)
            {
                // Timer supercedes listening on blobs
                trigger = new FunctionTrigger { TimerInterval = interval.Value, ListenOnBlobs = false };
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
            for(int i = 0; i < parameterBindings.Length; i++)
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
            foreach (var flow in index.Flow.Bindings)
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