// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // General rule for binding to input parameters.
    // Can invoke Converter manager. 
    // Can leverage OpenTypes for pattern matchers.
    internal class BindToInputBindingProvider<TAttribute, TType> : FluentBindingProvider<TAttribute>, IBindingProvider
        where TAttribute : Attribute
    {
        private readonly INameResolver _nameResolver;
        private readonly IConverterManager _converterManager;
        private readonly PatternMatcher _patternMatcher; 
        
        public BindToInputBindingProvider(
            INameResolver nameResolver,
            IConverterManager converterManager,
            PatternMatcher patternMatcher)
        {
            this._nameResolver = nameResolver;
            this._converterManager = converterManager;
            this._patternMatcher = patternMatcher;
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            var parameter = context.Parameter;
            var typeUser = parameter.ParameterType;

            if (typeUser.IsByRef)
            {
                return Task.FromResult<IBinding>(null);
            }

            var type = typeof(ExactBinding<>).MakeGenericType(typeof(TAttribute), typeof(TType), typeUser);
            var method = type.GetMethod("TryBuild", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var binding = BindingFactoryHelpers.MethodInvoke<IBinding>(method, this, context);
                
            return Task.FromResult<IBinding>(binding);
        }

        private class ExactBinding<TUserType> : BindingBase<TAttribute>
        {
            private readonly Func<object, object> _buildFromAttribute;

            private readonly FuncConverter<TType, TAttribute, TUserType> _converter;

            public ExactBinding(
                AttributeCloner<TAttribute> cloner,
                ParameterDescriptor param,
                Func<object, object> buildFromAttribute,
                FuncConverter<TType, TAttribute, TUserType> converter) : base(cloner, param)
            {
                this._buildFromAttribute = buildFromAttribute;
                this._converter = converter;
            }

            public static ExactBinding<TUserType> TryBuild(
                BindToInputBindingProvider<TAttribute, TType> parent,
                BindingProviderContext context)
            {
                var cm = parent._converterManager;
                var patternMatcher = parent._patternMatcher;

                var parameter = context.Parameter;
                var attributeSource = TypeUtility.GetResolvedAttribute<TAttribute>(parameter);

                Func<TAttribute, Task<TAttribute>> hookWrapper = null;
                if (parent.PostResolveHook != null)
                {
                    hookWrapper = (attrResolved) => parent.PostResolveHook(attrResolved, parameter, parent._nameResolver);
                }

                var cloner = new AttributeCloner<TAttribute>(attributeSource, context.BindingDataContract, parent._nameResolver, hookWrapper);

                Func<object, object> buildFromAttribute;
                FuncConverter<TType, TAttribute, TUserType> converter = null;
                                
                // Prefer the shortest route to creating the user type.
                // If TType matches the user type directly, then we should be able to directly invoke the builder in a single step. 
                //   TAttribute --> TUserType
                var checker = ConverterManager.GetTypeValidator<TType>();
                if (checker.IsMatch(typeof(TUserType)))
                {
                    buildFromAttribute = patternMatcher.TryGetConverterFunc(typeof(TAttribute), typeof(TUserType));
                }
                else
                {
                    // Try with a converter
                    // Find a builder for :   TAttribute --> TType
                    // and then couple with a converter:  TType --> TParameterType
                    converter = cm.GetConverter<TType, TUserType, TAttribute>();
                    if (converter == null)
                    {
                        return null;
                    }

                    buildFromAttribute = patternMatcher.TryGetConverterFunc(typeof(TAttribute), typeof(TType));
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
                            Description = "input"
                        }
                    };
                }

                return new ExactBinding<TUserType>(cloner, param, buildFromAttribute, converter);
            }

            protected override Task<IValueProvider> BuildAsync(
                TAttribute attrResolved, 
                ValueBindingContext context)
            {
                string invokeString = Cloner.GetInvokeString(attrResolved);

                object obj = _buildFromAttribute(attrResolved);
                TUserType finalObj;
                if (_converter == null)
                {
                    finalObj = (TUserType)obj;
                }
                else
                {
                    var intermediateObj = (TType)obj;
                    finalObj = _converter(intermediateObj, attrResolved, context);
                }

                IValueProvider vp = new ConstantValueProvider(finalObj, typeof(TUserType), invokeString);

                return Task.FromResult(vp);
            }
        }
    }
}
