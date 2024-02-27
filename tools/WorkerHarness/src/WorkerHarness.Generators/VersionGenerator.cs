using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Text;

[Generator]
public sealed class VersionGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var version = context.Compilation.Assembly.Identity.Version.ToString();
        string currentDate = DateTime.Now.ToString("yyyyMMddHHmmss");

        var source = $@"
namespace WorkerHarness
{{
    internal static class Constants
    {{
        internal const string WorkerHarnessVersion = ""{version}, Build time:{currentDate}"";
    }}
}}";

        context.AddSource("Constants.g.cs", SourceText.From(source, Encoding.UTF8));
    }
}
