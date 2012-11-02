using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Bind to IBinder
    // All return results get tracked on a cleanup list. 
    public class BinderBinderProvider : ICloudBinderProvider
    {
        // Wrap an IBinder to:
        // - ensure we cleanup all BindResults we hand out
        // - impl ISelfWatch so we can see all results we've handed out. 
        class BinderWrapper : IBinder, ISelfWatch
        {
            // Track for cleanup
            private readonly List<BindResult> _results = new List<BindResult>();
            private readonly IBinder _inner;

            class Watches
            {
                public string Name;                
                public ISelfWatch Watch;
            }
            private readonly List<Watches> _watches = new List<Watches>();

            public BinderWrapper(IBinder inner)
            {
                _inner = inner;
            }

            public BindResult<T> Bind<T>(Attribute a)
            {
                var result = _inner.Bind<T>(a);

                // For selfwatch 
                Watches w = new Watches
                {
                    Name = a.ToString(),
                    Watch = Program.GetWatcher(result, typeof(T))
                };
                lock (_watches)
                {
                    _watches.Add(w);
                }

                _results.Add(result);
                return result;
            }

            public string AccountConnectionString
            {
                get { return _inner.AccountConnectionString; }
            }

            public void Cleanup()
            { 
                foreach(var result in _results)
                {
                    result.OnPostAction();
                }
            }

            public string GetStatus()
            {
                lock (_watches)
                {
                    // Show selfwatch from objects we've handed out. 
                    StringBuilder sb = new StringBuilder();
                    foreach (var result in _watches)
                    {
                        sb.Append(result.Name);
                        if (result.Watch != null)
                        {
                            sb.Append(" ");
                            sb.Append(result.Watch.GetStatus());
                        }
                        sb.Append('.');
                        sb.AppendLine();
                    }
                    return sb.ToString();
                }                
            }
        }

        class BinderBinder : ICloudBinder
        {
            public BindResult Bind(IBinder bindingContext, ParameterInfo parameter)
            {
                var wrapper = new BinderWrapper(bindingContext);
                return new BindResult<IBinder>(wrapper)
                {
                    Cleanup = _ => wrapper.Cleanup()
                };
            }
        }

        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(IBinder))
            {
                return new BinderBinder(); 
            }
            return null;
        }
    }


    class BindingContext : IBinder
    {
        private string _accountConnectionString;
        private IConfiguration _config;
        public BindingContext(IConfiguration config, string accountConnectionString)
        {
            _config = config;
            _accountConnectionString = accountConnectionString;
        }

        // ### This is effectively re-running what the orchestrator did with static bindings!
        // Beware, attribute may be in a different assembly
        public BindResult<T> Bind<T>(Attribute a)
        {
            string name = a.GetType().FullName;
            dynamic dynAttr = a; // use dynamic dispatch to cross assembly boundaries

            if (name == typeof(TableAttribute).FullName)
            {               
                var t = new TableParameterRuntimeBinding
                {
                    Table = new CloudTableDescriptor
                        {
                             AccountConnectionString = this._accountConnectionString,
                              TableName = dynAttr.TableName
                        }
                };
                var bind = t.Bind(_config, typeof(T));
                return Utility.StrongWrapper<T>(bind);                
            }

            if (name == typeof(BlobInputAttribute).FullName)
            {
                var path = new CloudBlobPath(dynAttr.ContainerName);
                var runtimeBinding = new BlobParameterRuntimeBinding
                {
                    Blob = new CloudBlobDescriptor
                    {
                         AccountConnectionString = this._accountConnectionString,
                         ContainerName = path.ContainerName,
                         BlobName = path.BlobName
                    }
                };
                var bind = runtimeBinding.Bind(_config, this, typeof(T), input : true);
                return Utility.StrongWrapper<T>(bind);                
            }

            if (name == typeof(BlobOutputAttribute).FullName)
            {
                var path = new CloudBlobPath(dynAttr.ContainerName);
                var runtimeBinding = new BlobParameterRuntimeBinding
                {
                    Blob = new CloudBlobDescriptor
                    {
                        AccountConnectionString = this._accountConnectionString,
                        ContainerName = path.ContainerName,
                        BlobName = path.BlobName
                    }
                };
                var bind = runtimeBinding.Bind(_config, this, typeof(T), input: false);
                return Utility.StrongWrapper<T>(bind);
            }

            throw new InvalidOperationException("Can't bind");
        }

        public string AccountConnectionString
        {
            get { return _accountConnectionString; }
        }
    }


    public class CollisionDetector
    {
        // ### We don't have the Static binders...
        // Throw if binds read and write to the same resource. 
        public static void DetectCollisions(BindResult[] binds)
        {
        }
    }

    // Binder for converting ParameterRuntimeBinding to System.Object.
    // Tracks state for things like cleaning up (closing streams, etc) and ensuring parameters don't collide. 
    public class Binder
    {
        List<Action> _cleanup = new List<Action>();

        HashSet<string> _blobInputs = new HashSet<string>();
        HashSet<string> _blobOutputs = new HashSet<string>();




        public static bool IsInputParameter(ParameterInfo targetParameter)
        {
            foreach (var attrData in targetParameter.GetCustomAttributesData())
            {
                string name = attrData.Constructor.DeclaringType.FullName;
                if (name == typeof(BlobInputAttribute).FullName) 
                {
                    return true;
                }
                if (name == typeof(BlobInputsAttribute).FullName)
                {
                    return true;
                }
            }
            return false;
        }

        // Bind blob to the target parameter. 
        // May be either input or output. 
        public object BindFromBlob(CloudBlobDescriptor descr, ParameterInfo targetParameter)
        {
            bool input = IsInputParameter(targetParameter);

            {
                // Check against runtime collisions.
                // Many cases can be detected statically when the function is first indexed. But sitll need runtime checks. 
                // "{foo}.{bar}.txt" and "{bar}.{foo}.txt" can collide at runtime if foo==bar.
                // Give na error now to avoid something really cryptic later.
                string id = descr.GetId();

                if (_blobOutputs.Contains(id))
                {
                    string msg = string.Format("Can't read and write to the same blob container: {0}", id);
                    throw new InvalidOperationException(msg);
                }

                if (input)
                {
                    _blobInputs.Add(id);
                }
                else
                {
                    _blobOutputs.Add(id);
                    if (_blobInputs.Contains(id))
                    {
                        string msg = string.Format("Can't read and write to the same blob container: {0}", id);
                        throw new InvalidOperationException(msg);
                    }
                }
            }

            throw new System.NotImplementedException();
        }        
    }
}