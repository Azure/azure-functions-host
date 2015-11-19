// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionDescriptor
    {
        public FunctionDescriptor(string name, IFunctionInvoker invoker, Collection<ParameterDescriptor> parameters)
            : this(name, invoker, parameters, new Collection<CustomAttributeBuilder>())
        {
        }

        public FunctionDescriptor(string name, IFunctionInvoker invoker, Collection<ParameterDescriptor> parameters, Collection<CustomAttributeBuilder> attributes)
        {
            Name = name;
            Invoker = invoker;
            Parameters = parameters;
            CustomAttributes = attributes;
        }

        public string Name { get; private set; }

        public Collection<ParameterDescriptor> Parameters { get; private set; }

        public Collection<CustomAttributeBuilder> CustomAttributes { get; private set; }

        public IFunctionInvoker Invoker { get; private set; }

        public static FunctionDescriptor FromMethod(MethodInfo method, IFunctionInvoker invoker)
        {
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();
            foreach (var parameter in method.GetParameters())
            {
                var parameterAttributeBuilders = BuildCustomAttributes(parameter.CustomAttributes);
                ParameterDescriptor parameterDescriptor = new ParameterDescriptor(parameter.Name, parameter.ParameterType, new Collection<CustomAttributeBuilder>(parameterAttributeBuilders.ToList()));
                parameters.Add(parameterDescriptor);
            }

            var functionParameterBuilders = BuildCustomAttributes(method.CustomAttributes);

            return new FunctionDescriptor(method.Name, invoker, parameters, new Collection<CustomAttributeBuilder>(functionParameterBuilders.ToList()));
        }

        private static IEnumerable<CustomAttributeBuilder> BuildCustomAttributes(IEnumerable<CustomAttributeData> customAttributes)
        {
            return customAttributes.Select(attribute =>
            {
                var attributeArgs = attribute.ConstructorArguments.Select(a => a.Value).ToArray();
                var namedPropertyInfos = attribute.NamedArguments.Select(a => a.MemberInfo).OfType<PropertyInfo>().ToArray();
                var namedPropertyValues = attribute.NamedArguments.Where(a => a.MemberInfo is PropertyInfo).Select(a => a.TypedValue.Value).ToArray();
                var namedFieldInfos = attribute.NamedArguments.Select(a => a.MemberInfo).OfType<FieldInfo>().ToArray();
                var namedFieldValues = attribute.NamedArguments.Where(a => a.MemberInfo is FieldInfo).Select(a => a.TypedValue.Value).ToArray();
                return new CustomAttributeBuilder(attribute.Constructor, attributeArgs, namedPropertyInfos, namedPropertyValues, namedFieldInfos, namedFieldValues);
            });
        }
    }
}
