// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    // TODO: Make this type internal
    public static class FunctionGenerator
    {
        private static Dictionary<string, IFunctionInvoker> _invokerMap = new Dictionary<string, IFunctionInvoker>();

        // TODO: make this private
        public static IFunctionInvoker GetInvoker(string method)
        {
            return _invokerMap[method];
        }

        public static Type Generate(string typeName, Collection<FunctionDescriptor> functions)
        {
            Console.WriteLine(string.Format("Generating {0} job function(s)", functions.Count));

            AssemblyName aName = new AssemblyName("ScriptHost");
            AssemblyBuilder ab =
                AppDomain.CurrentDomain.DefineDynamicAssembly(
                    aName,
                    AssemblyBuilderAccess.RunAndSave);

            ModuleBuilder mb = ab.DefineDynamicModule(aName.Name, aName.Name + ".dll");

            TypeBuilder tb = mb.DefineType(typeName, TypeAttributes.Public);

            foreach (FunctionDescriptor descriptor in functions)
            {
                MethodBuilder methodBuilder = tb.DefineMethod(descriptor.Name, MethodAttributes.Public | MethodAttributes.Static);
                Type[] types = descriptor.Parameters.Select(p => p.Type).ToArray();
                methodBuilder.SetParameters(types);
                methodBuilder.SetReturnType(typeof(Task));

                for (int i = 0; i < descriptor.Parameters.Count; i++)
                {
                    ParameterDescriptor parameter = descriptor.Parameters[i];
                    ParameterBuilder parameterBuilder = methodBuilder.DefineParameter(i + 1, parameter.Attributes, parameter.Name);
                    if (parameter.CustomAttributes != null)
                    {
                        foreach (CustomAttributeBuilder attributeBuilder in parameter.CustomAttributes)
                        {
                            parameterBuilder.SetCustomAttribute(attributeBuilder);
                        }
                    }
                }

                _invokerMap[descriptor.Name] = descriptor.Invoker;

                MethodInfo invokeMethod = descriptor.Invoker.GetType().GetMethod("Invoke");
                MethodInfo getInvoker = typeof(FunctionGenerator).GetMethod("GetInvoker", BindingFlags.Static | BindingFlags.Public);

                ILGenerator il = methodBuilder.GetILGenerator();

                LocalBuilder argsLocal = il.DeclareLocal(typeof(object[]));
                LocalBuilder invokerLocal = il.DeclareLocal(typeof(IFunctionInvoker));

                il.Emit(OpCodes.Nop);

                // declare an array for all parameter values
                il.Emit(OpCodes.Ldc_I4, descriptor.Parameters.Count);
                il.Emit(OpCodes.Newarr, typeof(object));
                il.Emit(OpCodes.Stloc_0);

                // copy each parameter into the arg array
                for (int i = 0; i < descriptor.Parameters.Count; i++)
                {
                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldarg, i);
                    il.Emit(OpCodes.Stelem_Ref);
                }

                // get the invoker instance
                il.Emit(OpCodes.Ldstr, descriptor.Name);
                il.Emit(OpCodes.Call, getInvoker);
                il.Emit(OpCodes.Stloc_1);

                // now call the invoker, passing in the args
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Callvirt, invokeMethod);
                il.Emit(OpCodes.Ret);
            }

            Type t = tb.CreateType();

            return t;
        }

        private static CustomAttributeBuilder[] BuildCustomAttributes(IEnumerable<CustomAttributeData> customAttributes)
        {
            return customAttributes.Select(attribute =>
            {
                var attributeArgs = attribute.ConstructorArguments.Select(a => a.Value).ToArray();
                var namedPropertyInfos = attribute.NamedArguments.Select(a => a.MemberInfo).OfType<PropertyInfo>().ToArray();
                var namedPropertyValues = attribute.NamedArguments.Where(a => a.MemberInfo is PropertyInfo).Select(a => a.TypedValue.Value).ToArray();
                var namedFieldInfos = attribute.NamedArguments.Select(a => a.MemberInfo).OfType<FieldInfo>().ToArray();
                var namedFieldValues = attribute.NamedArguments.Where(a => a.MemberInfo is FieldInfo).Select(a => a.TypedValue.Value).ToArray();
                return new CustomAttributeBuilder(attribute.Constructor, attributeArgs, namedPropertyInfos, namedPropertyValues, namedFieldInfos, namedFieldValues);
            }).ToArray();
        }
    }
}
