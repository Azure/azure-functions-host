// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Helpers for creating bindings to common patterns such as Messaging or Streams.
    /// This will add additional adapters to connect the user's parameter type to an IAsyncCollector. 
    /// It will also see <see cref="IConverterManager"/> to convert 
    /// from the user's type to the underlying IAsyncCollector's message type.
    /// For example, for messaging patterns, if the user is a ICollector, this will add an adapter that implements ICollector and calls IAsyncCollector.  
    /// </summary>
    internal static class BindingFactoryHelpers
    {
         // Bind a trigger argument to various parameter types. 
        // Handles either T or T[], 
        internal static ITriggerDataArgumentBinding<TTriggerValue> GetTriggerArgumentBinding<TMessage, TTriggerValue>(
            ITriggerBindingStrategy<TMessage, TTriggerValue> bindingStrategy,
            ParameterInfo parameter,
            IConverterManager converterManager,
            out bool singleDispatch)
        {
            ITriggerDataArgumentBinding<TTriggerValue> argumentBinding = null;
            if (parameter.ParameterType.IsArray)
            {
                // dispatch the entire batch in a single call. 
                singleDispatch = false;

                var elementType = parameter.ParameterType.GetElementType();

                var innerArgumentBinding = GetTriggerArgumentElementBinding<TMessage, TTriggerValue>(elementType, bindingStrategy, converterManager);

                argumentBinding = new ArrayTriggerArgumentBinding<TMessage, TTriggerValue>(bindingStrategy, innerArgumentBinding, converterManager);

                return argumentBinding;
            }
            else
            {
                // Dispatch each item one at a time
                singleDispatch = true;

                var elementType = parameter.ParameterType;
                argumentBinding = GetTriggerArgumentElementBinding<TMessage, TTriggerValue>(elementType, bindingStrategy, converterManager);
                return argumentBinding;
            }
        }

        // Bind a T. 
        private static SimpleTriggerArgumentBinding<TMessage, TTriggerValue> GetTriggerArgumentElementBinding<TMessage, TTriggerValue>(
            Type elementType,
            ITriggerBindingStrategy<TMessage, TTriggerValue> bindingStrategy,
            IConverterManager converterManager)
        {
            if (elementType == typeof(TMessage))
            {
                return new SimpleTriggerArgumentBinding<TMessage, TTriggerValue>(bindingStrategy, converterManager);
            }
            if (elementType == typeof(string))
            {
                return new StringTriggerArgumentBinding<TMessage, TTriggerValue>(bindingStrategy, converterManager);
            }
            else
            {
                // Default, assume a Poco
                return new PocoTriggerArgumentBinding<TMessage, TTriggerValue>(bindingStrategy, converterManager, elementType);
            }
        }

        // Get the "core" TMEssage type that's consistent with an IAsyncCollector pattern. 
        // This must be in sync with the rules in BindCollector<>.
        // Or null if hte parameter type is not consistent with IASyncCollector.
        public static Type GetAsyncCollectorCoreType(Type parameterType)
        {
            // IAsyncCollector<T>
            if (parameterType.IsGenericType)
            {
                var genericType = parameterType.GetGenericTypeDefinition();
                var elementType = parameterType.GetGenericArguments()[0];

                if (genericType == typeof(IAsyncCollector<>))
                {
                    return elementType;
                }
                else if (genericType == typeof(ICollector<>))
                {
                    return elementType;
                }

                return null;
            }
            else
            {
                if (parameterType.IsByRef)
                {
                    var inner = parameterType.GetElementType(); // strip off the byref type

                    if (inner.IsArray)
                    {
                        var elementType = inner.GetElementType();
                        return elementType; 
                    }
                    return inner;
                }
                return null;
            }
        }

        /// <summary>
        /// Create a binding rule to an IAsyncCollector`T, where the user parameter's type is resolved to a T via the ConverterManager.
        /// </summary>
        /// <typeparam name="TAttribute">Type of binding attribute</typeparam>
        /// <typeparam name="TMessage">Core Message type, such as 'CloudQueueMessage'</typeparam>
        /// <param name="parameter">the parameter being bound. The parameter should have a custom attribute of TAttribute on it.</param>
        /// <param name="nameResolver">a name resolver object for resolving %% pairs in the attribute</param>
        /// <param name="converterManager">a converter manager for converting types</param>
        /// <param name="bindingDataContract">binding data contract</param>
        /// <param name="buildFromAttribute">a builder to create the IAsyncCollector`T from a 'resolved' version of the TAttribute on this parameter. </param>
        /// <param name="buildParamDescriptor">an optional function to create a more specific ParameterDescriptor object to display in the dashboard.</param>
        /// <param name="hook">An optional post-resolve hook for advanced scenarios.</param>
        /// <returns></returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "converterManager")]
        internal static IBinding BindCollector<TAttribute, TMessage>(
            ParameterInfo parameter,
            INameResolver nameResolver,
            IConverterManager converterManager,
            IReadOnlyDictionary<string, Type> bindingDataContract,
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
            Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> buildParamDescriptor = null,
            Func<TAttribute, ParameterInfo, INameResolver, Task<TAttribute>> hook = null)
            where TAttribute : Attribute
        {
            if (parameter == null)
            {
                throw new ArgumentNullException("parameter");
            }

            Func<TAttribute, Task<TAttribute>> hookWrapper = null;
            if (hook != null)
            {
                hookWrapper = (attr) => hook(attr, parameter, nameResolver);
            }
            TAttribute attributeSource = parameter.GetCustomAttribute<TAttribute>(inherit: false); 

            // ctor will do validation and throw. 
            var cloner = new AttributeCloner<TAttribute>(attributeSource, bindingDataContract, nameResolver, hookWrapper);
            
            Type parameterType = parameter.ParameterType;

            Func<TAttribute, IValueProvider> argumentBuilder = null;                                            

            if (parameterType.IsGenericType)
            {
                var genericType = parameterType.GetGenericTypeDefinition();
                var elementType = parameterType.GetGenericArguments()[0];

                if (genericType == typeof(IAsyncCollector<>))
                {
                    if (elementType == typeof(TMessage))
                    {
                        // Bind to IAsyncCollector<TMessage>. This is the "purest" binding, no adaption needed. 
                        argumentBuilder = (attrResolved) =>
                        {
                            IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                            var invokeString = cloner.GetInvokeString(attrResolved); 
                             
                            return new AsyncCollectorValueProvider<IAsyncCollector<TMessage>, TMessage>(raw, raw, invokeString);
                        };
                    }
                    else
                    {
                        // Bind to IAsyncCollector<T>
                        // Get a converter from T to TMessage
                        argumentBuilder = DynamicInvokeBuildIAsyncCollectorArgument(elementType, converterManager, buildFromAttribute, cloner);
                    }
                }
                else if (genericType == typeof(ICollector<>))
                {
                    if (elementType == typeof(TMessage))
                    {
                        // Bind to ICollector<TMessage> This just needs an Sync/Async wrapper
                        argumentBuilder = (attrResolved) =>
                        {
                            IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                            var invokeString = cloner.GetInvokeString(attrResolved);
                            ICollector<TMessage> obj = new SyncAsyncCollectorAdapter<TMessage>(raw);
                            return new AsyncCollectorValueProvider<ICollector<TMessage>, TMessage>(obj, raw, invokeString);
                        };
                    }
                    else
                    {
                        // Bind to ICollector<T>. 
                        // This needs both a conversion from T to TMessage and an Sync/Async wrapper
                        argumentBuilder = DynamicInvokeBuildICollectorArgument(elementType, converterManager, buildFromAttribute, cloner);
                    }
                }
            }

            if (parameter.IsOut)
            {
                Type elementType = parameter.ParameterType.GetElementType();

                // How should "out byte[]" bind?
                // If there's an explicit "byte[] --> TMessage" converter, then that takes precedence.                 
                // Else, bind over an array of "byte --> TMessage" converters 

                argumentBuilder = DynamicInvokeBuildOutArgument(elementType, converterManager, buildFromAttribute, cloner);

                if (argumentBuilder != null)
                {
                }
                else if (elementType.IsArray)
                {
                    if (elementType == typeof(TMessage[]))
                    {
                        argumentBuilder = (attrResolved) =>
                        {
                            IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                            var invokeString = cloner.GetInvokeString(attrResolved);
                            return new OutArrayValueProvider<TMessage>(raw, invokeString);
                        };
                    }
                    else
                    {
                        // out TMessage[]
                        var e2 = elementType.GetElementType();
                        argumentBuilder = DynamicBuildOutArrayArgument(e2, converterManager, buildFromAttribute, cloner);
                    }
                }
                else
                {
                    // Single enqueue
                    //    out TMessage
                    if (elementType == typeof(TMessage))
                    {
                        argumentBuilder = (attrResolved) =>
                        {
                            IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                            var invokeString = cloner.GetInvokeString(attrResolved);
                            return new OutValueProvider<TMessage>(raw, invokeString);
                        };
                    }
                }

                // For out-param, give some rich errors. 
                if (argumentBuilder == null)
                {
                    if (typeof(IEnumerable).IsAssignableFrom(elementType))
                    {
                        throw new InvalidOperationException(
                            "Enumerable types are not supported. Use ICollector<T> or IAsyncCollector<T> instead.");
                    }
                    else if (typeof(object) == elementType)
                    {
                        throw new InvalidOperationException("Object element types are not supported.");
                    }
                }
            }

            if (argumentBuilder == null)
            {
                // Can't bind it. 
                return null;
            }

            ParameterDescriptor param;
            if (buildParamDescriptor != null)
            {
                param = buildParamDescriptor(attributeSource, parameter, nameResolver);                
            }
            else
            {
                // If no parameter supplied, use a default. 
                param = new ParameterDescriptor
                {
                    Name = parameter.Name,
                    DisplayHints = new ParameterDisplayHints
                    {
                        Description = "output"
                    }
                };
            }

            return new AsyncCollectorBinding<TAttribute, TMessage>(param, argumentBuilder, cloner);
        }

        private static Func<TAttribute, IValueProvider> DynamicBuildOutArrayArgument<TAttribute, TMessage>(
            Type typeMessageSrc,
            IConverterManager cm,
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
            AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            var method = typeof(BindingFactoryHelpers).GetMethod("BuildOutArrayArgument", BindingFlags.NonPublic | BindingFlags.Static);
            method = method.MakeGenericMethod(typeof(TAttribute), typeof(TMessage), typeMessageSrc);
            var argumentBuilder = MethodInvoke<Func<TAttribute, IValueProvider>>(method, cm, buildFromAttribute, cloner);
            return argumentBuilder;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Dynamically invoked")]
        private static Func<TAttribute, IValueProvider> BuildOutArrayArgument<TAttribute, TMessage, TMessageSrc>(
            IConverterManager cm,
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
            AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            // Other 
            Func<TMessageSrc, TAttribute, TMessage> convert = cm.GetConverter<TMessageSrc, TMessage, TAttribute>();
            Func<TAttribute, IValueProvider> argumentBuilder = (attrResolved) =>
            {
                IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                IAsyncCollector<TMessageSrc> obj = new TypedAsyncCollectorAdapter<TMessageSrc, TMessage, TAttribute>(
                    raw, convert, attrResolved);
                string invokeString = cloner.GetInvokeString(attrResolved);
                return new OutArrayValueProvider<TMessageSrc>(obj, invokeString);
            };
            return argumentBuilder;
        }

        // Helper to dynamically invoke BuildICollectorArgument with the proper generics
        // Can we bind to 'out TUser'?  Requires converter manager to supply a TUser--> TMessage converter. 
        // Return null if we can't bind it. 
        private static Func<TAttribute, IValueProvider> DynamicInvokeBuildOutArgument<TAttribute, TMessage>(
                Type typeMessageSrc,
                IConverterManager cm,
                Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
                AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            var method = typeof(BindingFactoryHelpers).GetMethod("BuildOutArgument", BindingFlags.NonPublic | BindingFlags.Static);
            method = method.MakeGenericMethod(typeof(TAttribute), typeof(TMessage), typeMessageSrc);
            var argumentBuilder = MethodInvoke<Func<TAttribute, IValueProvider>>(method, cm, buildFromAttribute, cloner);
            return argumentBuilder;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Dynamically invoked")]
        private static Func<TAttribute, IValueProvider> BuildOutArgument<TAttribute, TMessage, TMessageSrc>(
            IConverterManager cm,
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
            AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            // Other 
            Func<TMessageSrc, TAttribute, TMessage> convert = cm.GetConverter<TMessageSrc, TMessage, TAttribute>();
            if (convert == null)
            {
                return null;
            }
            Func<TAttribute, IValueProvider> argumentBuilder = (attrResolved) =>
            {
                IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                IAsyncCollector<TMessageSrc> obj = new TypedAsyncCollectorAdapter<TMessageSrc, TMessage, TAttribute>(
                    raw, convert, attrResolved);
                string invokeString = cloner.GetInvokeString(attrResolved);
                return new OutValueProvider<TMessageSrc>(obj, invokeString);
            };
            return argumentBuilder;
        }

        // Helper to dynamically invoke BuildICollectorArgument with the proper generics
        private static Func<TAttribute, IValueProvider> DynamicInvokeBuildICollectorArgument<TAttribute, TMessage>(
                Type typeMessageSrc,
                IConverterManager cm,
                Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
                AttributeCloner<TAttribute> cloner)
             where TAttribute : Attribute
        {
            var method = typeof(BindingFactoryHelpers).GetMethod("BuildICollectorArgument", BindingFlags.NonPublic | BindingFlags.Static);
            method = method.MakeGenericMethod(typeof(TAttribute), typeof(TMessage), typeMessageSrc);
            var argumentBuilder = MethodInvoke<Func<TAttribute, IValueProvider>>(method, cm, buildFromAttribute, cloner);
            return argumentBuilder;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Dynamic invoke")]
        private static Func<TAttribute, IValueProvider> BuildICollectorArgument<TAttribute, TMessage, TMessageSrc>(
            IConverterManager cm,
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
            AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            // Other 
            Func<TMessageSrc, TAttribute, TMessage> convert = cm.GetConverter<TMessageSrc, TMessage, TAttribute>();
            if (convert == null)
            {
                ThrowMissingConversionError(typeof(TMessageSrc));
            }
            Func<TAttribute, IValueProvider> argumentBuilder = (attrResolved) =>
            {
                IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                IAsyncCollector<TMessageSrc> obj = new TypedAsyncCollectorAdapter<TMessageSrc, TMessage, TAttribute>(
                    raw, convert, attrResolved);
                ICollector<TMessageSrc> obj2 = new SyncAsyncCollectorAdapter<TMessageSrc>(obj);
                string invokeString = cloner.GetInvokeString(attrResolved);
                return new AsyncCollectorValueProvider<ICollector<TMessageSrc>, TMessage>(obj2, raw, invokeString);
            };
            return argumentBuilder;
        }

        // Helper to dynamically invoke BuildIAsyncCollectorArgument with the proper generics
        private static Func<TAttribute, IValueProvider> DynamicInvokeBuildIAsyncCollectorArgument<TAttribute, TMessage>(
                Type typeMessageSrc,
                IConverterManager cm,
                Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
                AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            var method = typeof(BindingFactoryHelpers).GetMethod("BuildIAsyncCollectorArgument", BindingFlags.NonPublic | BindingFlags.Static);
            method = method.MakeGenericMethod(typeof(TAttribute), typeof(TMessage), typeMessageSrc);
            var argumentBuilder = MethodInvoke<Func<TAttribute, IValueProvider>>(method, cm, buildFromAttribute, cloner);
            return argumentBuilder;
        }
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode", Justification = "Dynamically invoked")]
        private static Func<TAttribute, IValueProvider> BuildIAsyncCollectorArgument<TAttribute, TMessage, TMessageSrc>(
                IConverterManager cm,
                Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute,
                AttributeCloner<TAttribute> cloner)
            where TAttribute : Attribute
        {
            Func<TMessageSrc, TAttribute, TMessage> convert = cm.GetConverter<TMessageSrc, TMessage, TAttribute>();
            if (convert == null)
            {
                ThrowMissingConversionError(typeof(TMessageSrc));
            }
            Func<TAttribute, IValueProvider> argumentBuilder = (attrResolved) =>
            {
                IAsyncCollector<TMessage> raw = buildFromAttribute(attrResolved);
                IAsyncCollector<TMessageSrc> obj = new TypedAsyncCollectorAdapter<TMessageSrc, TMessage, TAttribute>(
                    raw, convert, attrResolved);
                var invokeString = cloner.GetInvokeString(attrResolved);
                return new AsyncCollectorValueProvider<IAsyncCollector<TMessageSrc>, TMessage>(obj, raw, invokeString);
            };
            return argumentBuilder;
        }

        // typeUser - type in the user's parameter. 
        private static void ThrowMissingConversionError(Type typeUser)
        {
            if (typeUser.IsPrimitive)
            {
                throw new NotSupportedException("Primitive types are not supported.");
            }

            if (typeof(IEnumerable).IsAssignableFrom(typeUser))
            {
                throw new InvalidOperationException("Nested collections are not supported.");
            }
            throw new InvalidOperationException("Can't convert from type '" + typeUser.FullName);
        }

        // Helper to invoke and unwrap teh target exception. 
        private static TReturn MethodInvoke<TReturn>(MethodInfo method, params object[] args)
        {
            try
            {
                var result = method.Invoke(null, args);
                return (TReturn)result;
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException;
            }
        }
    }
}