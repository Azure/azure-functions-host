// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{    
    using BindingData = IReadOnlyDictionary<string, object>;
    using BindingDataContract = IReadOnlyDictionary<string, System.Type>;
    // Func to transform Attribute,BindingData into value for cloned attribute property/constructor arg
    // Attribute is the new cloned attribute - null if constructor arg (new cloned attr not created yet)
    using BindingDataResolver = Func<Attribute, IReadOnlyDictionary<string, object>, object>;

    // Clone an attribute and resolve it.
    // This can be tricky since some read-only properties are set via the constructor.
    // This assumes that the property name matches the constructor argument name.
    internal class AttributeCloner<TAttribute>
        where TAttribute : Attribute
    {
        private readonly TAttribute _source;

        // Which constructor do we invoke to instantiate the new attribute?
        // The attribute is configured through a) constructor arguments, b) settable properties.
        private readonly ConstructorInfo _matchedCtor;

        // Compute the arguments to pass to the chosen constructor. Arguments are based on binding data.
        private readonly BindingDataResolver[] _ctorParamResolvers;

        // Compute the values to apply to Settable properties on newly created attribute.
        private readonly Action<TAttribute, BindingData>[] _propertySetters;

        private readonly Dictionary<PropertyInfo, AutoResolveAttribute> _autoResolves = new Dictionary<PropertyInfo, AutoResolveAttribute>();

        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;

        internal AttributeCloner(
            TAttribute source,
            BindingDataContract bindingDataContract,
            INameResolver nameResolver = null)
        {
            nameResolver = nameResolver ?? new EmptyNameResolver();
            _source = source;

            Type attributeType = typeof(TAttribute);

            PropertyInfo[] allProperties = attributeType.GetProperties(Flags);

            // Create dictionary of all non-null properties on source attribute.
            Dictionary<string, PropertyInfo> nonNullProps = allProperties
                .Where(prop => prop.GetValue(source) != null)
                .ToDictionary(prop => prop.Name, prop => prop, StringComparer.OrdinalIgnoreCase);

            // Pick the ctor with the longest parameter list where all are matched to non-null props.
            var ctorAndParams = attributeType.GetConstructors(Flags)
                .Select(ctor => new { ctor = ctor, parameters = ctor.GetParameters() })
                .OrderByDescending(tuple => tuple.parameters.Length)
                .FirstOrDefault(tuple => tuple.parameters.All(param => nonNullProps.ContainsKey(param.Name)));

            if (ctorAndParams == null)
            {
                throw new InvalidOperationException("Can't figure out which ctor to call.");
            }
           
            _matchedCtor = ctorAndParams.ctor;

            // Get appropriate binding data resolver (appsetting, autoresolve, or originalValue) for each constructor parameter
            _ctorParamResolvers = ctorAndParams.parameters
                .Select(param => GetResolver(nonNullProps[param.Name], nameResolver, bindingDataContract))
                .ToArray();

            // Get appropriate binding data resolver (appsetting, autoresolve, or originalValue) for each writeable property
            _propertySetters = allProperties
                .Where(prop => prop.CanWrite)
                .Select(prop =>
                {
                    var resolver = GetResolver(prop, nameResolver, bindingDataContract);
                    return (Action<TAttribute, BindingData>)((attr, data) => prop.SetValue(attr, resolver(attr, data)));
                })
                .ToArray();
        }

        // transforms binding data to appropriate resolver (appsetting, autoresolve, or originalValue)
        private BindingDataResolver GetResolver(PropertyInfo propInfo, INameResolver nameResolver, BindingDataContract contract)
        {
            object originalValue = propInfo.GetValue(_source);
            AppSettingAttribute appSettingAttr = propInfo.GetCustomAttribute<AppSettingAttribute>();
            AutoResolveAttribute autoResolveAttr = propInfo.GetCustomAttribute<AutoResolveAttribute>();

            if (appSettingAttr == null && autoResolveAttr == null)
            {
                // No special attributes, treat as literal. 
                return (newAttr, bindingData) => originalValue;
            }

            if (appSettingAttr != null && autoResolveAttr != null)
            {
                throw new InvalidOperationException($"Property '{propInfo.Name}' cannot be annotated with both AppSetting and AutoResolve.");
            }

            // attributes only work on string properties. 
            if (propInfo.PropertyType != typeof(string))
            {
                throw new InvalidOperationException($"AutoResolve or AppSetting property '{propInfo.Name}' must be of type string.");
            }
            var str = (string)originalValue;

            // first try to resolve with app setting
            if (appSettingAttr != null)
            {
                return GetAppSettingResolver(str, appSettingAttr, nameResolver, propInfo);
            }

            // Must have an [AutoResolve]
            // try to resolve with auto resolve ({...}, %...%)
            return GetAutoResolveResolver(str, autoResolveAttr, nameResolver, propInfo, contract);            
        }

        // Apply AutoResolve attribute 
        internal BindingDataResolver GetAutoResolveResolver(string originalValue, AutoResolveAttribute autoResolveAttr, INameResolver nameResolver, PropertyInfo propInfo, BindingDataContract contract)
        {
            if (string.IsNullOrWhiteSpace(originalValue))
            {
                if (autoResolveAttr.Default != null)
                {
                    return GetBuiltinTemplateResolver(autoResolveAttr.Default, nameResolver);
                }
                else
                {
                    return (newAttr, bindingData) => originalValue;
                }
            }
            else
            {
                _autoResolves[propInfo] = autoResolveAttr;
                return GetTemplateResolver(originalValue, autoResolveAttr, nameResolver, propInfo, contract);
            }
        }

        // Apply AppSetting attribute. 
        internal static BindingDataResolver GetAppSettingResolver(string originalValue, AppSettingAttribute attr, INameResolver nameResolver, PropertyInfo propInfo)
        {
            string appSettingName = originalValue ?? attr.Default;
            string resolvedValue = string.IsNullOrEmpty(appSettingName) ?
                originalValue : nameResolver.Resolve(appSettingName);

            // If a value is non-null and cannot be found, we throw to match the behavior
            // when %% values are not found in ResolveWholeString below.
            if (resolvedValue == null && originalValue != null)
            {
                // It's important that we only log the attribute property name, not the actual value to ensure
                // that in cases where users accidentally use a secret key *value* rather than indirect setting name
                // that value doesn't get written to logs.
                throw new InvalidOperationException($"Unable to resolve app setting for property '{propInfo.DeclaringType.Name}.{propInfo.Name}'. Make sure the app setting exists and has a valid value.");
            }
            return (newAttr, bindingData) => resolvedValue;
        }

        // Resolve for AutoResolve.Default templates. 
        // These only have access to the {sys} builtin variable and don't get access to trigger binding data. 
        internal static BindingDataResolver GetBuiltinTemplateResolver(string originalValue, INameResolver nameResolver)
        {
            string resolvedValue = nameResolver.ResolveWholeString(originalValue);

            var template = BindingTemplate.FromString(resolvedValue);

            SystemBindingData.ValidateStaticContract(template);

            // For static default contracts, we only have access to the built in binding data. 
            return (newAttr, bindingData) => template.Bind(SystemBindingData.GetSystemBindingData(bindingData));
        }

        // AutoResolve
        internal static BindingDataResolver GetTemplateResolver(string originalValue, AutoResolveAttribute attr, INameResolver nameResolver, PropertyInfo propInfo, BindingDataContract contract)
        {
            string resolvedValue = nameResolver.ResolveWholeString(originalValue);
            var template = BindingTemplate.FromString(resolvedValue);
            IResolutionPolicy policy = GetPolicy(attr.ResolutionPolicyType, propInfo);
            template.ValidateContractCompatibility(contract);
            return (newAttr, bindingData) => TemplateBind(policy, propInfo, newAttr, template, bindingData);
        }

        // Get a attribute with %% resolved, but not runtime {} resolved.
        public TAttribute GetNameResolvedAttribute()
        {
            TAttribute attr = ResolveFromBindings(null);
            return attr;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public string GetInvokeString(TAttribute attributeResolved)
        {
            string invokeString;

            var resolver = _source as IAttributeInvokeDescriptor<TAttribute>;
            if (resolver == null)
            {
                invokeString = DefaultAttributeInvokerDescriptor<TAttribute>.ToInvokeString(_autoResolves, attributeResolved);
            }
            else
            {
                invokeString = resolver.ToInvokeString();
            }
            return invokeString;
        }

        public TAttribute ResolveFromInvokeString(string invokeString)
        {
            TAttribute attr;
            var resolver = _source as IAttributeInvokeDescriptor<TAttribute>;
            if (resolver == null)
            {
                attr = DefaultAttributeInvokerDescriptor<TAttribute>.FromInvokeString(this, invokeString);
            }
            else
            {
                attr = resolver.FromInvokeString(invokeString);
            }
            return attr;
        }

        public TAttribute ResolveFromBindingData(BindingContext ctx)
        {
            var attr = ResolveFromBindings(ctx.BindingData);
            return attr;
        }

        // When there's only 1 resolvable property
        internal TAttribute New(string invokeString)
        {
            if (_autoResolves.Count() != 1)
            {
                throw new InvalidOperationException("Invalid invoke string format for attribute.");
            }
            var overrideProps = _autoResolves.Select(pair => pair.Key)
                .ToDictionary(prop => prop.Name, prop => invokeString, StringComparer.OrdinalIgnoreCase);
            return New(overrideProps);
        }

        // Clone the source attribute, but override the properties with the supplied.
        internal TAttribute New(IDictionary<string, string> overrideProperties)
        {
            IDictionary<string, object> propertyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Populate inititial properties from the source
            Type t = typeof(TAttribute);
            var properties = t.GetProperties(Flags);
            foreach (var prop in properties)
            {
                propertyValues[prop.Name] = prop.GetValue(_source);
            }

            foreach (var kv in overrideProperties)
            {
                propertyValues[kv.Key] = kv.Value;
            }

            var ctorArgs = Array.ConvertAll(_matchedCtor.GetParameters(), param => propertyValues[param.Name]);
            var newAttr = (TAttribute)_matchedCtor.Invoke(ctorArgs);

            foreach (var prop in properties)
            {
                if (prop.CanWrite)
                {
                    var val = propertyValues[prop.Name];
                    prop.SetValue(newAttr, val);
                }
            }
            return newAttr;
        }

        internal TAttribute ResolveFromBindings(BindingData bindingData)
        {
            // Invoke ctor
            var ctorArgs = Array.ConvertAll(_ctorParamResolvers, func => func(_source, bindingData));
            var newAttr = (TAttribute)_matchedCtor.Invoke(ctorArgs);

            foreach (var setProp in _propertySetters)
            {
                setProp(newAttr, bindingData);
            }

            return newAttr;
        }

        private static string TemplateBind(IResolutionPolicy policy, PropertyInfo prop, Attribute attr, BindingTemplate template, BindingData bindingData)
        {
            if (bindingData == null)
            {
                return template?.Pattern;
            }

            return policy.TemplateBind(prop, attr, template, bindingData);
        }

        internal static IResolutionPolicy GetPolicy(Type formatterType, PropertyInfo propInfo)
        { 
            if (formatterType != null)
            {
                // Special-case Table as there is no way to declare this ResolutionPolicy
                // and use BindingTemplate in the Core assembly
                if (formatterType == typeof(WebJobs.ODataFilterResolutionPolicy))
                {
                    return new ODataFilterResolutionPolicy();
                }

                if (!typeof(IResolutionPolicy).IsAssignableFrom(formatterType))
                {
                    throw new InvalidOperationException($"The {nameof(AutoResolveAttribute.ResolutionPolicyType)} on {propInfo.Name} must derive from {typeof(IResolutionPolicy).Name}.");
                }

                try
                {
                    var obj = Activator.CreateInstance(formatterType);
                    return (IResolutionPolicy)obj;
                }
                catch (MissingMethodException)
                {
                    throw new InvalidOperationException($"The {nameof(AutoResolveAttribute.ResolutionPolicyType)} on {propInfo.Name} must derive from {typeof(IResolutionPolicy).Name} and have a default constructor.");
                }
            }

            // return the default policy                        
            return new DefaultResolutionPolicy();
        }

        // If no name resolver is specified, then any %% becomes an error.
        private class EmptyNameResolver : INameResolver
        {
            public string Resolve(string name) => null;
        }
    }
}