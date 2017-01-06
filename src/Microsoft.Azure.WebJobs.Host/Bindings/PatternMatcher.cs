// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{    
    // Find a Convert() method on a class that matches the type parameters. 
    internal abstract class PatternMatcher
    {
        // Get the type of the converter object. Used with FindAndCreateConverter. 
        protected abstract Type TypeConverter { get; }

        public static PatternMatcher New(Type typeBuilder, params object[] constructorArgs)
        {
            return new CreateViaType(typeBuilder, constructorArgs);
        }

        public static PatternMatcher New(object instance)
        {
            return new CreateViaInstance(instance);
        }
                        
        /// <summary>
        /// Get a converter function for the given types. 
        /// Types can be open generics, so this needs to find the appropriate IConverter interface and pattern match. 
        /// Throws if can't match - caller is responsible for doing type validation before invoking. 
        /// </summary>
        /// <param name="typeSource">source type</param>
        /// <param name="typeDest">destination type</param>
        /// <returns>converter function that takes in source type and returns destination type</returns>
        public abstract Func<object, object> TryGetConverterFunc(Type typeSource, Type typeDest);

        // Find an IConverter<TIn,TOut> on the typeConverter interface where Tin,Tout are 
        // compatible with TypeSource,typeDest.         
        // Where TIn, TOut may be generic. This will infer the generics and resolved the 
        // generic converter interface. 
        // Instantiate a func<object,object> that will invoke the converter.  
        private Func<object, object> FindAndCreateConverter(
            Type typeSource, 
            Type typeDest)
        {
            Type typeConverter = this.TypeConverter;

            // Search for IConverter<> interfaces on the type converter object. 
            var interfaces = typeConverter.GetInterfaces();
            foreach (var iface in interfaces)
            {
                // verify it's an IConverter 

                if (!iface.IsGenericType)
                {
                    continue;
                }

                bool isTask = false;

                if (iface.GetGenericTypeDefinition() != typeof(IConverter<,>))
                {
                    if (iface.GetGenericTypeDefinition() != typeof(IAsyncConverter<,>))
                    {
                        continue;
                    }
                    isTask = true;
                }

                Type typeInput = iface.GetGenericArguments()[0];
                Type typeOutput = iface.GetGenericArguments()[1];

                // Does it match? 
                // (typeInput,typeOutput) is on the converter's static interface and may be generic. 
                // (typeSource,typeDest) is the runtime type and is concerete. 
                Dictionary<string, Type> genericArgs = new Dictionary<string, Type>();

                if (!CheckArg(typeOutput, typeDest, genericArgs))
                {
                    continue;
                }

                if (!CheckArg(typeInput, typeSource, genericArgs))
                {
                    continue;
                }

                // Found a match. Now instantiate the converter func. 
                object instance = this.GetInstance(genericArgs);

                // Create an invoker object 
                typeInput = ResolveGenerics(typeInput, genericArgs);
                typeOutput = ResolveGenerics(typeOutput, genericArgs);
                return CreateConverterFunc(isTask, typeInput, typeOutput,  instance);
            }

            throw new InvalidOperationException($"No Convert method on type {typeConverter.Name} to convert from " +
                $"{typeSource.Name} to {typeDest.Name}");
        }

        // Find IConverter<typeInput,TypeOutput> on the object instance. 
        // type parameters should be resolved concrete types.         
        private static Func<object, object> CreateConverterFunc(
            bool isTask,  // IConverter vs. IAsyncConverter
            Type typeInput,  
            Type typeOutput, 
            object instance)
        {
            Type invokeType = isTask ? typeof(AsyncInvoker<,>) : typeof(Invoker<,>);
            var typeInvoker = invokeType.MakeGenericType(typeInput, typeOutput);
            var instanceInvoker = (InvokerBase)Activator.CreateInstance(typeInvoker);
            var func = instanceInvoker.Work(instance);
            return func;
        }
             
        // Instantiate a type converter given the generic args. 
        protected abstract object GetInstance(Dictionary<string, Type> genericArgs);        

        // Given a type, resolve any generic parameters and return the resolved type. This can be recursive. 
        // string - non-generic type, does not contain generic parameters.  
        // T   - type parameter
        // IEnumerable`1 - generic type definition 
        // IEnumerable<T> - type signature with 1 generic parameter 
        // IConvereter<int, T> - type signature with 2 generic parameters, the 2nd is an open generic.
        internal static Type ResolveGenerics(Type type, Dictionary<string, Type> genericArgs)
        {
            // A generic type definition is an actual class / interface declaration, 
            // not to be confused with a signature referenced by the definition. 
            // Here, Foo'1 is the generic type definition, with one generic parameter, named 'T'
            // IEnumerable<T> is a type signature that containes a generic parameter. 
            // T is the generic parameter. 
            //    class Foo<T> : IEnumerable<T>
            //
            // MakeGenericType can only be called on a GenericTypeDefinition. 
            if (type.IsGenericTypeDefinition)
            {
                var typeArgs = type.GetGenericArguments();
                int len = typeArgs.Length;
                var actualTypeArgs = new Type[len];
                for (int i = 0; i < len; i++)
                {
                    actualTypeArgs[i] = genericArgs[typeArgs[i].Name];
                }

                var resolvedType = type.MakeGenericType(actualTypeArgs);

                return resolvedType;
            }
            else
            {
                // Simple case: T
                if (type.IsGenericParameter)
                {
                    var actual = genericArgs[type.Name];
                    return actual;
                }
                else if (type.ContainsGenericParameters)
                {
                    // eg, IEnumerable<T>, IConverter<int, T>
                    // Must decompose to the generic definition, resolve each arg, and build back up. 

                    // potentially recursive case: ie, IConverter<int, T>
                    var def = type.GetGenericTypeDefinition();
                    var args = type.GetGenericArguments();

                    var resolvedArgs = new Type[args.Length];
                    for (int i = 0; i < args.Length; i++)
                    {
                        resolvedArgs[i] = ResolveGenerics(args[i], genericArgs);
                    }

                    var finalType = def.MakeGenericType(resolvedArgs);
                    return finalType;
                }
                else
                {
                    // Easy non-generic case. ie: string, int 
                    return type;
                }
            }
        }

        // Name can only map to a single type. If try to map to difference types, then it's a failed match. 
        private static bool AddGenericArg(Dictionary<string, Type> genericArgs, string name, Type type)
        {
            Type typeExisting;
            if (genericArgs.TryGetValue(name, out typeExisting))
            {
                return typeExisting == type;
            }
            genericArgs[name] = type;
            return true;
        }

        // Check if specificType is a valid instance of openType. 
        // Return true if the types are compatible. 
        // If openType has generic args, then add a [Name,Type] entry to the genericArgs dictionary. 
        private static bool CheckArg(Type openType, Type specificType, Dictionary<string, Type> genericArgs)
        {
            if (openType == specificType)
            {
                return true;
            }

            if (openType.IsAssignableFrom(specificType))
            {
                // Allow derived types. 
                return true;
            }

            // Is it a generic match?
            // T, string
            if (openType.IsGenericParameter)
            {
                string name = openType.Name;
                return AddGenericArg(genericArgs, name, specificType);
            }

            // IFoo<T>, IFoo<string> 
            if (openType.IsGenericType)
            {
                if (specificType.GetGenericTypeDefinition() != openType.GetGenericTypeDefinition())
                {
                    return false;
                }

                var typeArgs = openType.GetGenericArguments();
                var specificTypeArgs = specificType.GetGenericArguments();

                int len = typeArgs.Length;

                for (int i = 0; i < len; i++)
                {
                    if (!AddGenericArg(genericArgs, typeArgs[i].Name, specificTypeArgs[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            return false;
        }

        // Helper for getting a converter func that invokes the converter interface on an object.
        private abstract class InvokerBase
        {
            public abstract Func<object, object> Work(object instance);
        }

        // Get a converter function that invokes IConverter on an object. 
        private class Invoker<TSrc, TDest> : InvokerBase
        {
            public override Func<object, object> Work(object instance)
            {
                IConverter<TSrc, TDest> converter = (IConverter<TSrc, TDest>)instance;

                Func<object, object> func = (input) =>
                {
                    TSrc src = (TSrc)input;
                    var result = converter.Convert(src);
                    return result;
                };
                return func;
            }
        }

        // Get a converter function that invokes IAsyncConverter on an object. 
        private class AsyncInvoker<TSrc, TDest> : InvokerBase
        {
            public override Func<object, object> Work(object instance)
            {
                IAsyncConverter<TSrc, TDest> converter = (IAsyncConverter<TSrc, TDest>)instance;

                Func<object, object> func = (input) =>
                {
                    TSrc src = (TSrc)input;
                    Task<TDest> resultTask = Task.Run(() => converter.ConvertAsync(src, CancellationToken.None));

                    TDest result = resultTask.GetAwaiter().GetResult();
                    return result;
                };
                return func;
            }
        }

        // Wrapper for matching against a static type. 
        private class CreateViaType : PatternMatcher
        {
            private readonly Type _typeConverter;
            private readonly object[] _constructorArgs;
                    
            public CreateViaType(Type builderType, object[] constructorArgs)
            {
                _typeConverter = builderType;
                _constructorArgs = constructorArgs;
            }

            protected override Type TypeConverter
            {
                get
                {
                    return this._typeConverter;
                }
            }

            public override Func<object, object> TryGetConverterFunc(Type typeSource, Type typeDest)
            {
                var func = this.FindAndCreateConverter(typeSource, typeDest);                
                return func;
            }

            protected override object GetInstance(Dictionary<string, Type> genericArgs)
            {
                Type finalType = ResolveGenerics(_typeConverter, genericArgs);

                try
                {
                    // common for constructor to throw validation errors.          
                    var instance = Activator.CreateInstance(finalType, _constructorArgs);
                    return instance;
                }
                catch (TargetInvocationException e)
                {
                    throw e.InnerException;
                }
            }
        }

        // Wrapper for matching against an instance. 
        private class CreateViaInstance : PatternMatcher
        {
            private readonly object _instance;
         
            public CreateViaInstance(object instance)
            {
                _instance = instance;
            }

            protected override Type TypeConverter
            {
                get
                {
                    return _instance.GetType();
                }
            }

            public override Func<object, object> TryGetConverterFunc(Type typeSource, Type typeDest)
            {
                var func = this.FindAndCreateConverter(typeSource, typeDest);
                return func;
            }

            protected override object GetInstance(Dictionary<string, Type> genericArgs)
            {
                return _instance;
            }
        }
    }
}
