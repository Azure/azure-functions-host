using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerHost;
using RunnerInterfaces;
using SimpleBatch;

namespace Orchestrator
{
    // Abstraction over a MethodInfo so that we can bind from either 
    // attributes or code-config.
    public class MethodDescriptor
    {
        public string Name; 
        public Attribute[] MethodAttributes;
        public ParameterInfo[] Parameters;
    }

    // Go down and build an index
    public class Indexer
    {
        private readonly IIndexerSettings _settings;

        // Account for where index lives 
        public Indexer(IIndexerSettings settings)
        {
            _settings = settings;
        }

        // !!! Should never be calling this. Nukes the whole table!!
        // $$$ Move this somewhere else. Indexer is just writing to the location. 
        public void CleanFunctionIndex()
        {
            _settings.CleanFunctionIndex();
        }

        // Index all things in the container 
        // account - account that binderLookupTable paths resolve to. ($$$ move account info int ot he table too?)
        public void IndexContainer(CloudBlobDescriptor containerDescriptor, string localCacheRoot, IAzureTableReader<BinderEntry> binderLookupTable)
        {
            // Locally copy 
            using (var helper = new ContainerDownloader(containerDescriptor, localCacheRoot, uploadNewFiles : true))
            {
                string localCache = helper.LocalCachePrivate;

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

        private void RemoveStaleFunctions(CloudBlobDescriptor containerDescriptor, string localCache)
        {
            FunctionIndexEntity[] funcs = _settings.ReadFunctionTable();

            string connection = containerDescriptor.AccountConnectionString;
            string containerName = containerDescriptor.ContainerName;

            foreach (var file in Directory.EnumerateFiles(localCache))
            {
                string name = Path.GetFileName(file);

                foreach (FunctionIndexEntity func in funcs)
                {
                    var loc = func.Location;
                    if (loc.Blob.BlobName == name && loc.Blob.ContainerName == containerName && loc.Blob.AccountConnectionString == connection)
                    {
                        _settings.Delete(func);
                    }
                }
            }
        }

        // AssemblyName doesn't implement GetHashCode().
        Dictionary<AssemblyName, string> _fileLocations = new Dictionary<AssemblyName, string>(new AssemblyNameComparer());

        Assembly CurrentDomain_ReflectionOnlyAssemblyResolve(object sender, ResolveEventArgs args)
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

        HashSet<Type> _binderTypes = new HashSet<Type>();

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
        
        private void IndexLocalDirWorker(Func<MethodInfo, FunctionLocation> funcApplyLocation, string localDirectory)
        {                        
            var filesDll = Directory.EnumerateFiles(localDirectory, "*.dll");
            var filesExe = Directory.EnumerateFiles(localDirectory, "*.exe");

            
            foreach (string file in filesExe.Concat(filesDll))
            {
                //string name = Path.GetFileNameWithoutExtension(file);
                var name = AssemblyName.GetAssemblyName(file);
                _fileLocations[name] = file;
            }

            foreach (string file in filesExe.Concat(filesDll))
            {
                Assembly a = Assembly.LoadFrom(file);
                if (string.Compare(a.Location, file, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    // $$$
                    // Stupid loader, loaded the assembly from the wrong spot.
                    // This is important when an assembly has been updated and recompiled, 
                    // but it still has the same identity, and so the loader foolishly pulls the old
                    // assembly.
                    // This goes away when the process is recycled. 
                    // Get a warning now so that we don't have subtle bugs from processing the wrong assembly.
                    throw new InvalidOperationException("CLR Loaded assembly from wrong spot");
                }


                // The hosts and binders are IL-only and running in 64-bit environments. 
                // So the entry point can't require 32-bit. 
                {
                    var mainModule = a.GetLoadedModules(false)[0];
                    PortableExecutableKinds peKind;
                    ImageFileMachine machine;
                    mainModule.GetPEKind(out peKind, out machine);
                    if (peKind != PortableExecutableKinds.ILOnly)
                    {                        
                        throw new InvalidOperationException("Indexing must be in IL-only entry points.");
                    }
                }


                IndexAssembly(funcApplyLocation, a);
            }
        }

        public void IndexAssembly(Func<MethodInfo, FunctionLocation> funcApplyLocation, Assembly a)
        {
            // Only try to index assemblies that reference SimpleBatch.
            // This avoids trying to index through a bunch of FX assemblies that reflection may not be able to load anyways.
            {
                bool skip = true;
                var names = a.GetReferencedAssemblies();
                foreach (var name in names)
                {
                    if (string.Compare(name.Name, "SimpleBatch", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        skip = false;
                        break;
                    }
                }

                if (skip)
                {
                    return;
                }
            }

            foreach (var type in a.GetTypes())
            {
                IndexType(funcApplyLocation, type);
            }
        }


        static BindingFlags MethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

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
            InvokeInitMethodOnType(type, funcApplyLocation);

            // Now register any declaritive methods 
            foreach (MethodInfo method in type.GetMethods(MethodFlags))
            {
                IndexMethod(funcApplyLocation, method);
            }
        }
                
        // Invoke the Initialize(IConfiguration) hook on a type in the assembly we're indexing.
        // Register any functions provided by code-configuration. 
        void InvokeInitMethodOnType(Type type, Func<MethodInfo, FunctionLocation> funcApplyLocation)
        {
            // Invoke initialization function on this type.
            // This may register functions imperatively. 
            Func<string, MethodInfo> fpFuncLookup = name => ResolveMethod(type, name);
            IndexerConfig config = new IndexerConfig(fpFuncLookup);

            RunnerHost.Program.ApplyHooks(type, config);
            

            Func<MethodDescriptor, FunctionLocation> funcApplyLocation2 = Convert(fpFuncLookup, funcApplyLocation);                

            foreach (var descr in config.GetRegisteredMethods())
            {
                IndexMethod(funcApplyLocation2, descr);
            }
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
            
            // This is effectively serializing out a MethodInfo.
            return new FunctionLocation
            {
                Blob = new CloudBlobDescriptor
                {
                     AccountConnectionString = container.AccountConnectionString,
                     ContainerName = container.ContainerName,
                     BlobName = Path.GetFileName(type.Assembly.Location)
                },
                MethodName = method.Name,
                TypeName = type.FullName
            };
        }

        // Entry-point from reflection-based configuration. This is looking at inline attributes.
        public void IndexMethod(Func<MethodInfo, FunctionLocation> funcApplyLocation, MethodInfo method)
        {
            MethodDescriptor descr = GetFromMethod(method);

            Func<string, MethodInfo> fpFuncLookup = name => ResolveMethod(method.DeclaringType, name);
            Func<MethodDescriptor, FunctionLocation> funcApplyLocation2 = Convert(fpFuncLookup, funcApplyLocation);
            
            IndexMethod(funcApplyLocation2, descr);
        }

        // Container is where the method lived on the cloud. 
        // Common path for both attribute-cased and code-based configuration. 
        public void IndexMethod(Func<MethodDescriptor, FunctionLocation> funcApplyLocation, MethodDescriptor descr)
        {
            FunctionIndexEntity index = GetDescriptionForMethod(descr);
            if (index != null)
            {
                FunctionLocation loc = funcApplyLocation(descr);
                index.Location = loc;
                index.SetRowKey(); // may use location info

                _settings.Add(index);

                // Add custom binders for parameter types
                foreach (var parameter in descr.Parameters)
                {
                    var t = parameter.ParameterType;
                    MaybeAddBinderType(t);
                }
            }            
        }    

        // Determine if we should check for a custom binder for the given type.
        void MaybeAddBinderType(Type type)
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
                        
            ParameterStaticBinding[] flows = Array.ConvertAll(ps,  BindParameter);

            // Populate input names
            HashSet<string> paramNames = new HashSet<string>();
            foreach (var flow in flows)
            {
                if (flow != null)
                {
                    paramNames.UnionWith(flow.ProducedRouteParameters);
                }
            }

            // Take a second pass to bind params diretly to {key} in the attributes above,.
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

        // Note any remaining unbound parameters must be provided by the user. 
        // Return true if any parameters were unbound. Else false.
        public static bool MarkUnboundParameters(MethodDescriptor descr, ParameterStaticBinding[] flows)
        {
            ParameterInfo[] ps = descr.Parameters;

            bool hasUnboundParams = false;
            for(int i = 0; i < flows.Length; i++)
            {
                if (flows[i] == null)
                {
                    string name = ps[i].Name;
                    flows[i] = new NameParameterStaticBinding { KeyName = name, Name = name,  UserSupplied = true };
                    hasUnboundParams = true;
                }
            }
            return hasUnboundParams;
        }

        static MethodDescriptor GetFromMethod(MethodInfo method)
        {
            var descr = new MethodDescriptor();
            descr.Name = method.Name;
            descr.MethodAttributes = Array.ConvertAll(method.GetCustomAttributes(true), attr => (Attribute) attr);
            descr.Parameters = method.GetParameters();

            return descr;           
        }


        public static FunctionIndexEntity GetDescriptionForMethod(MethodInfo method)
        {
            MethodDescriptor descr = GetFromMethod(method);
            return GetDescriptionForMethod(descr);
        }

        // Returns a partially instantiated FunctionIndexEntity. 
        // Caller must add Location information. 
        public static FunctionIndexEntity GetDescriptionForMethod(MethodDescriptor descr)
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
                trigger = new FunctionTrigger { TimerInterval = interval.ToString(), ListenOnBlobs = false };
            }
            else if (triggerAttr != null)
            {
                // Explicit [NoTrigger] attribute.
                trigger = new FunctionTrigger(); // no triggers
            }
            else if (hasAnyBindings)
            {
                // Can't tell teh difference between unbound parameters and modelbound parameters.
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

            FunctionIndexEntity index = new FunctionIndexEntity
            {
                Description = description,
                Trigger = trigger,
                Flow = new FunctionFlow
                {
                    Bindings = parameterBindings                    
                }
            };
            
            return index;
        }
    }
}
