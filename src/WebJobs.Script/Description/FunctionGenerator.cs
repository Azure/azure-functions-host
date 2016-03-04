// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    // TODO: Make this type internal - it has to be public currently due to the
    // public GetInvoker helper which we need to remove (IL genned code calls into it)
    public static class FunctionGenerator
    {
        private static Dictionary<string, IFunctionInvoker> _invokerMap = new Dictionary<string, IFunctionInvoker>();

        // TODO: make this private
        public static IFunctionInvoker GetInvoker(string method)
        {
            return _invokerMap[method];
        }

        public static Type Generate(string functionAssemblyName, string typeName, Collection<FunctionDescriptor> functions)
        {
            if (functions == null)
            {
                throw new ArgumentNullException("functions");
            }

            AssemblyName assemblyName = new AssemblyName(functionAssemblyName);
            AssemblyBuilder assemblyBuilder =
                AppDomain.CurrentDomain.DefineDynamicAssembly(
                    assemblyName,
                    AssemblyBuilderAccess.RunAndSave);

            ModuleBuilder mb = assemblyBuilder.DefineDynamicModule(assemblyName.Name, assemblyName.Name + ".dll");

            TypeBuilder tb = mb.DefineType(typeName, TypeAttributes.Public);

            foreach (FunctionDescriptor function in functions)
            {
                MethodBuilder methodBuilder = tb.DefineMethod(function.Name, MethodAttributes.Public | MethodAttributes.Static);
                Type[] types = function.Parameters.Select(p => p.Type).ToArray();
                methodBuilder.SetParameters(types);
                methodBuilder.SetReturnType(typeof(Task));

                if (function.CustomAttributes != null)
                {
                    foreach (CustomAttributeBuilder attributeBuilder in function.CustomAttributes)
                    {
                        methodBuilder.SetCustomAttribute(attributeBuilder);
                    }
                }

                for (int i = 0; i < function.Parameters.Count; i++)
                {
                    ParameterDescriptor parameter = function.Parameters[i];
                    ParameterBuilder parameterBuilder = methodBuilder.DefineParameter(i + 1, parameter.Attributes, parameter.Name);
                    if (parameter.CustomAttributes != null)
                    {
                        foreach (CustomAttributeBuilder attributeBuilder in parameter.CustomAttributes)
                        {
                            parameterBuilder.SetCustomAttribute(attributeBuilder);
                        }
                    }
                }

                _invokerMap[function.Name] = function.Invoker;

                MethodInfo invokeMethod = function.Invoker.GetType().GetMethod("Invoke");
                MethodInfo getInvoker = typeof(FunctionGenerator).GetMethod("GetInvoker", BindingFlags.Static | BindingFlags.Public);

                ILGenerator il = methodBuilder.GetILGenerator();

                LocalBuilder argsLocal = il.DeclareLocal(typeof(object[]));
                LocalBuilder invokerLocal = il.DeclareLocal(typeof(IFunctionInvoker));

                il.Emit(OpCodes.Nop);

                // declare an array for all parameter values
                il.Emit(OpCodes.Ldc_I4, function.Parameters.Count);
                il.Emit(OpCodes.Newarr, typeof(object));
                il.Emit(OpCodes.Stloc_0);

                // copy each parameter into the arg array
                for (int i = 0; i < function.Parameters.Count; i++)
                {
                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldarg, i);

                    // For Out and Ref types, need to do an indirection. 
                    if (function.Parameters[i].Type.IsByRef)
                    {
                        il.Emit(OpCodes.Ldind_Ref);
                    }

                    il.Emit(OpCodes.Stelem_Ref);
                }

                // get the invoker instance
                il.Emit(OpCodes.Ldstr, function.Name);
                il.Emit(OpCodes.Call, getInvoker);
                il.Emit(OpCodes.Stloc_1);

                // now call the invoker, passing in the args
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Callvirt, invokeMethod);

                // Copy back out and ref parameters
                for (int i = 0; i < function.Parameters.Count; i++)
                {
                    var param = function.Parameters[i];
                    if (!param.Type.IsByRef)
                    {
                        continue;
                    }

                    il.Emit(OpCodes.Ldarg, i);

                    il.Emit(OpCodes.Ldloc_0);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldelem_Ref);
                    il.Emit(OpCodes.Castclass, param.Type.GetElementType());

                    il.Emit(OpCodes.Stind_Ref);
                }

                il.Emit(OpCodes.Ret);
            }

            Type t = tb.CreateType();
            
            return t;
        }
    }
}
