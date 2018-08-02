﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// This general purpose binder can service all SDK extensions by leveraging the SDK metadata provider.
    /// This should eventually replace all other ScriptBindingProviders.
    /// </summary>
    internal class GeneralScriptBindingProvider : ScriptBindingProvider
    {
        private IJobHostMetadataProvider _metadataProvider;

        public GeneralScriptBindingProvider(ILogger logger, IJobHostMetadataProvider metadataProvider)
            : base(logger)
        {
            _metadataProvider = metadataProvider;
        }

        public override bool TryCreate(ScriptBindingContext context, out ScriptBinding binding)
        {
            string name = context.Type;
            var attrType = _metadataProvider?.GetAttributeTypeFromName(name);
            if (attrType == null)
            {
                binding = null;
                return false;
            }

            try
            {
                var attr = _metadataProvider.GetAttribute(attrType, context.Metadata);
                binding = new GeneralScriptBinding(_metadataProvider, attr, context);
            }
            catch (Exception e)
            {
                throw new Exception($"Unable to configure binding '{context.Name}' of type '{name}'. This may indicate invalid function.json properties", e);
            }

            return true;
        }

        public override bool TryResolveAssembly(string assemblyName, AssemblyLoadContext targetContext, out Assembly assembly)
        {
            return _metadataProvider.TryResolveAssembly(assemblyName, out assembly);
        }

        // Function.json specifies a type via optional DataType and Cardinality properties.
        // Read the properties and convert that into a System.Type.
        internal static Type GetRequestedType(ScriptBindingContext context)
        {
            Type type = ParseDataType(context);

            Cardinality cardinality;
            if (!Enum.TryParse<Cardinality>(context.Cardinality, true, out cardinality))
            {
                cardinality = Cardinality.One; // default
            }

            if (cardinality == Cardinality.Many)
            {
                // arrays are supported for both trigger input as well
                // as output bindings
                type = type.MakeArrayType();
            }
            return type;
        }

        // Parse the DataType field and return as a System.Type.
        // Never return null. Use typeof(object) to refer to an unnkown.
        private static Type ParseDataType(ScriptBindingContext context)
        {
            DataType result;
            if (Enum.TryParse<DataType>(context.DataType, true, out result))
            {
                switch (result)
                {
                    case DataType.Binary:
                        return typeof(byte[]);

                    case DataType.Stream:
                        return typeof(Stream);

                    case DataType.String:
                        return typeof(string);
                }
            }

            return typeof(object);
        }

        private class GeneralScriptBinding : ScriptBinding
        {
            private readonly Attribute _attribute;
            private readonly IJobHostMetadataProvider _metadataProvider;

            private Type _defaultType;

            public GeneralScriptBinding(IJobHostMetadataProvider metadataProvider, Attribute attribute, ScriptBindingContext context)
                : base(context)
            {
                _metadataProvider = metadataProvider;
                _attribute = attribute;
            }

            // This should only be called in script scenarios (not C#).
            // So explicitly make it lazy.
            public override Type DefaultType
            {
                get
                {
                    if (_defaultType == null)
                    {
                        Type requestedType = GetRequestedType(Context);
                        _defaultType = _metadataProvider.GetDefaultType(_attribute, Context.Access, requestedType);
                    }
                    return _defaultType;
                }
            }

            public override Collection<Attribute> GetAttributes() => new Collection<Attribute> { _attribute };
        }
    }
}
