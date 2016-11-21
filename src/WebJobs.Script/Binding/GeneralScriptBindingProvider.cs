// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Collections.ObjectModel;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    // A general purpose binder that pulls any self-describing SDK extensions. 
    [CLSCompliant(false)]
    public class GeneralScriptBindingProvider : ScriptBindingProvider    
    {
        private ToolingHelper _helper;
        private JObject _hostMetadata;

        public GeneralScriptBindingProvider(
            JobHostConfiguration config, JObject hostMetadata, TraceWriter traceWriter) : base(config, hostMetadata, traceWriter)
        {
            _helper = new ToolingHelper(config);
            _hostMetadata = hostMetadata;
        }

        // 
        public async Task AddAssembly(Assembly assembly)
        {
            await _helper.AddAssemblyAsync(assembly, _hostMetadata);
        }
        public async Task AddExtension(Type extensionType)
        {
            await _helper.AddExtension(extensionType, _hostMetadata);
        }

        // $$$ Get rid of this method 
        public async Task FinishAddsAsync()
        {
            await _helper.FinishAddsAsync();
        }

        public override bool TryResolveAssembly(string assemblyName, out Assembly assembly)
        {
            return _helper.TryResolveAssembly(assemblyName, out assembly);
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            string kind = context.Type;
            Type attributeType = _helper.GetAttributeTypeFromName(kind);
            if (attributeType == null)
            {
                binding = null;
                return false;
            }

            var attributes = _helper.GetAttributes(attributeType, context.Metadata);
            var attribute = attributes[0];

            FileAccess access = context.Access;
            Cardinality cardinality = Cardinality.One;
            if (context.Cardinality != null)
            {
                cardinality = (Cardinality)Enum.Parse(typeof(Cardinality), context.Cardinality, ignoreCase: true);
            }
            DataType dataType = DataType.String;
            if (context.DataType != null)
            {
                dataType = (DataType)Enum.Parse(typeof(DataType), context.DataType, ignoreCase: true);
            }

            var defaultType = _helper.GetDefaultType(access, cardinality, dataType, attribute);

            binding = new AwesomeScriptBinding(context, defaultType, attributes);
            return true;
        }
                
        private class AwesomeScriptBinding : ScriptBinding
        {
            private readonly Type _defaultType;
            private readonly Attribute[] _attributes;

            public AwesomeScriptBinding(ScriptBindingContext context, 
                Type defaultType, 
                Attribute[] attributes
                ) : base(context)
            {
                _defaultType = defaultType;
                _attributes = attributes;
            }

            public override Type DefaultType
            {
                get
                {
                    return _defaultType;
                }
            }

            public override Collection<Attribute> GetAttributes()
            {
                return new Collection<Attribute>(_attributes);
            }
        }
    }
}
