// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    public abstract class FunctionBinding
    {
        private readonly ScriptHostConfiguration _config;

        protected FunctionBinding(ScriptHostConfiguration config, BindingMetadata metadata, FileAccess access)
        {
            _config = config;
            Access = access;
            Metadata = metadata;
        }

        public BindingMetadata Metadata { get; private set; }

        public FileAccess Access { get; private set; }

        public abstract Task BindAsync(BindingContext context);

        public abstract Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType);

        internal static Collection<FunctionBinding> GetBindings(ScriptHostConfiguration config, IEnumerable<BindingMetadata> functions, FileAccess fileAccess)
        {
            Collection<FunctionBinding> bindings = new Collection<FunctionBinding>();

            if (bindings != null)
            {
                foreach (var function in functions)
                {
                    string type = function.Type.ToLowerInvariant();
                    switch (type)
                    {
                        case "table":
                            TableBindingMetadata tableBindingMetadata = (TableBindingMetadata)function;
                            bindings.Add(new TableBinding(config, tableBindingMetadata, fileAccess));
                            break;
                        case "http":
                            if (fileAccess != FileAccess.Write)
                            {
                                throw new InvalidOperationException("Http binding can only be used for output.");
                            }
                            bindings.Add(new HttpBinding(config, function, FileAccess.Write));
                            break;
                        default:
                            FunctionBinding binding = null;
                            if (TryParseFunctionBinding(config, function.Raw, out binding))
                            {
                                bindings.Add(binding);
                            }
                            break;
                    }
                }
            }

            return bindings;
        }

        private static bool TryParseFunctionBinding(ScriptHostConfiguration config, JObject metadata, out FunctionBinding functionBinding)
        {
            functionBinding = null;            

            ScriptBindingContext bindingContext = new ScriptBindingContext(metadata);
            ScriptBinding scriptBinding = null;
            foreach (var provider in config.BindingProviders)
            {
                if (provider.TryCreate(bindingContext, out scriptBinding))
                {
                    break;
                }
            }

            if (scriptBinding == null)
            {
                return false;
            }

            BindingMetadata bindingMetadata = BindingMetadata.Create(metadata);
            functionBinding = new ExtensionBinding(config, scriptBinding, bindingMetadata);

            return true;
        }

        protected string Resolve(string name)
        {
            if (_config.HostConfig.NameResolver == null)
            {
                return name;
            }

            return _config.HostConfig.NameResolver.ResolveWholeString(name);
        }

        internal static void AddStorageAccountAttribute(Collection<CustomAttributeBuilder> attributes, string connection)
        {
            var constructorTypes = new Type[] { typeof(string) };
            var constructorArguments = new object[] { connection };
            var attribute = new CustomAttributeBuilder(typeof(StorageAccountAttribute).GetConstructor(constructorTypes), constructorArguments);
            attributes.Add(attribute);
        }

        internal static ICollection ReadAsCollection(object value)
        {
            ICollection values = null;

            if (value is Stream)
            {
                // first deserialize the stream as a string
                using (var reader = new StreamReader((Stream)value))
                {
                    value = reader.ReadToEnd();
                }
            }

            string stringValue = value as string;
            if (Utility.IsJson(stringValue))
            {
                // json values can either be singleton objects,
                // or arrays of objects/values
                JToken token = JToken.Parse(stringValue);
                if (token.Type != JTokenType.Array)
                {
                    // not an array so create a new array and add
                    // the singleton
                    values = new JArray() { token };
                }
                else
                {
                    values = (JArray)token;
                }
            }
            else
            {
                // not json, so add the singleton value
                values = new Collection<object>() { value };
            }

            return values;
        }

        internal static async Task BindAsyncCollectorAsync<T>(BindingContext context)
        {
            IAsyncCollector<T> collector = await context.Binder.BindAsync<IAsyncCollector<T>>(context.Attributes);

            IEnumerable values = ReadAsCollection(context.Value);

            // convert values as necessary and add to the collector
            foreach (var value in values)
            {
                object converted = null;
                if (typeof(T) == typeof(string))
                {
                    converted = value.ToString();
                }
                else if (typeof(T) == typeof(JObject))
                {
                    converted = (JObject)value;
                }
                else if (typeof(T) == typeof(byte[]))
                {
                    byte[] bytes = value as byte[];
                    if (bytes == null)
                    {
                        string stringValue = value.ToString();
                        bytes = Encoding.UTF8.GetBytes(stringValue);
                    }
                    converted = bytes;
                }
                else
                {
                    throw new ArgumentException("Unsupported collection type.");
                }

                await collector.AddAsync((T)converted);
            }
        }

        internal static async Task BindStreamAsync(BindingContext context, FileAccess access)
        {
            Stream stream = await context.Binder.BindAsync<Stream>(context.Attributes);

            if (access == FileAccess.Write)
            {
                ConvertValueToStream(context.Value, stream);
            }
            else
            {
                // Read the value into the context Value converting based on data type
                object converted = null;
                ConvertStreamToValue(stream, context.DataType, ref converted);
                context.Value = converted;
            }
        }

        public static void ConvertValueToStream(object value, Stream stream)
        {
            Stream valueStream = value as Stream;
            if (valueStream == null)
            {
                // Convert the value to bytes and write it
                // to the stream
                byte[] bytes = null;
                Type type = value.GetType();
                if (type == typeof(byte[]))
                {
                    bytes = (byte[])value;
                }
                else if (type == typeof(string))
                {
                    bytes = Encoding.UTF8.GetBytes((string)value);
                }
                else if (type == typeof(int))
                {
                    bytes = BitConverter.GetBytes((int)value);
                }
                else if (type == typeof(bool))
                {
                    bytes = BitConverter.GetBytes((bool)value);
                }
                else if (type == typeof(double))
                {
                    bytes = BitConverter.GetBytes((double)value);
                }

                using (valueStream = new MemoryStream(bytes))
                {
                    valueStream.CopyTo(stream);
                }
            }
            else
            {
                // value is already a stream, so copy it directly
                valueStream.CopyTo(stream);
            }
        }

        public static void ConvertStreamToValue(Stream stream, DataType dataType, ref object converted)
        {
            switch (dataType)
            {
                case DataType.String:
                    using (StreamReader sr = new StreamReader(stream))
                    {
                        converted = sr.ReadToEnd();
                    }
                    break;
                case DataType.Binary:
                    using (MemoryStream ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        converted = ms.ToArray();
                    }
                    break;
                case DataType.Stream:
                    // when the target value is a Stream, we copy the value
                    // into the Stream passed in
                    Stream targetStream = converted as Stream;
                    stream.CopyTo(targetStream);
                    break;
            }
        }
    }
}
