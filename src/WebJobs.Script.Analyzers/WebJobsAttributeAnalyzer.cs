// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Reflection;

namespace Microsoft.Azure.Functions.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class WebJobsAttributeAnalyzer : DiagnosticAnalyzer
    {
        JobHostMetadataProvider _tooling;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return ImmutableArray.Create(
                    DiagnosticDescriptors.IllegalFunctionName,
                    DiagnosticDescriptors.BadBindingExpressionSyntax,
                    DiagnosticDescriptors.FailedValidation);
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            // https://stackoverflow.com/questions/62638455/analyzer-with-code-fix-project-template-is-broken
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Analyze method signatures.
            context.RegisterSyntaxNodeAction(AnalyzeMethodDeclarationNode, SyntaxKind.MethodDeclaration);

            // Hook compilation to get the assemblies' references and build the WebJob tooling interfaces.
            context.RegisterCompilationStartAction(AnalyzeCompilation);
        }

        private void AnalyzeCompilation(CompilationStartAnalysisContext context)
        {
            var compilation = context.Compilation;

            AssemblyCache.Instance.Build(compilation);
            _tooling = AssemblyCache.Instance.Tooling;
        }

        // This is called extremely frequently
        // Analyze the method signature to validate binding attributes + types on the parameters
        private void AnalyzeMethodDeclarationNode(SyntaxNodeAnalysisContext context)
        {
            if (_tooling == null) // Not yet initialized
            {
                return;
            }

            var methodDecl = (MethodDeclarationSyntax)context.Node;

            CheckForFunctionNameAttributeAndReport(context, methodDecl);

            // Go through
            var parameterList = methodDecl.ParameterList;

            foreach (ParameterSyntax parameterSyntax in parameterList.Parameters)
            {
                // No symbol for the parameter; just the parameter's type
                // Lazily do this - only do this if we're actually looking at a webjobs parameter.
                Type parameterType = null;

                // Now validate each parameter in the method.
                foreach (var attrListSyntax in parameterSyntax.AttributeLists)
                {
                    foreach (AttributeSyntax attributeSyntax in attrListSyntax.Attributes)
                    {
                        var symbolInfo = context.SemanticModel.GetSymbolInfo(attributeSyntax);

                        var symbol = symbolInfo.Symbol;
                        if (symbol == null)
                        {
                            return; // compilation error
                        }

                        try
                        {
                            // Major call to instantiate a reflection Binding attribute from a symbol.
                            // Need this so we can pass the attribute to WebJobs's binding engine.
                            // throws if fails to instantiate
                            Attribute attribute = ReflectionHelpers.MakeAttr(_tooling, context.SemanticModel, attributeSyntax);
                            if (attribute == null)
                            {
                                continue;
                            }

                            // At this point, we know we're looking at a webjobs parameter.
                            if (parameterType == null)
                            {
                                parameterType = ReflectionHelpers.GetParameterType(context, parameterSyntax);
                                if (parameterType == null)
                                {
                                    return; // errors in signature
                                }
                            }

                            // Report errors from invalid attribute properties.
                            ValidateAttribute(context, attribute, attributeSyntax);
                        }
                        catch (Exception e)
                        {
                            return;
                        }
                    }
                }
            }
        }

        // First argument to the FunctionName ctor.
        private string GetFunctionNameFromAttribute(SemanticModel semantics, AttributeSyntax attributeSyntax)
        {
            if (attributeSyntax.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            var firstArg = attributeSyntax.ArgumentList.Arguments[0];
            var val = semantics.GetConstantValue(firstArg.Expression);

            return val.Value as string;
        }

        // Quick check for [FunctionName] attribute on a method.
        // Reports a diagnostic if the name doesn't meet requirements.
        private void CheckForFunctionNameAttributeAndReport(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax methodDeclarationSyntax)
        {
            foreach (var attrListSyntax in methodDeclarationSyntax.AttributeLists)
            {
                foreach (AttributeSyntax attributeSyntax in attrListSyntax.Attributes)
                {
                    // Perf - Can we get the name without doing a symbol resolution?
                    var symAttributeCtor = context.SemanticModel.GetSymbolInfo(attributeSyntax).Symbol;
                    if (symAttributeCtor != null)
                    {
                        var attrType = symAttributeCtor.ContainingType;
                        if (attrType.Name != nameof(FunctionNameAttribute))
                        {
                            return;
                        }

                        // Validate the FunctionName
                        var functionName = GetFunctionNameFromAttribute(context.SemanticModel, attributeSyntax);

                        bool match = !string.IsNullOrEmpty(functionName) && FunctionNameAttribute.FunctionNameValidationRegex.IsMatch(functionName);
                        if (!match)
                        {
                            var error = Diagnostic.Create(DiagnosticDescriptors.IllegalFunctionName, attributeSyntax.GetLocation(), functionName);
                            context.ReportDiagnostic(error);
                        }
                    }
                }
            }
        }

        // Given an instantiated attribute, run the validators on it and report back any errors.
        // Attribute is the live attribute, constructed from the attributeSyntax node in the user's source code.
        private void ValidateAttribute(SyntaxNodeAnalysisContext context, Attribute attribute, AttributeSyntax attributeSyntax)
        {
            SemanticModel semantics = context.SemanticModel;
            Type attributeType = attribute.GetType();

            IMethodSymbol symAttributeCtor = (IMethodSymbol)semantics.GetSymbolInfo(attributeSyntax).Symbol;
            var syntaxParams = symAttributeCtor.Parameters;

            int idx = 0;
            if (attributeSyntax.ArgumentList != null)
            {
                foreach (AttributeArgumentSyntax arg in attributeSyntax.ArgumentList.Arguments)
                {
                    string argName = null;
                    if (arg.NameColon != null)
                    {
                        argName = arg.NameColon.Name.ToString();
                    }
                    else if (arg.NameEquals != null)
                    {
                        argName = arg.NameEquals.Name.ToString();
                    }
                    else
                    {
                        argName = syntaxParams[idx].Name; // Positional
                    }

                    PropertyInfo propInfo = attributeType.GetProperty(argName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
                    if (propInfo != null)
                    {
                        ValidateAttributeProperty(context, attribute, propInfo, arg);
                    }

                    idx++;
                }
            }
        }

        // Validate an individual property on the attribute
        // propInfo is a property on the attribute.
        private void ValidateAttributeProperty(SyntaxNodeAnalysisContext context, Attribute attribute, PropertyInfo propInfo, AttributeArgumentSyntax attributeSyntax)
        {
            var propValue = propInfo.GetValue(attribute);
            var propertyAttributes = propInfo.GetCustomAttributes();

            // First validate [AutoResolve] and [AppSetting].
            // Then do validators.
            bool isAutoResolve = false;
            bool isAppSetting = false;
            MethodInfo validator = null;
            Attribute validatorAttribute = null;

            foreach (Attribute propertyAttribute in propertyAttributes)
            {
                // AutoResolve and AppSetting are exclusive.
                var propAttrType = propertyAttribute.GetType();
                if (propAttrType == typeof(Microsoft.Azure.WebJobs.Description.AutoResolveAttribute))
                {
                    isAutoResolve = true;
                }
                if (propAttrType == typeof(Microsoft.Azure.WebJobs.Description.AppSettingAttribute))
                {
                    isAppSetting = true;
                }

                if (validator == null)
                {
                    validator = propAttrType.GetMethod("Validate", new Type[] { typeof(object), typeof(string) });
                    validatorAttribute = propertyAttribute;
                }
            }

            // Now apply error checks in order.
            if (isAutoResolve)
            {
                // Value should parse with { } and %%
                try
                {
                    if (propValue is string valueStr)
                    {
                        var template = Microsoft.Azure.WebJobs.Host.Bindings.Path.BindingTemplate.FromString(valueStr);
                        if (template.HasParameters)
                        {
                            // The validator runs after the { } and %% are substituted.
                            // But {} and %% may be illegal characters, so we can't validate with them.
                            // So skip validation.
                            // TODO - could we have some "dummy" substitution so that we can still do validation?
                            return;
                        }
                    }
                }
                catch (FormatException e)
                {
                    // Parse error
                    var error = Diagnostic.Create(DiagnosticDescriptors.BadBindingExpressionSyntax, attributeSyntax.GetLocation(), propInfo.Name, propValue, e.Message);
                    context.ReportDiagnostic(error);
                    return;
                }
            }
            else if (isAppSetting)
            {
                // TODO - validate the appsetting. In local.json? etc?
            }

            if (validator != null)
            {
                // Run Validators.
                //   If this is an autoresolve/appsetting, technically we should do the runtime substitution
                // for the %appsetting% and {key} tokens.

                // We'd like to get all attributes deriving from ValidationAttribute.
                // But that's net20, and the analyzer is net451, so we can't reference the right type.
                // Need to do a dynamic dispatch to ValidationAttribute.Validate(object,string).

                try
                {
                    // attr.Validate(value, propInfo.Name);
                    validator.Invoke(validatorAttribute, new object[] { propValue, propInfo.Name });
                }
                catch (TargetInvocationException te)
                {
                    var ex = te.InnerException;
                    var diagnostic = Diagnostic.Create(DiagnosticDescriptors.FailedValidation, attributeSyntax.GetLocation(), propInfo.Name, propValue, ex.Message);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }
}
