// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.Node.Compilation
{
    public class ConditionalCompilationServiceTests
    {
        [Fact]
        public async Task GetCompilationResultAsync_WithPositivePredicate_ExecutesServiceCompilation()
        {
            var settingsManager = new ScriptSettingsManager();
            var innerCompilationServiceMock = new Mock<ICompilationService<IJavaScriptCompilation>>();
            innerCompilationServiceMock.Setup(m => m.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()))
                .ReturnsAsync(new TestJavaScriptCompilation("test", ImmutableArray<Diagnostic>.Empty));

            var compilationService = new ConditionalJavaScriptCompilationService(settingsManager, innerCompilationServiceMock.Object, () => true);
            var functionMetadata = new FunctionMetadata
            {
                Name = "testfunction",
                ScriptFile = "test.ts"
            };

            IJavaScriptCompilation compilation = await compilationService.GetFunctionCompilationAsync(functionMetadata);

            Assert.NotNull(compilation);

            string result = await compilation.EmitAsync(CancellationToken.None);
            Assert.Equal("test", result);

            innerCompilationServiceMock.Verify();
        }

        [Fact]
        public async Task GetCompilationResultAsync_WithNegativePredicate_WaitsForCachedCompilation()
        {
            var negativeTestData = CreateTestData(() => false);
            negativeTestData.CompilationServiceMock.Setup(m => m.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()))
                .ReturnsAsync(new TestJavaScriptCompilation("negativeresult", ImmutableArray<Diagnostic>.Empty));

            var functionMetadata = new FunctionMetadata
            {
                Name = "testfunction",
                ScriptFile = "test.ts"
            };

            var compilationTask = negativeTestData.Service.GetFunctionCompilationAsync(functionMetadata);

            // Wait 1 second
            await Task.Delay(1000);

            // Ensure our task is still running
            Assert.False(compilationTask.IsCompleted);

            // Create another compilation service that will perform the compilation
            var positiveTestData = CreateTestData(() => true);
            positiveTestData.CompilationServiceMock.Setup(m => m.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()))
                .ReturnsAsync(new TestJavaScriptCompilation("positiveresult", ImmutableArray<Diagnostic>.Empty));

            var compilation = await positiveTestData.Service.GetFunctionCompilationAsync(functionMetadata);

            // Wait for our process to pick up the results (default iteration interval is 500ms)
            await Task.Delay(700);

            Assert.True(compilationTask.IsCompleted);

            var cachedCompilation = compilationTask.Result;

            Assert.NotNull(compilation);
            Assert.NotNull(cachedCompilation);
            Assert.Equal("positiveresult", compilation.EmitAsync(CancellationToken.None).Result);
            Assert.Equal("positiveresult", cachedCompilation.EmitAsync(CancellationToken.None).Result);

            negativeTestData.CompilationServiceMock.Verify(c => c.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()), Times.Never());
            positiveTestData.CompilationServiceMock.Verify(c => c.GetFunctionCompilationAsync(It.IsAny<FunctionMetadata>()));
        }

        public(Mock<ICompilationService<IJavaScriptCompilation>> CompilationServiceMock, ConditionalJavaScriptCompilationService Service) CreateTestData(Func<bool> predicate)
        {
            var settingsManager = new ScriptSettingsManager();
            var innerCompilationServiceMock = new Mock<ICompilationService<IJavaScriptCompilation>>();
            var compilationService = new ConditionalJavaScriptCompilationService(settingsManager, innerCompilationServiceMock.Object, predicate);

            return (innerCompilationServiceMock, compilationService);
        }

        public class TestJavaScriptCompilation : IJavaScriptCompilation
        {
            private readonly string _emitResult;
            private readonly ImmutableArray<Diagnostic> _diagnostics;

            public TestJavaScriptCompilation(string emitResult, ImmutableArray<Diagnostic> diagnostics)
            {
                _emitResult = emitResult;
                _diagnostics = diagnostics;
            }

            public bool SupportsDiagnostics => true;

            public Task<string> EmitAsync(CancellationToken cancellationToken) => Task.FromResult(_emitResult);

            public ImmutableArray<Diagnostic> GetDiagnostics() => _diagnostics;

            async Task<object> ICompilation.EmitAsync(CancellationToken cancellationToken) => await EmitAsync(cancellationToken);
        }
    }
}
