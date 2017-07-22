// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static Type Generate(string functionAssemblyName, string typeName, Collection<CustomAttributeBuilder> typeAttributes, Collection<FunctionDescriptor> functions)
        {
            if (functions == null)
            {
                throw new ArgumentNullException("functions");
            }

            AssemblyName assemblyName = new AssemblyName(functionAssemblyName);
            AssemblyBuilder assemblyBuilder =
                AppDomain.CurrentDomain.DefineDynamicAssembly(
                    assemblyName,
                    AssemblyBuilderAccess.Run);

            ModuleBuilder mb = assemblyBuilder.DefineDynamicModule(assemblyName.Name);

            TypeBuilder tb = mb.DefineType(typeName, TypeAttributes.Public);

            if (typeAttributes != null)
            {
                foreach (CustomAttributeBuilder attributeBuilder in typeAttributes)
                {
                    tb.SetCustomAttribute(attributeBuilder);
                }
            }

            foreach (FunctionDescriptor function in functions)
            {
                MethodBuilder methodBuilder = tb.DefineMethod(function.Name, MethodAttributes.Public | MethodAttributes.Static);
                Type[] types = function.Parameters.Select(p => p.Type).ToArray();
                methodBuilder.SetParameters(types);

                if (function.Parameters.Any(p => p.Name == ScriptConstants.SystemReturnParameterBindingName))
                {
                    // TODO: In order for return value trigger binding (new feature) to work correctly, we need to set
                    //       the return type to Task<TReturnValue> instead of Task<object>. Using Task<object> will only
                    //       work for triggers which support binding to System.Object.
                    methodBuilder.SetReturnType(typeof(Task<object>));
                }
                else
                {
                    methodBuilder.SetReturnType(typeof(Task));
                }

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
                il.Emit(OpCodes.Stloc, argsLocal);

                // copy each parameter into the arg array
                for (int i = 0; i < function.Parameters.Count; i++)
                {
                    ParameterDescriptor parameter = function.Parameters[i];

                    il.Emit(OpCodes.Ldloc, argsLocal);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldarg, i);

                    // For Out and Ref types, need to do an indirection.
                    if (parameter.Type.IsByRef)
                    {
                        il.Emit(OpCodes.Ldind_Ref);
                    }

                    // Box value types
                    if (parameter.Type.IsValueType)
                    {
                        il.Emit(OpCodes.Box, parameter.Type);
                    }

                    il.Emit(OpCodes.Stelem_Ref);
                }

                // get the invoker instance
                il.Emit(OpCodes.Ldstr, function.Name);
                il.Emit(OpCodes.Call, getInvoker);
                il.Emit(OpCodes.Stloc, invokerLocal);

                // now call the invoker, passing in the args
                il.Emit(OpCodes.Ldloc, invokerLocal);
                il.Emit(OpCodes.Ldloc, argsLocal);
                il.Emit(OpCodes.Callvirt, invokeMethod);

                if (function.Parameters.Any(p => p.Type.IsByRef))
                {
                    LocalBuilder taskLocal = il.DeclareLocal(typeof(Task));
                    LocalBuilder taskAwaiterLocal = il.DeclareLocal(typeof(TaskAwaiter));

                    // We need to wait on the function's task if we have any out/ref
                    // parameters to ensure they have been populated before we copy them back

                    // Store the result into a local Task
                    // and load it onto the evaluation stack
                    il.Emit(OpCodes.Stloc, taskLocal);
                    il.Emit(OpCodes.Ldloc, taskLocal);

                    // Call "GetAwaiter" on the Task
                    il.Emit(OpCodes.Callvirt, typeof(Task).GetMethod("GetAwaiter", Type.EmptyTypes));

                    // Call "GetResult", which will synchonously wait for the Task to complete
                    il.Emit(OpCodes.Stloc, taskAwaiterLocal);
                    il.Emit(OpCodes.Ldloca, taskAwaiterLocal);
                    il.Emit(OpCodes.Call, typeof(TaskAwaiter).GetMethod("GetResult"));

                    // Copy back out and ref parameters
                    for (int i = 0; i < function.Parameters.Count; i++)
                    {
                        var param = function.Parameters[i];
                        if (!param.Type.IsByRef)
                        {
                            continue;
                        }

                        il.Emit(OpCodes.Ldarg, i);

                        il.Emit(OpCodes.Ldloc, argsLocal);
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Castclass, param.Type.GetElementType());

                        il.Emit(OpCodes.Stind_Ref);
                    }

                    il.Emit(OpCodes.Ldloc, taskLocal);
                }

                il.Emit(OpCodes.Ret);
            }

            Type t = tb.CreateType();

            return t;
        }
    }
}
