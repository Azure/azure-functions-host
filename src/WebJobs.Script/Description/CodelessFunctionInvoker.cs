// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description;

internal sealed class CodelessFunctionInvoker : FunctionInvokerBase
{
    private static readonly Regex EntryPointPattern = new(@"^(?<typename>.+)\.(?<methodname>\S+)$");
    private readonly Collection<FunctionBinding> _inputBindings;
    private readonly Collection<FunctionBinding> _outputBindings;
    private readonly Lazy<MethodInfo> _functionTarget;
    private readonly Action _onReferencesChanged;
    private MethodInvoker _methodInvoker;

    internal CodelessFunctionInvoker(ScriptHost host, FunctionMetadata functionMetadata,
        Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, ILoggerFactory loggerFactory)
        : base(host, functionMetadata, loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(functionMetadata.ScriptFile))
        {
            throw new InvalidOperationException($"Codeless function '{functionMetadata.Name}' must specify a precompiled assembly in scriptFile.");
        }

        _inputBindings = inputBindings;
        _outputBindings = outputBindings;
        _functionTarget = new Lazy<MethodInfo>(CreateFunctionTarget);
        _onReferencesChanged = new Action(OnReferencesChanged).Debounce();

        if (!DependencyHelper.IsAssemblyReferenceFormat(functionMetadata.ScriptFile))
        {
            InitializeFileWatcherIfEnabled();
        }
    }

    internal Task<MethodInfo> GetFunctionTargetAsync(bool isInvocation = false)
    {
        try
        {
            return Task.FromResult(_functionTarget.Value);
        }
        catch (Exception exception)
        {
            var properties = isInvocation ? null : PrimaryHostLogProperties;
            FunctionLogger.Log(LogLevel.Error, 0, properties, exception, (state, ex) => "Codeless function loading error");
            Utility.AddFunctionError(Host.FunctionErrors, Metadata.Name, exception.ToString());

            return Task.FromException<MethodInfo>(exception);
        }
    }

    protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
    {
        MethodInfo function = await GetFunctionTargetAsync(isInvocation: true);

        // The generated wrapper appends system and return-binding parameters.
        object[] functionParameters = parameters.Take(function.GetParameters().Length).ToArray();
        object result = await _methodInvoker.InvokeAsync(null, functionParameters);

        Array.Copy(functionParameters, parameters, functionParameters.Length);

        return result;
    }

    protected override void OnScriptFileChanged(FileSystemEventArgs e)
    {
        if (ScriptConstants.AssemblyFileTypes.Contains(Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase))
        {
            _onReferencesChanged();
        }
    }

    private void OnReferencesChanged()
    {
        FunctionLogger.LogInformation("Assembly reference changes detected. Restarting host...");
        Host.Shutdown();
    }

    private MethodInfo CreateFunctionTarget()
    {
        Match entryPoint = EntryPointPattern.Match(Metadata.EntryPoint ?? string.Empty);
        if (!entryPoint.Success)
        {
            throw new InvalidOperationException("Invalid entry point configuration. The function entry point must be defined in the format <fulltypename>.<methodname>");
        }

        Assembly assembly;
        if (DependencyHelper.TryGetAssemblyReference(Metadata.ScriptFile, out string assemblyName))
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                throw new InvalidOperationException($"Codeless function '{Metadata.Name}' must specify an assembly name after 'assembly:'.");
            }

            assembly = FunctionAssemblyLoadContext.Shared.LoadFromAssemblyName(new AssemblyName(assemblyName));
        }
        else
        {
            string assemblyPath = Path.GetFullPath(Path.Combine(Host.ScriptOptions.RootScriptPath, Metadata.ScriptFile));
            assembly = FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(assemblyPath, true);
        }

        string typeName = entryPoint.Groups["typename"].Value;
        string methodName = entryPoint.Groups["methodname"].Value;
        Type functionType = assembly.GetType(typeName);
        if (functionType is null)
        {
            throw new InvalidOperationException($"The function type name '{typeName}' is invalid.");
        }

        // Preserve the legacy static-first lookup; instance methods are not activated.
        MethodInfo method = functionType.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public)
            ?? functionType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public);
        if (method is null)
        {
            throw new InvalidOperationException($"The method '{methodName}' cannot be found.");
        }

        ValidateFunctionBindingArguments(method);
        _methodInvoker = MethodInvoker.Create(method);

        return method;
    }

    private void ValidateFunctionBindingArguments(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string triggerInputName = Metadata.Bindings.FirstOrDefault(b => b.IsTrigger)?.Name;
        if (triggerInputName is null || !parameters.Any(p => string.Equals(p.Name, triggerInputName, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Missing a trigger argument named '{triggerInputName}'.");
        }

        foreach (FunctionBinding binding in _inputBindings.Where(b => !b.Metadata.IsTrigger).Union(_outputBindings))
        {
            if (binding.Metadata.IsReturn || string.Equals(binding.Metadata.Type, "http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!parameters.Any(p => string.Equals(p.Name, binding.Metadata.Name, StringComparison.Ordinal)))
            {
                LogOnPrimaryHost($"Missing binding argument named '{binding.Metadata.Name}'. Mismatched binding argument names may lead to function indexing errors.", LogLevel.Warning);
            }
        }
    }

    private abstract class MethodInvoker
    {
        public static MethodInvoker Create(MethodInfo method)
        {
            Type returnType = method.ReturnType;
            if (returnType == typeof(void) || returnType == typeof(Task))
            {
                returnType = typeof(object);
            }
            else if (typeof(Task).IsAssignableFrom(returnType))
            {
                if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
                {
                    throw new InvalidOperationException($"{ScriptConstants.SystemReturnParameterBindingName} cannot be bound to return type {returnType.Name}.");
                }

                returnType = returnType.GetGenericArguments()[0];
            }

            Type invokerType = typeof(MethodInvoker<,>).MakeGenericType(method.DeclaringType, returnType);

            return (MethodInvoker)Activator.CreateInstance(invokerType, method);
        }

        public abstract Task<object> InvokeAsync(object target, object[] parameters);
    }

    private sealed class MethodInvoker<TReflected, TResult> : MethodInvoker
    {
        private readonly IMethodInvoker<TReflected, TResult> _invoker;

        public MethodInvoker(MethodInfo method)
        {
            _invoker = MethodInvokerFactory.Create<TReflected, TResult>(method);
        }

        public override async Task<object> InvokeAsync(object target, object[] parameters)
        {
            return await _invoker.InvokeAsync((TReflected)target, parameters);
        }
    }
}
