// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;

namespace Microsoft.Azure.Functions.Analyzers
{
    internal class DiagnosticDescriptors
    {
        private static DiagnosticDescriptor Create(string id, string title, string messageFormat, string category, DiagnosticSeverity severity)
        {
            string helpLink = $"https://docs.microsoft.com/azure/azure-functions/errors-diagnostics/sdk-rules/{id}";
            return new DiagnosticDescriptor(id, title, messageFormat, category, severity, isEnabledByDefault: true, helpLinkUri: helpLink);
        }

        public static DiagnosticDescriptor AsyncVoidDiscouraged { get; }
            = Create(id: "AZF0001", title: "Avoid async void",
                messageFormat: "Async void can lead to unexpected behavior. Return Task instead.",
                category: Constants.DiagnosticsCategories.Usage,
                severity: DiagnosticSeverity.Error);

        public static DiagnosticDescriptor AvoidNonStaticHttpClient { get; }
            = Create(id: "AZF0002", title: "Inefficient HttpClient usage",
                messageFormat: "Reuse HttpClient instances to avoid holding more connections than necessary. See helplink for more information.",
                category: Constants.DiagnosticsCategories.Reliability,
                severity: DiagnosticSeverity.Warning);

        public static DiagnosticDescriptor IllegalFunctionName { get; }
            = Create(id: "AZF0003", title: "Illegal Function name",
                messageFormat: "Function name can't be '{0}'. It must start with a letter, be no longer than 128 characters, and contain only alphanumeric characters, underscores, or hyphens.",
                category: Constants.DiagnosticsCategories.WebJobsBindings,
                severity: DiagnosticSeverity.Error);

        public static DiagnosticDescriptor BadBindingExpressionSyntax { get; }
            = Create(id: "AZF0004", title: "Illegal binding expression syntax",
                messageFormat: "{0} can't be value '{1}': {2}",
                category: Constants.DiagnosticsCategories.WebJobsBindings,
                severity: DiagnosticSeverity.Warning);

        public static DiagnosticDescriptor FailedValidation { get; }
            = Create(id: "AZF0005", title: "Illegal binding type",
                messageFormat: "{0} can't be value '{1}': {2}",
                category: Constants.DiagnosticsCategories.WebJobsBindings,
                severity: DiagnosticSeverity.Warning);
    }
}
