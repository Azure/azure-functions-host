// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    /// <summary>
    /// Wrapper used to adapt a <see cref="ScriptBinding"/> to the binding pipeline.
    /// </summary>
    [CLSCompliant(false)]
    public class ExtensionBinding : FunctionBinding
    {
        private ScriptBinding _binding;
        private Collection<Attribute> _attributes;

        public ExtensionBinding(ScriptHostConfiguration config, ScriptBinding binding, BindingMetadata metadata) : base(config, metadata, binding.Context.Access)
        {
            _binding = binding;
            _attributes = _binding.GetAttributes();
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            Collection<CustomAttributeBuilder> attributeBuilders = new Collection<CustomAttributeBuilder>();
            foreach (var attribute in _attributes)
            {
                CustomAttributeBuilder builder = GetAttributeBuilder(attribute);
                attributeBuilders.Add(builder);
            }

            return attributeBuilders;
        }

        public override async Task BindAsync(BindingContext context)
        {
            // All the below BindAsync logic is temporary IBinder support
            // Once the Invoker work is done, we'll be binding directly
            Collection<Attribute> resolvedAttributes = ResolveAttributes(_attributes, context.BindingData);
            var attribute = resolvedAttributes.First();
            var additionalAttributes = resolvedAttributes.Skip(1).ToArray();
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute, additionalAttributes);

            // TEMP: We'll be doing away with this IBinder code
            if (_binding.DefaultType == typeof(IAsyncCollector<byte[]>))
            {
                await BindAsyncCollectorAsync<byte[]>(context, runtimeContext);
            }
            else if (_binding.DefaultType == typeof(IAsyncCollector<JObject>))
            {
                await BindAsyncCollectorAsync<JObject>(context, runtimeContext);
            }
            else if (_binding.DefaultType == typeof(Stream))
            {
                await BindStreamAsync(context, Access, runtimeContext);
            }
            else if (_binding.DefaultType == typeof(JObject))
            {
                var result = await context.Binder.BindAsync<JObject>(runtimeContext);
                if (Access == FileAccess.Read)
                {
                    context.Value = result;
                }
            }
        }

        // TEMP - Since we're still using IBinder for non C#, we have to construct the Attributes
        // This code will go away soon
        private Collection<Attribute> ResolveAttributes(Collection<Attribute> attributes, IReadOnlyDictionary<string, string> bindingData)
        {
            Collection<Attribute> resolvedAttributes = new Collection<Attribute>();

            foreach (var attribute in attributes)
            {
                // Get the attribute construction info
                var attributeConstructionInfo = GetAttributeBuilderInfo(attribute);

                // resolve all attribute data
                if (bindingData != null)
                {
                    if (attributeConstructionInfo.ConstructorArgs != null)
                    {
                        for (int i = 0; i < attributeConstructionInfo.ConstructorArgs.Length; i++)
                        {
                            string value = attributeConstructionInfo.ConstructorArgs[i] as string;
                            if (value != null)
                            {
                                attributeConstructionInfo.ConstructorArgs[i] = ResolveAndBind(value, bindingData);
                            }
                        }
                    }

                    if (attributeConstructionInfo.Properties != null)
                    {
                        foreach (var namedProperty in attributeConstructionInfo.Properties.Where(p => p.Value is string).ToArray())
                        {
                            string value = (string)namedProperty.Value;
                            attributeConstructionInfo.Properties[namedProperty.Key] = ResolveAndBind(value, bindingData);
                        }
                    }
                }

                // construct the attribute
                Attribute resolvedAttribute = (Attribute)attributeConstructionInfo.Constructor.Invoke(attributeConstructionInfo.ConstructorArgs);

                // apply any named property values
                foreach (var namedProperty in attributeConstructionInfo.Properties)
                {
                    namedProperty.Key.SetValue(resolvedAttribute, namedProperty.Value);
                }

                resolvedAttributes.Add(resolvedAttribute);
            }

            return resolvedAttributes;
        }

        internal class AttributeBuilderInfo
        {
            public ConstructorInfo Constructor { get; set; }
            public object[] ConstructorArgs { get; set; }
            public IDictionary<PropertyInfo, object> Properties { get; set; }
        }

        internal static CustomAttributeBuilder GetAttributeBuilder(Attribute attribute)
        {
            AttributeBuilderInfo constructionInfo = GetAttributeBuilderInfo(attribute);

            var namedProperties = constructionInfo.Properties.Keys.ToArray();
            var namedPropertyValues = constructionInfo.Properties.Values.ToArray();

            return new CustomAttributeBuilder(constructionInfo.Constructor, constructionInfo.ConstructorArgs, namedProperties, namedPropertyValues);
        }

        internal static AttributeBuilderInfo GetAttributeBuilderInfo(Attribute attribute)
        {
            IDictionary<string, object> attributeData = GetAttributeData(attribute);
            Dictionary<string, object> attributeDataCaseInsensitive = new Dictionary<string, object>(attributeData, StringComparer.OrdinalIgnoreCase);
            Type attributeType = attribute.GetType();

            // Pick the ctor with the longest parameter list where all parameters are matched.
            int longestMatch = -1;
            ConstructorInfo bestCtor = null;
            Dictionary<PropertyInfo, object> propertiesToSet = null;
            object[] constructorArgs = null;
            var ctors = attributeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var currCtor in ctors)
            {
                var currCtorParams = currCtor.GetParameters();
                int len = currCtorParams.Length;
                object[] currConstructorArgs = new object[len];

                bool hasAllParameters = true;
                for (int i = 0; i < len; i++)
                {
                    var p = currCtorParams[i];
                    object value = null;
                    if (!attributeDataCaseInsensitive.TryGetValue(p.Name, out value) || value == null)
                    {
                        hasAllParameters = false;
                        break;
                    }

                    currConstructorArgs[i] = value;
                }

                if (hasAllParameters)
                {
                    if (len > longestMatch)
                    {
                        propertiesToSet = new Dictionary<PropertyInfo, object>();

                        // Set any remaining property values
                        foreach (var prop in attributeType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        {
                            if (!prop.CanWrite || !prop.GetSetMethod(/*nonPublic*/ true).IsPublic ||
                                Nullable.GetUnderlyingType(prop.PropertyType) != null)
                            {
                                continue;
                            }

                            object objValue = null;
                            if (attributeDataCaseInsensitive.TryGetValue(prop.Name, out objValue))
                            {
                                propertiesToSet.Add(prop, objValue);
                            }
                        }

                        bestCtor = currCtor;
                        constructorArgs = currConstructorArgs;
                        longestMatch = len;
                    }
                }
            }

            if (bestCtor == null)
            {
                // error!!!
                throw new InvalidOperationException("Can't figure out which ctor to call.");
            }

            AttributeBuilderInfo info = new AttributeBuilderInfo
            {
                Constructor = bestCtor,
                ConstructorArgs = constructorArgs,
                Properties = propertiesToSet
            };

            return info;
        }

        // TEMP
        internal static IDictionary<string, object> GetAttributeData(Attribute attribute)
        {
            Dictionary<string, object> attributeData = new Dictionary<string, object>();

            foreach (var property in attribute.GetType().GetProperties())
            {
                object value = property.GetValue(attribute);
                if (value != null)
                {
                    attributeData.Add(property.Name, value);
                }
            }

            return attributeData;
        }
    }
}
