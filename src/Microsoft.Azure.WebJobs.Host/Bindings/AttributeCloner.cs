// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Tables;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    // Clone an attribute and resolve it.
    // This can be tricky since some read-only properties are set via the constructor.
    // This assumes that the property name matches the constructor argument name.
    internal class AttributeCloner<TAttribute>
        where TAttribute : Attribute
    {
        private readonly TAttribute _source;

        // Which constructor do we invoke to instantiate the new attribute?
        // The attribute is configured through a) constructor arguments, b) settable properties.
        private readonly ConstructorInfo _bestCtor;

        // Compute the arguments to pass to the chosen constructor. Arguments are based on binding data.
        private readonly Func<IReadOnlyDictionary<string, object>, object>[] _bestCtorArgBuilder;

        // Compute the values to apply to Settable properties on newly created attribute.
        private readonly Action<TAttribute, IReadOnlyDictionary<string, object>>[] _setProperties;

        // Optional hook for post-processing the attribute. This is intended for legacy hack rules.
        private readonly Func<TAttribute, Task<TAttribute>> _hook;

        public AttributeCloner(
            TAttribute source,
            IReadOnlyDictionary<string, Type> bindingDataContract,
            INameResolver nameResolver = null,
            Func<TAttribute, Task<TAttribute>> hook = null)
        {
            _hook = hook;
            nameResolver = nameResolver ?? new EmptyNameResolver();
            _source = source;

            Type t = typeof(TAttribute);

            Dictionary<string, PropertyInfo> availableParams = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var objValue = prop.GetValue(_source);
                if (objValue != null)
                {
                    availableParams[prop.Name] = prop;
                }
            }

            int longestMatch = -1;

            // Pick the ctor with the longest parameter list where all parameters are matched.
            var ctors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var ctor in ctors)
            {
                var ps = ctor.GetParameters();
                int len = ps.Length;

                var getArgFuncs = new Func<IReadOnlyDictionary<string, object>, object>[len];

                bool hasAllParameters = true;
                for (int i = 0; i < len; i++)
                {
                    var p = ps[i];
                    PropertyInfo propInfo = null;
                    if (!availableParams.TryGetValue(p.Name, out propInfo))
                    {
                        hasAllParameters = false;
                        break;
                    }

                    BindingTemplate template;
                    if (TryCreateAutoResolveBindingTemplate(propInfo, nameResolver, out template))
                    {
                        var policy = ResolutionPolicy.GetPolicy(propInfo);
                        template.ValidateContractCompatibility(bindingDataContract);
                        getArgFuncs[i] = (bindingData) => policy.TemplateBind(template, bindingData);
                    }
                    else
                    {
                        var propValue = propInfo.GetValue(_source);
                        getArgFuncs[i] = (bindingData) => propValue;
                    }
                }

                if (hasAllParameters)
                {
                    if (len > longestMatch)
                    {
                        var setProperties = new List<Action<TAttribute, IReadOnlyDictionary<string, object>>>();

                        // Record properties too.
                        foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        {
                            if (!prop.CanWrite)
                            {
                                continue;
                            }

                            BindingTemplate template;
                            if (TryCreateAutoResolveBindingTemplate(prop, nameResolver, out template))
                            {
                                var policy = ResolutionPolicy.GetPolicy(prop);
                                template.ValidateContractCompatibility(bindingDataContract);
                                setProperties.Add((newAttr, bindingData) => prop.SetValue(newAttr, policy.TemplateBind(template, bindingData)));
                            }
                            else
                            {
                                var objValue = prop.GetValue(_source);
                                setProperties.Add((newAttr, bindingData) => prop.SetValue(newAttr, objValue));
                            }
                        }

                        _setProperties = setProperties.ToArray();
                        _bestCtor = ctor;
                        longestMatch = len;
                        _bestCtorArgBuilder = getArgFuncs;
                    }
                }
            }

            if (_bestCtor == null)
            {
                // error!!!
                throw new InvalidOperationException("Can't figure out which ctor to call.");
            }
        }

        private bool TryCreateAutoResolveBindingTemplate(PropertyInfo propInfo, INameResolver nameResolver, out BindingTemplate template)
        {
            template = null;

            string resolvedValue = null;
            if (!TryAutoResolveValue(_source, propInfo, nameResolver, out resolvedValue))
            {
                return false;
            }

            template = BindingTemplate.FromString(resolvedValue);

            return true;
        }

        internal static bool TryAutoResolveValue(TAttribute attribute, PropertyInfo propInfo, INameResolver nameResolver, out string resolvedValue)
        {
            resolvedValue = null;

            AutoResolveAttribute attr = propInfo.GetCustomAttribute<AutoResolveAttribute>();
            if (attr == null)
            {
                return false;
            }

            string originalValue = (string)propInfo.GetValue(attribute);
            if (originalValue == null)
            {
                return false;
            }

            if (!attr.AllowTokens)
            {
                resolvedValue = nameResolver.Resolve(originalValue);

                // If a value is non-null and cannot be found, we throw to match the behavior
                // when %% values are not found in ResolveWholeString below.
                if (resolvedValue == null)
                {
                    // It's important that we only log the attribute property name, not the actual value to ensure
                    // that in cases where users accidentally use a secret key *value* rather than indirect setting name
                    // that value doesn't get written to logs.
                    throw new InvalidOperationException($"Unable to resolve value for property '{propInfo.DeclaringType.Name}.{propInfo.Name}'.");
                }
            }
            else
            {
                // The logging consideration above doesn't apply in this case, since only tokens wrapped
                // in %% characters will be resolved, so they are less likely to include a secret value.
                resolvedValue = nameResolver.ResolveWholeString(originalValue);
            }

            return true;
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
                invokeString = DefaultAttributeInvokerDescriptor<TAttribute>.ToInvokeString(attributeResolved);
            }
            else
            {
                invokeString = resolver.ToInvokeString();
            }
            return invokeString;
        }

        public async Task<TAttribute> ResolveFromInvokeStringAsync(string invokeString)
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
            if (_hook != null)
            {
                attr = await _hook(attr);
            }
            return attr;
        }

        public async Task<TAttribute> ResolveFromBindingDataAsync(BindingContext ctx)
        {
            var attr = ResolveFromBindings(ctx.BindingData);
            if (_hook != null)
            {
                attr = await _hook(attr);
            }
            return attr;
        }

        // When there's only 1 resolvable property
        internal TAttribute New(string invokeString)
        {
            IDictionary<string, string> overrideProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Type t = typeof(TAttribute);
            foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                bool resolve = prop.GetCustomAttribute<AutoResolveAttribute>() != null;
                if (resolve)
                {
                    overrideProperties[prop.Name] = invokeString;
                }
            }
            if (overrideProperties.Count != 1)
            {
                throw new InvalidOperationException("Invalid invoke string format for attribute.");
            }
            return New(overrideProperties);
        }

        // Clone the source attribute, but override the properties with the supplied.
        internal TAttribute New(IDictionary<string, string> overrideProperties)
        {
            IDictionary<string, object> propertyValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Populate inititial properties from the source
            Type t = typeof(TAttribute);
            foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                propertyValues[prop.Name] = prop.GetValue(_source);
            }

            foreach (var kv in overrideProperties)
            {
                propertyValues[kv.Key] = kv.Value;
            }

            var ctorArgs = Array.ConvertAll(_bestCtor.GetParameters(), param => propertyValues[param.Name]);
            var newAttr = (TAttribute)_bestCtor.Invoke(ctorArgs);

            foreach (var prop in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (prop.CanWrite)
                {
                    var val = propertyValues[prop.Name];
                    prop.SetValue(newAttr, val);
                }
            }
            return newAttr;
        }

        internal TAttribute ResolveFromBindings(IReadOnlyDictionary<string, object> bindingData)
        {
            // Invoke ctor
            var ctorArgs = Array.ConvertAll(_bestCtorArgBuilder, func => func(bindingData));
            var newAttr = (TAttribute)_bestCtor.Invoke(ctorArgs);

            foreach (var setProp in _setProperties)
            {
                setProp(newAttr, bindingData);
            }

            return newAttr;
        }

        // Resolution policy for { } in  binding templates. 
        // The default policy is just a direct substitution for the binding data. 
        // Derived policies can enforce formatting / escaping when they do injection. 
        private class ResolutionPolicy
        {
            public virtual string TemplateBind(BindingTemplate template, IReadOnlyDictionary<string, object> bindingData)
            {
                if (bindingData == null)
                {
                    return template.Pattern;
                }
                return template.Bind(bindingData);
            }

            // Expand on [AutoResolve] property to make this pluggable, 
            public static ResolutionPolicy GetPolicy(PropertyInfo propInfo)
            {
                if (propInfo.DeclaringType == typeof(TableAttribute) && propInfo.Name == "Filter")
                {
                    return new TableFilterResolutionPolicy();
                }
                else
                {
                    return new ResolutionPolicy();
                }
            }
        }

        // Table.Filter's { } resolution does OData escaping. 
        private class TableFilterResolutionPolicy : ResolutionPolicy
        {
            public override string TemplateBind(BindingTemplate template, IReadOnlyDictionary<string, object> bindingData)
            {
                var filter = TableFilterFormatter.Format(template, bindingData);
                return filter;
            }
        }

        // If no name resolver is specified, then any %% becomes an error.
        private class EmptyNameResolver : INameResolver
        {
            public string Resolve(string name)
            {
                return null;
            }
        }
    }
}