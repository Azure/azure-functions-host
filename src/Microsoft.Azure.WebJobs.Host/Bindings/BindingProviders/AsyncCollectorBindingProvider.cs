// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // General rule for binding parameters to an AsyncCollector. 
    // Supports the various flavors like IAsyncCollector, ICollector, out T, out T[]. 
    internal class AsyncCollectorBindingProvider<TAttribute, TType> : FluentBindingProvider<TAttribute>, IBindingProvider, IBindingRuleProvider
        where TAttribute : Attribute
    {
        private readonly INameResolver _nameResolver;
        private readonly IConverterManager _converterManager;
        private readonly PatternMatcher _patternMatcher;

         public AsyncCollectorBindingProvider(
            INameResolver nameResolver,
            IConverterManager converterManager,
            PatternMatcher patternMatcher)
        {
            this._nameResolver = nameResolver;
            this._converterManager = converterManager;
            this._patternMatcher = patternMatcher;
        }

        // Describe different flavors of IAsyncCollector<T> bindings. 
        private enum Mode
        {
            IAsyncCollector,
            ICollector,
            OutSingle,
            OutArray
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            var parameter = context.Parameter;

            var mode = GetMode(parameter);
            if (mode == null)
            {
                return Task.FromResult<IBinding>(null);
            }
            
            var type = typeof(ExactBinding<>).MakeGenericType(typeof(TAttribute), typeof(TType), mode.ElementType);
            var method = type.GetMethod("TryBuild", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var binding = BindingFactoryHelpers.MethodInvoke<IBinding>(method, this, mode.Mode, context);

            return Task.FromResult<IBinding>(binding);
        }

        // Parse the signature to determine which mode this is. 
        // Can also check with converter manager to disambiguate some cases. 
        private CollectorBindingPattern GetMode(ParameterInfo parameter)
        {
            Type parameterType = parameter.ParameterType;
            if (parameterType.IsGenericType)
            {
                var genericType = parameterType.GetGenericTypeDefinition();
                var elementType = parameterType.GetGenericArguments()[0];

                if (genericType == typeof(IAsyncCollector<>))
                {
                    return new CollectorBindingPattern(Mode.IAsyncCollector, elementType);
                }
                else if (genericType == typeof(ICollector<>))
                {
                    return new CollectorBindingPattern(Mode.ICollector, elementType);
                }

                // A different interface. Let another rule try it. 
                return null;
            }

            if (parameter.IsOut)
            {
                // How should "out byte[]" bind?
                // If there's an explicit "byte[] --> TMessage" converter, then that takes precedence.                 
                // Else, bind over an array of "byte --> TMessage" converters 
                Type elementType = parameter.ParameterType.GetElementType();
                bool hasConverter = this._converterManager.HasConverter<TAttribute>(elementType, typeof(TType));
                if (hasConverter)
                {
                    // out T, where T might be an array 
                    return new CollectorBindingPattern(Mode.OutSingle, elementType);
                }

                if (elementType.IsArray)
                {
                    // out T[]
                    var messageType = elementType.GetElementType();
                    return new CollectorBindingPattern(Mode.OutArray, messageType);
                }

                var validator = ConverterManager.GetTypeValidator<TType>();
                if (validator.IsMatch(elementType))
                {
                    // out T, t is not an array 
                    return new CollectorBindingPattern(Mode.OutSingle, elementType);                    
                }

                // For out-param ,we don't expect another rule to claim it. So give some rich errors on mismatch.
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

            // No match. Let another rule claim it
            return null;            
        }

        // Represent the different possible flavors for binding to an async collector
        private class CollectorBindingPattern
        {
            public CollectorBindingPattern(Mode mode, Type elementType)
            {
                this.Mode = mode;
                this.ElementType = elementType;
            }
            public Mode Mode { get; set; }
            public Type ElementType { get; set; }
        }

        private static Type[] MakeArray(params Type[] types)
        {
            return types.Where(type => type != null).ToArray();
        }

        private static void AddRulesForType(Type type, List<BindingRule> rules)
        {
            var typeIAC = typeof(IAsyncCollector<>).MakeGenericType(type);

            Type intermediateType = null;
            if (type != typeof(TType))
            {
                // Use a converter 
                intermediateType = typeof(IAsyncCollector<TType>);
            }

            rules.Add(
                new BindingRule
                {
                    SourceAttribute = typeof(TAttribute),
                    Converters = MakeArray(intermediateType),
                    UserType = new ConverterManager.ExactMatch(typeIAC)
                });

            rules.Add(
                  new BindingRule
                  {
                      SourceAttribute = typeof(TAttribute),
                      Converters = MakeArray(intermediateType, typeIAC),                      
                      UserType = new ConverterManager.ExactMatch(typeof(ICollector<>).MakeGenericType(type))
                  });

            rules.Add(
                  new BindingRule
                  {
                      SourceAttribute = typeof(TAttribute),
                      Converters = MakeArray(intermediateType, typeIAC),
                      UserType = new ConverterManager.ExactMatch(type.MakeByRefType())
                  });

            rules.Add(
                  new BindingRule
                  {
                      SourceAttribute = typeof(TAttribute),
                      Converters = MakeArray(intermediateType, typeIAC),
                      UserType = new ConverterManager.ExactMatch(type.MakeArrayType().MakeByRefType())
                  });
        }

        public IEnumerable<BindingRule> GetRules()
        {
            var rules = new List<BindingRule>();
            AddRulesForType(typeof(TType), rules);
                        
            var cm = (ConverterManager)_converterManager;
            var types = cm.GetPossibleSourceTypesFromDestination(typeof(TAttribute), typeof(TType));
                        
            foreach (var type in types)
            {
                AddRulesForType(type, rules);
            }

            return rules;
        }

        public Type GetDefaultType(Attribute attribute, FileAccess access, Type requestedType)
        {
            if (access == FileAccess.Write)
            {              
                var cm = (ConverterManager)this._converterManager;
                var types = cm.GetPossibleSourceTypesFromDestination(attribute.GetType(), typeof(TType));

                // search in precedence 
                foreach (var target in new Type[] { typeof(JObject), typeof(byte[]), typeof(string) })
                {
                    if (types.Contains(target))
                    {
                        return typeof(IAsyncCollector<>).MakeGenericType(target);
                    }
                }
                return null;
            }
            return null;
        }

        // TType - specified in the rule. 
        // TMessage - element type of the IAsyncCollector<> we matched to.  
        private class ExactBinding<TMessage> : BindingBase<TAttribute>
        {
            private readonly Func<object, object> _buildFromAttribute;

            private readonly FuncConverter<TMessage, TAttribute, TType> _converter;
            private readonly Mode _mode;

            public ExactBinding(
                AttributeCloner<TAttribute> cloner,
                ParameterDescriptor param,
                Mode mode,
                Func<object, object> buildFromAttribute,
                FuncConverter<TMessage, TAttribute, TType> converter) : base(cloner, param)
            {
                this._buildFromAttribute = buildFromAttribute;
                this._mode = mode;
                this._converter = converter;
            }

            public static ExactBinding<TMessage> TryBuild(
                AsyncCollectorBindingProvider<TAttribute, TType> parent,
                Mode mode,
                BindingProviderContext context)
            {                
                var patternMatcher = parent._patternMatcher;

                var parameter = context.Parameter;
                var attributeSource = TypeUtility.GetResolvedAttribute<TAttribute>(parameter);
                   
                Func<object, object> buildFromAttribute;
                FuncConverter<TMessage, TAttribute, TType> converter = null;

                // Prefer the shortest route to creating the user type.
                // If TType matches the user type directly, then we should be able to directly invoke the builder in a single step. 
                //   TAttribute --> TUserType
                var checker = ConverterManager.GetTypeValidator<TType>();
                if (checker.IsMatch(typeof(TMessage)))
                {
                    buildFromAttribute = patternMatcher.TryGetConverterFunc(
                        typeof(TAttribute), typeof(IAsyncCollector<TMessage>));
                }
                else
                {
                    var converterManager = parent._converterManager;

                    // Try with a converter
                    // Find a builder for :   TAttribute --> TType
                    // and then couple with a converter:  TType --> TParameterType
                    converter = converterManager.GetConverter<TMessage, TType, TAttribute>();
                    if (converter == null)
                    {
                        // Preserves legacy behavior. This means we can only have 1 async collector.
                        // However, the collector's builder object can switch. 
                        throw NewMissingConversionError(typeof(TMessage));
                    }

                    buildFromAttribute = patternMatcher.TryGetConverterFunc(
                        typeof(TAttribute), typeof(IAsyncCollector<TType>));
                }

                if (buildFromAttribute == null)
                {
                    return null;
                }

                ParameterDescriptor param;
                if (parent.BuildParameterDescriptor != null)
                {
                    param = parent.BuildParameterDescriptor(attributeSource, parameter, parent._nameResolver);
                }
                else
                {
                    param = new ParameterDescriptor
                    {
                        Name = parameter.Name,
                        DisplayHints = new ParameterDisplayHints
                        {
                            Description = "output"
                        }
                    };
                }

                var cloner = new AttributeCloner<TAttribute>(attributeSource, context.BindingDataContract, parent._nameResolver);
                return new ExactBinding<TMessage>(cloner, param, mode, buildFromAttribute, converter);
            }

            // typeUser - type in the user's parameter. 
            private static Exception NewMissingConversionError(Type typeUser)
            {
                if (typeUser.IsPrimitive)
                {
                    return new NotSupportedException("Primitive types are not supported.");
                }

                if (typeof(IEnumerable).IsAssignableFrom(typeUser))
                {
                    return new InvalidOperationException("Nested collections are not supported.");
                }
                return new InvalidOperationException("Can't convert from type '" + typeUser.FullName);
            }

            protected override Task<IValueProvider> BuildAsync(
                TAttribute attrResolved,
                ValueBindingContext context)
            {
                string invokeString = Cloner.GetInvokeString(attrResolved);

                object obj = _buildFromAttribute(attrResolved);
                
                IAsyncCollector<TMessage> collector;
                if (_converter != null)
                {
                    // Apply a converter
                    var innerCollector = (IAsyncCollector<TType>)obj;

                    collector = new TypedAsyncCollectorAdapter<TMessage, TType, TAttribute>(
                                innerCollector, _converter, attrResolved, context);
                }
                else
                {
                    collector = (IAsyncCollector<TMessage>)obj;
                }

                var vp = CoerceValueProvider(_mode, invokeString, collector);
                return Task.FromResult(vp);
            }

            // Get a ValueProvider that's in the right mode. 
            private static IValueProvider CoerceValueProvider(Mode mode, string invokeString, IAsyncCollector<TMessage> collector)
            {
                switch (mode)
                {
                    case Mode.IAsyncCollector:
                        return new AsyncCollectorValueProvider<IAsyncCollector<TMessage>, TMessage>(collector, collector, invokeString);

                    case Mode.ICollector:
                        ICollector<TMessage> syncCollector = new SyncAsyncCollectorAdapter<TMessage>(collector);
                        return new AsyncCollectorValueProvider<ICollector<TMessage>, TMessage>(syncCollector, collector, invokeString);

                    case Mode.OutArray:
                        return new OutArrayValueProvider<TMessage>(collector, invokeString);
                        
                    case Mode.OutSingle:
                        return new OutValueProvider<TMessage>(collector, invokeString);
                        
                    default:
                        throw new NotImplementedException($"mode ${mode} not implemented");
                }             
            }
        }
    }
}
