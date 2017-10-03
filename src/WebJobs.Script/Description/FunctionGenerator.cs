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
                if (function.Metadata.IsDirect)
                {
                    continue;
                }

                var retValue = function.Parameters.Where(x => x.Name == ScriptConstants.SystemReturnParameterName).FirstOrDefault();
                var parameters = function.Parameters.Where(x => x != retValue).ToArray();

                MethodBuilder methodBuilder = tb.DefineMethod(function.Name, MethodAttributes.Public | MethodAttributes.Static);
                Type[] types = parameters.Select(p => p.Type).ToArray();
                methodBuilder.SetParameters(types);

                Type innerReturnType = null;

                if (retValue == null)
                {
                    methodBuilder.SetReturnType(typeof(Task));
                }
                else
                {
                    // It's critical to set the proper return type for the binder.
                    // The return parameters was added a MakeByRefType, so need to get the inner type.
                    innerReturnType = retValue.Type.GetElementType();

                    // Task<> derives from Task.
                    var actualReturnType = typeof(Task<>).MakeGenericType(innerReturnType);

                    methodBuilder.SetReturnType(actualReturnType);

                    ParameterBuilder parameterBuilder = methodBuilder.DefineParameter(0, ParameterAttributes.Retval, null);

                    if (retValue.CustomAttributes != null)
                    {
                        foreach (CustomAttributeBuilder attributeBuilder in retValue.CustomAttributes)
                        {
                            parameterBuilder.SetCustomAttribute(attributeBuilder);
                        }
                    }
                }

                if (function.CustomAttributes != null)
                {
                    foreach (CustomAttributeBuilder attributeBuilder in function.CustomAttributes)
                    {
                        methodBuilder.SetCustomAttribute(attributeBuilder);
                    }
                }

                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterDescriptor parameter = parameters[i];
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
                il.Emit(OpCodes.Ldc_I4, parameters.Length);
                il.Emit(OpCodes.Newarr, typeof(object));
                il.Emit(OpCodes.Stloc, argsLocal);

                // copy each parameter into the arg array
                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterDescriptor parameter = parameters[i];

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
                il.Emit(OpCodes.Callvirt, invokeMethod); // pushes a Task<object>

                if (parameters.Any(p => p.Type.IsByRef))
                {
                    LocalBuilder taskLocal = il.DeclareLocal(typeof(Task<object>));
                    LocalBuilder taskAwaiterLocal = il.DeclareLocal(typeof(TaskAwaiter<object>));

                    // We need to wait on the function's task if we have any out/ref
                    // parameters to ensure they have been populated before we copy them back

                    // Store the result into a local Task
                    // and load it onto the evaluation stack
                    il.Emit(OpCodes.Stloc, taskLocal);
                    il.Emit(OpCodes.Ldloc, taskLocal);

                    // Call "GetAwaiter" on the Task
                    il.Emit(OpCodes.Callvirt, typeof(Task<object>).GetMethod("GetAwaiter", Type.EmptyTypes));

                    // Call "GetResult", which will synchonously wait for the Task to complete
                    il.Emit(OpCodes.Stloc, taskAwaiterLocal);
                    il.Emit(OpCodes.Ldloca, taskAwaiterLocal);
                    il.Emit(OpCodes.Call, typeof(TaskAwaiter<object>).GetMethod("GetResult"));
                    il.Emit(OpCodes.Pop); // ignore GetResult();

                    // Copy back out and ref parameters
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];
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

                // Need to coerce.
                if (innerReturnType != null)
                {
                    var m = typeof(FunctionGenerator).GetMethod("Coerce", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var m2 = m.MakeGenericMethod(innerReturnType);
                    il.Emit(OpCodes.Call, m2);
                }

                il.Emit(OpCodes.Ret);
            }

            Type t = tb.CreateType();

            return t;
        }

        public static async Task<T> Coerce<T>(Task<object> src)
        {
            var val = await src;
            return (T)val;
        }
    }
}
