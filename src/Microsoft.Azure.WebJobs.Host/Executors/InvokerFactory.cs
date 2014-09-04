// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal static class InvokerFactory
    {
        public static IInvoker Create(MethodInfo method)
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }

            if (!method.IsStatic)
            {
                throw new NotSupportedException("Only static methods can be invoked.");
            }

            Type returnType = method.ReturnType;

            if (returnType != typeof(void) && returnType != typeof(Task))
            {
                throw new NotSupportedException("Methods may only return void or Task.");
            }

            List<string> parameterNames = method.GetParameters().Select(p => p.Name).ToList();

            // Parameter to invoker: object[] arguments
            ParameterExpression argumentsParameter = Expression.Parameter(typeof(object[]), "arguments");

            // Local variables passed as arguments to Call
            List<ParameterExpression> localVariables = new List<ParameterExpression>();

            // Pre-Call, copy from arguments array to local variables.
            List<Expression> arrayToLocalsAssignments = new List<Expression>();

            // Post-Call, copy from local variables back to arguments array.
            List<Expression> localsToArrayAssignments = new List<Expression>();

            ParameterInfo[] parameterInfos = method.GetParameters();
            Debug.Assert(parameterInfos != null);

            for (int index = 0; index < parameterInfos.Length; index++)
            {
                ParameterInfo parameterInfo = parameterInfos[index];
                Type argumentType = parameterInfo.ParameterType;

                if (argumentType.IsByRef)
                {
                    // The type of the local variable (and object in the arguments array) should be T rather than T&.
                    argumentType = argumentType.GetElementType();
                }

                // T argumentN
                ParameterExpression localVariable = Expression.Parameter(argumentType);
                localVariables.Add(localVariable);

                // arguments[index]
                Expression arrayAccess = Expression.ArrayAccess(argumentsParameter, Expression.Constant(index));

                // Pre-Call:
                // T argumentN = (T)arguments[index];
                Expression arrayAccessAsT = Expression.Convert(arrayAccess, argumentType);
                Expression assignArrayToLocal = Expression.Assign(localVariable, arrayAccessAsT);
                arrayToLocalsAssignments.Add(assignArrayToLocal);

                // Post-Call:
                // arguments[index] = (object)argumentN;
                Expression localAsObject = Expression.Convert(localVariable, typeof(object));
                Expression assignLocalToArray = Expression.Assign(arrayAccess, localAsObject);
                localsToArrayAssignments.Add(assignLocalToArray);
            }

            // Static call:
            // method(param0, param1, ...);
            Expression call = Expression.Call(null, method, localVariables);

            List<Expression> blockExpressions = new List<Expression>();
            // T0 argument0 = (T0)arguments[0];
            // T1 argument1 = (T1)arguments[1];
            // ...
            blockExpressions.AddRange(arrayToLocalsAssignments);
            // Call(argument0, argument1, ...);
            blockExpressions.Add(call);
            // arguments[0] = (object)argument0;
            // arguments[1] = (object)argument1;
            // ...
            blockExpressions.AddRange(localsToArrayAssignments);

            Expression block = Expression.Block(localVariables, blockExpressions);

            if (call.Type == typeof(void))
            {
                // for: public void JobMethod()
                Expression<Action<object[]>> lambda = Expression.Lambda<Action<object[]>>(block, argumentsParameter);
                Action<object[]> compiled = lambda.Compile();
                return new VoidInvoker(parameterNames, compiled);
            }
            else
            {
                // for: public Task JobMethod()
                Debug.Assert(call.Type == typeof(Task));
                Expression<Func<object[], Task>> lambda = Expression.Lambda<Func<object[], Task>>(block,
                    argumentsParameter);
                Func<object[], Task> compiled = lambda.Compile();
                return new TaskInvoker(parameterNames, compiled);
            }
        }
    }
}
