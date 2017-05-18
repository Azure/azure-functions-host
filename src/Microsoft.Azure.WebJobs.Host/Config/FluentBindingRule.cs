// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;
using static Microsoft.Azure.WebJobs.Host.Bindings.BindingFactory;

namespace Microsoft.Azure.WebJobs.Host.Config
{
    /// <summary>
    /// Helpers for adding binding rules to a given attribute.
    /// </summary>
    /// <typeparam name="TAttribute"></typeparam>
    public class FluentBindingRule<TAttribute> : FluentConverterRules<TAttribute, FluentBindingRule<TAttribute>>
        where TAttribute : Attribute
    {
        private readonly JobHostConfiguration _parent;

        private List<IBindingProvider> _binders = new List<IBindingProvider>();

        // Filters to apply to current binder
        private List<Func<TAttribute, bool>> _filters = new List<Func<TAttribute, bool>>();
        private StringBuilder _filterDescription = new StringBuilder();

        private Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> _hook;

        private Action<TAttribute, Type> _validator;

        internal FluentBindingRule(JobHostConfiguration parent)
        {
            _parent = parent;
        }

        internal override IConverterManager Converters
        {
            get
            {
                return _parent.ConverterManager;
            }
        }

        #region Filters

        private static PropertyInfo ResolveProperty(string propertyName)
        {
            var prop = typeof(TAttribute).GetProperty(propertyName);
            if (prop == null || !prop.CanRead)
            {
                throw new InvalidOperationException($"Attribute type {typeof(TAttribute).Name} does not have readable property '{propertyName}'");
            }
            return prop;
        }

        private void AppendFilter(string propertyName, string formatString)
        {
            if (_filterDescription.Length > 0)
            {
                _filterDescription.Append(" && ");
            }
            _filterDescription.AppendFormat(formatString, propertyName);
        }
         
        /// <summary>
        /// The subsequent Bind* operations only apply when the Attribute's property is null. 
        /// </summary>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public FluentBindingRule<TAttribute> WhenIsNull(string propertyName)
        {
            var prop = ResolveProperty(propertyName);
            Func<TAttribute, bool> func = (attribute) =>
            {
                var value = prop.GetValue(attribute);
                return value == null;
            };
            _filters.Add(func);
            AppendFilter(propertyName, "({0} == null)");

            return this;
        }

        /// <summary>
        /// The subsequent Bind* operations only apply when the Attribute's property is not null. 
        /// </summary>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public FluentBindingRule<TAttribute> WhenIsNotNull(string propertyName)
        {
            var prop = ResolveProperty(propertyName);
            Func<TAttribute, bool> func = (attribute) =>
            {
                var value = prop.GetValue(attribute);
                return value != null;
            };
            _filters.Add(func);
            AppendFilter(propertyName, "({0} != null)");

            return this;
        }

        internal FluentBindingRule<TAttribute> SetPostResolveHook(Func<TAttribute, ParameterInfo, INameResolver, ParameterDescriptor> hook)
        {
            _hook = hook;
            return this;
        }
        #endregion // Filters

        #region BindToInput
        /// <summary>
        /// Bind an attribute to the given input, using the converter manager. 
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="builderInstance"></param>
        /// <returns></returns>
        public void BindToInput<TType>(IConverter<TAttribute, TType> builderInstance)
        {
            var bf = _parent.BindingFactory;
            var rule = bf.BindToInput<TAttribute, TType>(builderInstance);
            Bind(rule);
        }

        /// <summary>
        /// Bind an attribute to the given input, using the converter manager. 
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="builderInstance"></param>
        /// <returns></returns>
        public void BindToInput<TType>(IAsyncConverter<TAttribute, TType> builderInstance)
        {
            var bf = _parent.BindingFactory;

            var pm = PatternMatcher.New(builderInstance);
            var rule = new BindToInputBindingProvider<TAttribute, TType>(bf.NameResolver, bf.ConverterManager, pm);
            Bind(rule);
        }

        /// <summary>
        /// General rule for binding to an generic input type for a given attribute. 
        /// </summary>
        /// <typeparam name="TType">The user type must be compatible with this type for the binding to apply.</typeparam>
        /// <param name="builderType">A that implements IConverter for the target parameter. 
        /// This will get instantiated with the appropriate generic args to perform the builder rule.</param>
        /// <param name="constructorArgs">constructor arguments to pass to the typeBuilder instantiation. This can be used 
        /// to flow state (like configuration, secrets, etc) from the configuration to the specific binding</param>
        /// <returns>A binding rule.</returns>
        public void BindToInput<TType>(
            Type builderType,
            params object[] constructorArgs)
        {
            var bf = _parent.BindingFactory;
            var rule = bf.BindToInput<TAttribute, TType>(builderType, constructorArgs);
            Bind(rule);
        }

        /// <summary>
        /// Bind an attribute to the given input, using the supplied delegate to build the input from an resolved 
        /// instance of the attribute. 
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="builder"></param>
        /// <returns></returns>
        public void BindToInput<TType>(Func<TAttribute, TType> builder)
        {
            var builderInstance = new DelegateConverterBuilder<TAttribute, TType> { BuildFromAttribute = builder };
            this.BindToInput<TType>(builderInstance);
        }

        #endregion // BindToInput

        /// <summary>
        /// Add a general binder.
        /// </summary>
        /// <param name="binder"></param>
        /// <returns></returns>
        public void Bind(IBindingProvider binder)
        {
            if (this._hook != null)
            {
                var fluidBinder = (FluentBindingProvider<TAttribute>)binder;
                fluidBinder.BuildParameterDescriptor = _hook;
                _hook = null;
            }

            // Apply filters
            if (this._filters.Count > 0)
            {
                var filters = this._filters.ToArray(); // produce copy 
                Func<TAttribute, Type, bool> predicate = (attribute, type) =>
                {                    
                    foreach (var filter in filters)
                    {
                        if (!filter(attribute))
                        {
                            return false;
                        }
                    }
                    return true;
                };
                binder = new FilteringBindingProvider<TAttribute>(
                    predicate, 
                    this._parent.NameResolver, 
                    binder, 
                    this._filterDescription.ToString());

                this._filterDescription.Clear();
                this._filters.Clear();    
            }

            _binders.Add(binder);
        }

        #region BindToCollector
        /// <summary>
        /// Bind to a collector 
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="buildFromAttribute"></param>
        /// <returns></returns>
        public void BindToCollector<TMessage>(
            Func<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute)
        {
            var converter = new DelegateConverterBuilder<TAttribute, IAsyncCollector<TMessage>> { BuildFromAttribute = buildFromAttribute };

            BindToCollector(converter);
        }

        /// <summary>
        /// Bind to a collector. 
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="buildFromAttribute"></param>
        /// <returns></returns>
        public void BindToCollector<TMessage>(
           IConverter<TAttribute, IAsyncCollector<TMessage>> buildFromAttribute)
        {
            var bf = _parent.BindingFactory;
            var pm = PatternMatcher.New(buildFromAttribute);
            var rule = new AsyncCollectorBindingProvider<TAttribute, TMessage>(bf.NameResolver, bf.ConverterManager, pm);

            Bind(rule);
        }

        /// <summary>
        /// Bind to a collector. 
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="builderType"></param>
        /// <param name="constructorArgs"></param>
        /// <returns></returns>
        public void BindToCollector<TMessage>(
             Type builderType,
             params object[] constructorArgs)
        {
            var bf = _parent.BindingFactory;
            var pm = PatternMatcher.New(builderType, constructorArgs);
            var rule = new AsyncCollectorBindingProvider<TAttribute, TMessage>(bf.NameResolver, bf.ConverterManager, pm);
            Bind(rule);
        }

        #endregion // BindToCollector

        /// <summary>
        /// Setup a trigger binding for this attribute
        /// </summary>
        /// <param name="trigger"></param>
        public void BindToTrigger(ITriggerBindingProvider trigger)
        {
            if (_binders.Count > 0)
            {
                throw new InvalidOperationException($"The same attribute can't be bound to trigger and non-trigger bindings");
            }
            IExtensionRegistry extensions = _parent.GetService<IExtensionRegistry>();
            extensions.RegisterExtension<ITriggerBindingProvider>(trigger);
        }

        /// <summary>
        /// Add a validator for the set of rules. 
        /// The validator will apply to all of these rules. 
        /// </summary>
        /// <param name="validator"></param>
        /// <returns></returns>
        [Obsolete("move this directly onto the attribute")]
        public FluentBindingRule<TAttribute> AddValidator(Action<TAttribute, Type> validator)
        {
            if (_validator != null)
            {
                throw new InvalidOperationException("Validator already set");
            }
            _validator = validator;
            return this;
        }

        internal void DebugDumpGraph(TextWriter output)
        {
            var binding = CreateBinding() as IBindingRuleProvider;
            JobHostMetadataProvider.DumpRule(binding, output);
        }

        private IBindingProvider CreateBinding()
        {
            var all = new GenericCompositeBindingProvider<TAttribute>(_validator, this._parent.NameResolver, _binders.ToArray());
            return all;
        }

        // Called by infrastructure after the extension is invoked.
        // This applies all changes accumulated on the fluent object. 
        internal void ApplyRules()
        {
            if (_hook != null)
            {
                throw new InvalidOperationException("SetPostResolveHook() should be called before the Bind() it applies to.");
            }
            if (_filters.Count > 0)
            {
                throw new InvalidOperationException($"Filters ({_filterDescription}) should be called before the Bind() they apply to.");
            }

            if (_binders.Count > 0)
            {
                IExtensionRegistry extensions = _parent.GetService<IExtensionRegistry>();  
                var binding = CreateBinding();
                extensions.RegisterExtension<IBindingProvider>(binding);
                _binders.Clear();
            }
        }
    }
}
