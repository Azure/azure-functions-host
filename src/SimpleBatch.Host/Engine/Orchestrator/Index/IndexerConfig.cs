using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;




namespace Microsoft.WindowsAzure.Jobs
{
    // IConfiguration implementation for Registering functions. 
    internal class IndexerConfig : IConfiguration
    {
        private readonly List<FunctionDefinition> _functions = new List<FunctionDefinition>();

        private readonly Func<string, MethodInfo> _fpFuncLookup;

        private readonly List<FluentConfig> _registeredFuncs = new List<FluentConfig>();

        public IndexerConfig(Func<string, MethodInfo> fpFuncLookup)
        {
            _fpFuncLookup = fpFuncLookup;
        }

        List<ICloudBlobBinderProvider> _blobBinders = new List<ICloudBlobBinderProvider>();
        public IList<ICloudBlobBinderProvider> BlobBinders
        {
            get { return _blobBinders; }
        }

        List<ICloudTableBinderProvider> _tableBinders = new List<ICloudTableBinderProvider>();
        public IList<ICloudTableBinderProvider> TableBinders
        {
            get { return _tableBinders; }
        }

        List<ICloudBinderProvider> _binders = new List<ICloudBinderProvider>();
        public IList<ICloudBinderProvider> Binders
        {
            get { return _binders; }
        }

        // User calls this in Initialize(IConfiguration) to provide code-configuration registration of methods. 
        public IFluentConfig Register(string functionName)
        {
            var method = _fpFuncLookup(functionName);
                        

            var x = new FluentConfig(method);
            _registeredFuncs.Add(x);
            return x;
        }

        // Index driver calls this to collect the functions that were registered.
        public IEnumerable<MethodDescriptor> GetRegisteredMethods()
        {
            return from func in _registeredFuncs select func.GetDescriptor();
        }
    }

    class FluentConfig : IFluentConfig
    {
        public FluentConfig(MethodInfo method)
        {
            _method = method;


            foreach (var param in _method.GetParameters())
            {
                _paramAttributes[param.Name] = new List<object>();
            }
        }

        private readonly MethodInfo _method;
        private readonly List<Attribute> _methodAttributes = new List<Attribute>();

        Dictionary<string, List<object>> _paramAttributes = new Dictionary<string, List<object>>();

        public IFluentConfig Bind(string parameterName, Attribute binderAttribute)
        {
            if (parameterName == null)
            {
                _methodAttributes.Add(binderAttribute);
                return this;
            }

            List<object> attrs;
            if (!_paramAttributes.TryGetValue(parameterName, out attrs))
            {
                string msg = string.Format("No parameter named '{0}'", parameterName);
                throw new InvalidOperationException(msg);
            }
            attrs.Add(binderAttribute);

            return this;
        }

        public MethodDescriptor GetDescriptor()
        {
            var ps = Array.ConvertAll(_method.GetParameters(), 
                param => 
                    new FakeParameterInfo(param.ParameterType, param.Name, param.IsOut, _paramAttributes[param.Name].ToArray())
                    );

            if (_methodAttributes.Count == 0)
            {
                // If nothing, add a description to force the method 
                // to get registered.
                _methodAttributes.Add(new DescriptionAttribute(string.Empty));
            }

            return new MethodDescriptor
            {
                 Name = _method.Name,
                 MethodAttributes = _methodAttributes.ToArray(),
                 Parameters = ps
            };
        }
    }


}
