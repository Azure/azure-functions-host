// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description;

internal sealed class CodelessFunctionDescriptorProvider : FunctionDescriptorProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public CodelessFunctionDescriptorProvider(ScriptHost host, ScriptJobHostOptions config,
        ICollection<IScriptBindingProvider> bindingProviders, ILoggerFactory loggerFactory)
        : base(host, config, bindingProviders)
    {
        _loggerFactory = loggerFactory;
    }

    public override Task<(bool Success, FunctionDescriptor Descriptor)> TryCreate(FunctionMetadata functionMetadata)
    {
        ArgumentNullException.ThrowIfNull(functionMetadata);

        if (!functionMetadata.IsExtensionFunction() || !functionMetadata.IsCodeless() || functionMetadata.IsProxy())
        {
            return Task.FromResult<(bool, FunctionDescriptor)>((false, null));
        }

        if (!string.Equals(functionMetadata.Language, DotNetScriptTypes.DotNetAssembly, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(functionMetadata.Language, DotNetScriptTypes.CSharp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(functionMetadata.Language, DotNetScriptTypes.RawDotNetAssembly, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Extension function '{functionMetadata.Name}' uses unsupported language '{functionMetadata.Language}'. Codeless .NET functions must use '{DotNetScriptTypes.DotNetAssembly}' with a precompiled assembly; C# script compilation is not supported.");
            }

            return Task.FromResult<(bool, FunctionDescriptor)>((false, null));
        }

        return base.TryCreate(functionMetadata);
    }

    protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata,
        FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
    {
        return new CodelessFunctionInvoker(Host, functionMetadata, inputBindings, outputBindings, _loggerFactory);
    }

    protected override async Task<Collection<ParameterDescriptor>> GetFunctionParametersAsync(IFunctionInvoker functionInvoker,
        FunctionMetadata functionMetadata, BindingMetadata triggerMetadata, Collection<CustomAttributeBuilder> methodAttributes,
        Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
    {
        ArgumentNullException.ThrowIfNull(functionInvoker);
        ArgumentNullException.ThrowIfNull(functionMetadata);
        ArgumentNullException.ThrowIfNull(triggerMetadata);
        ArgumentNullException.ThrowIfNull(methodAttributes);

        if (functionInvoker is not CodelessFunctionInvoker invoker)
        {
            throw new InvalidOperationException($"Expected invoker of type '{nameof(CodelessFunctionInvoker)}' but received '{functionInvoker.GetType().Name}'");
        }

        ApplyMethodLevelAttributes(functionMetadata, triggerMetadata, methodAttributes);

        MethodInfo functionTarget = await invoker.GetFunctionTargetAsync();
        Collection<ParameterDescriptor> descriptors = new();
        FunctionBinding[] bindings = inputBindings.Union(outputBindings).ToArray();

        foreach (ParameterInfo parameter in functionTarget.GetParameters())
        {
            ParameterDescriptor descriptor;
            if (string.Equals(parameter.Name, triggerMetadata.Name, StringComparison.Ordinal))
            {
                descriptor = CreateTriggerParameter(triggerMetadata, parameter.ParameterType);
            }
            else
            {
                descriptor = new ParameterDescriptor(parameter.Name, parameter.ParameterType);
                FunctionBinding binding = bindings.FirstOrDefault(b => string.Equals(b.Metadata.Name, parameter.Name, StringComparison.Ordinal));
                AddBindingAttributes(descriptor, binding);
            }

            if (parameter.IsOut)
            {
                descriptor.Attributes |= ParameterAttributes.Out;
            }

            descriptors.Add(descriptor);
        }

        if (!descriptors.Any(p => p.Type == typeof(ExecutionContext)))
        {
            descriptors.Add(new ParameterDescriptor(ScriptConstants.SystemExecutionContextParameterName, typeof(ExecutionContext)));
        }

        if (TryCreateReturnValueParameterDescriptor(functionTarget.ReturnType, bindings, out ParameterDescriptor returnDescriptor))
        {
            descriptors.Add(returnDescriptor);
        }

        return descriptors;
    }

    internal static bool TryCreateReturnValueParameterDescriptor(Type functionReturnType,
        IEnumerable<FunctionBinding> bindings, out ParameterDescriptor descriptor)
    {
        descriptor = null;
        if (functionReturnType == typeof(void) || functionReturnType == typeof(Task))
        {
            return false;
        }

        if (typeof(Task).IsAssignableFrom(functionReturnType))
        {
            if (!functionReturnType.IsGenericType || functionReturnType.GetGenericTypeDefinition() != typeof(Task<>))
            {
                throw new InvalidOperationException($"{ScriptConstants.SystemReturnParameterBindingName} cannot be bound to return type {functionReturnType.Name}.");
            }

            functionReturnType = functionReturnType.GetGenericArguments()[0];
        }

        if (string.Equals(functionReturnType.FullName, "Microsoft.FSharp.Core.Unit", StringComparison.Ordinal))
        {
            return false;
        }

        descriptor = new ParameterDescriptor(ScriptConstants.SystemReturnParameterName, functionReturnType.MakeByRefType());
        descriptor.Attributes |= ParameterAttributes.Out;
        AddBindingAttributes(descriptor, bindings.SingleOrDefault(b => b.Metadata.IsReturn));

        return true;
    }

    private static void AddBindingAttributes(ParameterDescriptor descriptor, FunctionBinding binding)
    {
        Collection<CustomAttributeBuilder> attributes = binding?.GetCustomAttributes(descriptor.Type);
        if (attributes is not null)
        {
            foreach (CustomAttributeBuilder attribute in attributes)
            {
                descriptor.CustomAttributes.Add(attribute);
            }
        }
    }
}
