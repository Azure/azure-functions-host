// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal static class FunctionInvokerFactory
    {
        public static IFunctionInvoker Create(MethodInfo method)
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            Type reflectedType = method.ReflectedType;
            MethodInfo genericMethodDefinition = typeof(FunctionInvokerFactory).GetMethod("CreateGeneric",
                BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Assert(genericMethodDefinition != null);
            MethodInfo genericMethod = genericMethodDefinition.MakeGenericMethod(reflectedType);
            Debug.Assert(genericMethod != null);
            Func<MethodInfo, IFunctionInvoker> lambda = (Func<MethodInfo, IFunctionInvoker>)Delegate.CreateDelegate(
                typeof(Func<MethodInfo, IFunctionInvoker>), genericMethod);
            return lambda.Invoke(method);
        }

        private static IFunctionInvoker CreateGeneric<TReflected>(MethodInfo method)
        {
            Debug.Assert(method != null);

            List<string> parameterNames = method.GetParameters().Select(p => p.Name).ToList();

            IMethodInvoker<TReflected> methodInvoker = MethodInvokerFactory.Create<TReflected>(method);

            IFactory<TReflected> instanceFactory = CreateInstanceFactory<TReflected>(method);

            return new FunctionInvoker<TReflected>(parameterNames, instanceFactory, methodInvoker);
        }

        private static IFactory<TReflected> CreateInstanceFactory<TReflected>(MethodInfo method)
        {
            Debug.Assert(method != null);

            if (method.IsStatic)
            {
                return NullInstanceFactory<TReflected>.Instance;
            }
            else
            {
                return new ActivatorInstanceFactory<TReflected>();
            }
        }
    }
}
