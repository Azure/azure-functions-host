// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests;

public class CodelessFunctionTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly IHost _host;
    private readonly ScriptHost _scriptHost;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TestLoggerProvider _loggerProvider = new();
    private readonly CodelessFunctionDescriptorProvider _provider;

    public CodelessFunctionTests()
    {
        File.WriteAllText(Path.Combine(_directory.Path, "host.json"), "{\"version\":\"2.0\"}");
        _host = new HostBuilder()
            .ConfigureDefaultTestWebScriptHost(options =>
            {
                options.ScriptPath = _directory.Path;
                options.LogPath = _directory.Path;
            })
            .ConfigureServices(services =>
                services.PostConfigure<ScriptJobHostOptions>(options => options.FileWatchingEnabled = false))
            .Build();
        _scriptHost = _host.GetScriptHost();
        _loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(_loggerProvider));

        var binding = new Mock<ScriptBinding>(new ScriptBindingContext(CreateBinding("input", "manualTrigger").Raw));
        binding.SetupGet(b => b.DefaultType).Returns(typeof(string));
        binding.Setup(b => b.GetAttributes()).Returns(new Collection<Attribute> { new QueueAttribute("codeless-output") });
        ScriptBinding scriptBinding = binding.Object;
        var bindingProvider = new Mock<IScriptBindingProvider>();
        bindingProvider.Setup(p => p.TryCreate(It.IsAny<ScriptBindingContext>(), out scriptBinding)).Returns(true);

        _provider = new CodelessFunctionDescriptorProvider(_scriptHost, _scriptHost.ScriptOptions,
            new[] { bindingProvider.Object }, _loggerFactory);
    }

    [Theory]
    [InlineData(true, true, false, "node")]
    [InlineData(true, false, false, DotNetScriptTypes.DotNetAssembly)]
    [InlineData(false, true, false, DotNetScriptTypes.DotNetAssembly)]
    [InlineData(false, true, false, DotNetScriptTypes.CSharp)]
    [InlineData(true, true, true, DotNetScriptTypes.DotNetAssembly)]
    public async Task TryCreate_FunctionNotOwnedByProvider_ReturnsFalse(bool extensionOwned, bool codeless, bool proxy, string language)
    {
        FunctionMetadata metadata = proxy ? new ProxyFunctionMetadata(null) : new FunctionMetadata();
        metadata.Language = language;
        metadata.SetIsExtensionFunction(extensionOwned);
        metadata.SetIsCodeless(codeless);
        metadata.Properties["IsExtensionFunction"] = true;

        (bool success, FunctionDescriptor descriptor) = await _provider.TryCreate(metadata);

        Assert.False(success);
        Assert.Null(descriptor);
    }

    [Theory]
    [InlineData(DotNetScriptTypes.CSharp)]
    [InlineData(DotNetScriptTypes.RawDotNetAssembly)]
    public async Task TryCreate_UnsupportedExtensionLanguage_Throws(string language)
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Sync));
        metadata.Language = language;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.TryCreate(metadata));

        Assert.Contains("unsupported language", exception.Message);
        Assert.Contains(DotNetScriptTypes.DotNetAssembly, exception.Message);
    }

    [Fact]
    public async Task TryCreate_NullMetadata_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _provider.TryCreate(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryCreate_AssemblyReferenceOrPath_ExecutesRefOutAndReturn(bool assemblyReference)
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Sync), assemblyReference);
        metadata.Bindings.Add(CreateBinding("value", "queue", "inout"));
        metadata.Bindings.Add(CreateBinding("output", "queue", "out"));
        metadata.Bindings.Add(CreateBinding(ScriptConstants.SystemReturnParameterBindingName, "queue", "out"));

        (bool success, FunctionDescriptor descriptor) = await _provider.TryCreate(metadata);
        using var invoker = Assert.IsType<CodelessFunctionInvoker>(descriptor.Invoker);
        Assert.True(success);
        Assert.Collection(descriptor.Parameters,
            parameter =>
            {
                Assert.Equal("input", parameter.Name);
                Assert.True(parameter.IsTrigger);
                Assert.Equal(typeof(string), parameter.Type);
                Assert.Single(parameter.CustomAttributes);
            },
            parameter =>
            {
                Assert.Equal("value", parameter.Name);
                Assert.Equal(typeof(string).MakeByRefType(), parameter.Type);
                Assert.False(parameter.Attributes.HasFlag(ParameterAttributes.Out));
                Assert.Single(parameter.CustomAttributes);
            },
            parameter =>
            {
                Assert.Equal("output", parameter.Name);
                Assert.Equal(typeof(string).MakeByRefType(), parameter.Type);
                Assert.True(parameter.Attributes.HasFlag(ParameterAttributes.Out));
                Assert.Single(parameter.CustomAttributes);
            },
            parameter => Assert.Equal(typeof(ExecutionContext), parameter.Type),
            parameter =>
            {
                Assert.Equal(ScriptConstants.SystemReturnParameterName, parameter.Name);
                Assert.Equal(typeof(string).MakeByRefType(), parameter.Type);
                Assert.True(parameter.Attributes.HasFlag(ParameterAttributes.Out));
                Assert.Single(parameter.CustomAttributes);
            });

        MethodInfo generatedMethod = GenerateMethod(descriptor);
        Assert.Single(generatedMethod.GetCustomAttributes<NoAutomaticTriggerAttribute>());
        Assert.Single(generatedMethod.ReturnParameter.GetCustomAttributes<QueueAttribute>());
        var context = new ExecutionContext();
        object[] parameters = { "input", "original", null, context };

        string result = await (Task<string>)generatedMethod.Invoke(null, parameters);

        Assert.Equal("input:original:updated", result);
        Assert.Equal("original:updated", parameters[1]);
        Assert.Equal("input:output", parameters[2]);
        Assert.Same(context, parameters[3]);
        Assert.Equal(metadata.Name, context.FunctionName);
        Assert.Same(await invoker.GetFunctionTargetAsync(), await invoker.GetFunctionTargetAsync());
    }

    [Theory]
    [InlineData(nameof(Functions.Async), "input:async")]
    [InlineData(nameof(Functions.TaskOnly), null)]
    [InlineData(nameof(Functions.Void), null)]
    public async Task Invoke_TaskAndVoidSignatures_PreserveReturnSemantics(string methodName, string expected)
    {
        FunctionMetadata metadata = CreateMetadata(methodName);
        (_, FunctionDescriptor descriptor) = await _provider.TryCreate(metadata);
        using var invoker = Assert.IsType<CodelessFunctionInvoker>(descriptor.Invoker);
        MethodInfo generatedMethod = GenerateMethod(descriptor);

        var task = (Task)generatedMethod.Invoke(null, new object[] { "input", new ExecutionContext() });
        await task;

        if (expected is not null)
        {
            Assert.Equal(expected, await Assert.IsAssignableFrom<Task<string>>(task));
        }
        else
        {
            Assert.Equal(typeof(Task), generatedMethod.ReturnType);
            Assert.DoesNotContain(descriptor.Parameters, p => string.Equals(p.Name, ScriptConstants.SystemReturnParameterName, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task TryCreate_ExistingExecutionContext_DoesNotAppendAnother()
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.WithContext));
        (_, FunctionDescriptor descriptor) = await _provider.TryCreate(metadata);
        using var invoker = Assert.IsType<CodelessFunctionInvoker>(descriptor.Invoker);
        var context = new ExecutionContext();

        Assert.Single(descriptor.Parameters.Where(p => p.Type == typeof(ExecutionContext)));
        Assert.Equal(metadata.Name, await invoker.Invoke(new object[] { "input", context, null }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Run")]
    [InlineData("Example.")]
    public async Task TryCreate_InvalidEntryPoint_ThrowsWithoutFallback(string entryPoint)
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Void));
        metadata.EntryPoint = entryPoint;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.TryCreate(metadata));

        Assert.Contains("Invalid entry point configuration", exception.Message);
        Assert.Contains(metadata.Name, _scriptHost.FunctionErrors.Keys);
        Assert.Contains(_loggerProvider.GetAllLogMessages(), m =>
            m.Level == LogLevel.Error && string.Equals(m.FormattedMessage, "Codeless function loading error", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("assembly:")]
    public async Task TryCreate_InvalidAssembly_ThrowsClearError(string scriptFile)
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Void));
        metadata.ScriptFile = scriptFile;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.TryCreate(metadata));

        Assert.Contains("assembly", exception.Message);
    }

    [Theory]
    [InlineData("Missing.Type.Run", "The function type name 'Missing.Type' is invalid.")]
    [InlineData("MissingMethod", "The method 'MissingMethod' cannot be found.")]
    public async Task TryCreate_MissingTarget_Throws(string entryPoint, string expectedMessage)
    {
        FunctionMetadata metadata = CreateMetadata(entryPoint);
        if (entryPoint.Contains(".", StringComparison.Ordinal))
        {
            metadata.EntryPoint = entryPoint;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.TryCreate(metadata));

        Assert.Equal(expectedMessage, exception.Message);
    }

    [Fact]
    public async Task GetFunctionTargetAsync_PrefersStaticMethodOverInstanceOverload()
    {
        using var invoker = CreateInvoker(CreateMetadata(nameof(Functions.Overloaded)));

        MethodInfo method = await invoker.GetFunctionTargetAsync();

        Assert.True(method.IsStatic);
        Assert.Equal("input:static", await invoker.Invoke(new object[] { "input", new ExecutionContext() }));
    }

    [Fact]
    public async Task GetFunctionTargetAsync_InstanceFallback_DoesNotActivateInstance()
    {
        using var invoker = CreateInvoker(CreateMetadata(nameof(Functions.Instance)));

        Assert.False((await invoker.GetFunctionTargetAsync()).IsStatic);
        await Assert.ThrowsAsync<NullReferenceException>(() => invoker.Invoke(new object[] { "input", new ExecutionContext() }));
    }

    [Theory]
    [InlineData(nameof(Functions.Throw))]
    [InlineData(nameof(Functions.ThrowAsync))]
    public async Task Invoke_TargetThrows_PropagatesAndLogsOriginalException(string methodName)
    {
        using var invoker = CreateInvoker(CreateMetadata(methodName));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            invoker.Invoke(new object[] { "input", new ExecutionContext() }));

        Assert.Equal("Function failure", exception.Message);

        invoker.OnError(exception);

        var error = Assert.Single(_loggerProvider.GetAllLogMessages().Where(m => m.Level == LogLevel.Error));
        Assert.Equal($"{typeof(Functions).Assembly.GetName().Name}: Function failure.", error.FormattedMessage);
    }

    [Fact]
    public async Task TryCreate_MissingTriggerArgument_Throws()
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Void));
        metadata.Bindings.Clear();
        metadata.Bindings.Add(CreateBinding("Input", "manualTrigger"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.TryCreate(metadata));

        Assert.Equal("Missing a trigger argument named 'Input'.", exception.Message);
    }

    [Fact]
    public async Task TryCreate_MissingBindingArgument_LogsWarningButSkipsHttpAndReturnBindings()
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Async));
        metadata.Bindings.Add(CreateBinding("missing", "queue", "out"));
        metadata.Bindings.Add(CreateBinding("response", "http", "out"));
        metadata.Bindings.Add(CreateBinding(ScriptConstants.SystemReturnParameterBindingName, "queue", "out"));

        (_, FunctionDescriptor descriptor) = await _provider.TryCreate(metadata);
        using var invoker = Assert.IsType<CodelessFunctionInvoker>(descriptor.Invoker);

        var warning = Assert.Single(_loggerProvider.GetAllLogMessages().Where(m => m.Level == LogLevel.Warning));
        Assert.Contains("Missing binding argument named 'missing'.", warning.FormattedMessage);
        Assert.Equal("input:async", await invoker.Invoke(new object[] { "input", new ExecutionContext() }));
    }

    [Fact]
    public void TryCreateReturnValueParameterDescriptor_FSharpUnit_HasNoReturnBinding()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CodelessUnit"), AssemblyBuilderAccess.Run);
        Type unit = assembly.DefineDynamicModule("CodelessUnit")
            .DefineType("Microsoft.FSharp.Core.Unit", TypeAttributes.Public)
            .CreateTypeInfo().AsType();

        Assert.False(CodelessFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(unit, Array.Empty<FunctionBinding>(), out _));
        Assert.False(CodelessFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(typeof(Task<>).MakeGenericType(unit), Array.Empty<FunctionBinding>(), out _));
    }

    [Fact]
    public async Task TryCreate_UnsupportedTaskSubtype_ThrowsClearError()
    {
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.CustomTask));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => _provider.TryCreate(metadata));

        Assert.Equal("$return cannot be bound to return type UnsupportedTask.", exception.Message);
    }

    [Fact]
    public async Task AssemblyFileChange_PathBasedFunction_RequestsHostShutdown()
    {
        _scriptHost.ScriptOptions.FileWatchingEnabled = true;
        FunctionMetadata metadata = CreateMetadata(nameof(Functions.Void), assemblyReference: false);
        using var invoker = CreateInvoker(metadata);
        var lifetime = _host.Services.GetRequiredService<IScriptApplicationLifetime>();
        var change = new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(metadata.ScriptFile), Path.GetFileName(metadata.ScriptFile));

        _scriptHost.EventManager.Publish(new FileEvent(EventSources.ScriptFiles, change));

        await TestHelpers.Await(() => lifetime.ApplicationStopping.IsCancellationRequested);
        Assert.Contains(_loggerProvider.GetAllLogMessages(), m => string.Equals(m.FormattedMessage,
            "Assembly reference changes detected. Restarting host...", StringComparison.Ordinal));
    }

    private CodelessFunctionInvoker CreateInvoker(FunctionMetadata metadata)
    {
        return new CodelessFunctionInvoker(_scriptHost, metadata, new Collection<FunctionBinding>(),
            new Collection<FunctionBinding>(), _loggerFactory);
    }

    private static FunctionMetadata CreateMetadata(string methodName, bool assemblyReference = true)
    {
        var metadata = new FunctionMetadata
        {
            Name = $"Codeless{Guid.NewGuid():N}",
            Language = DotNetScriptTypes.DotNetAssembly,
            ScriptFile = assemblyReference ? $"assembly:{typeof(Functions).Assembly.FullName}" : typeof(Functions).Assembly.Location,
            FunctionDirectory = AppContext.BaseDirectory,
            EntryPoint = $"{typeof(Functions).FullName}.{methodName}"
        };
        metadata.SetIsExtensionFunction(true);
        metadata.SetIsCodeless(true);
        metadata.Bindings.Add(CreateBinding("input", "manualTrigger"));

        return metadata;
    }

    private static BindingMetadata CreateBinding(string name, string type, string direction = "in")
    {
        return BindingMetadata.Create(new JObject
        {
            ["name"] = name,
            ["type"] = type,
            ["direction"] = direction
        });
    }

    private static MethodInfo GenerateMethod(FunctionDescriptor descriptor)
    {
        Type generatedType = FunctionGenerator.Generate("CodelessFunctions", "Functions", null, new[] { descriptor });

        return generatedType.GetMethod(descriptor.Name);
    }

    public void Dispose()
    {
        _scriptHost.Dispose();
        _host.Dispose();
        _loggerFactory.Dispose();
        _directory.Dispose();
    }

    public class Functions
    {
        private readonly string _suffix = ":instance";

        public static string Sync(string input, ref string value, out string output)
        {
            value += ":updated";
            output = $"{input}:output";

            return $"{input}:{value}";
        }

        public static async Task<string> Async(string input)
        {
            await Task.Yield();

            return $"{input}:async";
        }

        public static async Task TaskOnly(string input)
        {
            await Task.Yield();
        }

        public static void Void(string input)
        {
        }

        public static string WithContext(string input, ExecutionContext context) => context.FunctionName;

        public static string Overloaded(string input) => $"{input}:static";

        public string Overloaded(string input, int unused) => $"{input}{_suffix}";

        public string Instance(string input) => $"{input}{_suffix}";

        public static string Throw(string input) => throw new InvalidOperationException("Function failure");

        public static async Task<string> ThrowAsync(string input)
        {
            await Task.Yield();
            throw new InvalidOperationException("Function failure");
        }

        public static UnsupportedTask CustomTask(string input) => new();
    }

    public class UnsupportedTask : Task
    {
        public UnsupportedTask()
            : base(() => { })
        {
        }
    }
}
