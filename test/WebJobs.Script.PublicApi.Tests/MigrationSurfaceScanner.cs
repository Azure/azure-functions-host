// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Recomputes the focused <c>IEnvironment</c> migration surface directly from the shipped Release
/// assemblies, using the same signature shape as the Phase 0 source scanner.
/// </summary>
/// <remarks>
/// The classification ledger is cross-checked against this live scan so that neither the ledger nor
/// the Phase 0 inventory can drift silently. All three shipped host assemblies are loaded into a
/// single isolated context so that cross-assembly type identity is preserved.
/// </remarks>
internal static class MigrationSurfaceScanner
{
    private const string EnvironmentExtensionsTypeName = "Microsoft.Azure.WebJobs.Script.EnvironmentExtensions";
    private const string EnvironmentInterfaceTypeName = "Microsoft.Azure.WebJobs.Script.IEnvironment";
    private const string SystemEnvironmentTypeName = "Microsoft.Azure.WebJobs.Script.SystemEnvironment";
    private const string ScriptSettingsManagerTypeName = "Microsoft.Azure.WebJobs.Script.Config.ScriptSettingsManager";

    private static readonly string[] ScannedAssemblies =
    {
        "Microsoft.Azure.WebJobs.Script",
        "Microsoft.Azure.WebJobs.Script.WebHost",
        "Microsoft.Azure.WebJobs.Script.Grpc"
    };

    private static readonly Lazy<MigrationSurface> LazySurface = new(Scan, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Gets the live migration surface of the shipped Release assemblies.
    /// </summary>
    public static MigrationSurface Current => LazySurface.Value;

    private static MigrationSurface Scan()
    {
        ShippedAssemblyManifest manifest = ShippedAssemblyManifest.Load();
        IReadOnlyList<string> probeDirectories = manifest.GetProbeDirectories();

        ShippedAssemblyManifest.ShippedAssembly webHost = manifest.Assemblies
            .Single(assembly => string.Equals(assembly.BaselineAssemblyName, "Microsoft.Azure.WebJobs.Script.WebHost", StringComparison.Ordinal));

        PublicApiAssemblyLoadContext context = PublicApiSnapshotBuilder.LoadInContext(webHost.GetReleaseOutputPath(), probeDirectories);

        Assembly[] assemblies = ScannedAssemblies
            .Select(name => context.LoadFromAssemblyName(new AssemblyName(name)))
            .ToArray();

        Assembly script = assemblies[0];
        Type environmentInterface = script.GetType(EnvironmentInterfaceTypeName, throwOnError: true);
        Type environmentExtensions = script.GetType(EnvironmentExtensionsTypeName, throwOnError: true);

        var migrationTypes = new HashSet<Type>
        {
            environmentInterface,
            script.GetType(SystemEnvironmentTypeName, throwOnError: true),
            script.GetType(ScriptSettingsManagerTypeName, throwOnError: true)
        };

        string[] helpers = environmentExtensions
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => FormatMethod(method, environmentInterface))
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        var signatures = new List<string>();

        foreach (Type type in assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            bool isMigrationType = migrationTypes.Contains(type);

            if (isMigrationType || environmentInterface.IsAssignableFrom(type))
            {
                signatures.Add(FormatTypeDeclaration(type, environmentInterface));
            }

            foreach (MemberInfo member in GetDeclaredPublicMembers(type))
            {
                if (isMigrationType || ReferencesEnvironment(member, environmentInterface))
                {
                    signatures.Add(FormatMember(member, environmentInterface));
                }
            }
        }

        return new MigrationSurface(
            helpers,
            signatures.OrderBy(signature => signature, StringComparer.Ordinal).ToArray(),
            environmentExtensions.IsPublic);
    }

    private static IEnumerable<MemberInfo> GetDeclaredPublicMembers(Type type)
    {
        return type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member switch
            {
                MethodInfo method => !method.IsSpecialName,
                ConstructorInfo => true,
                PropertyInfo => true,
                FieldInfo => true,
                EventInfo => true,
                _ => false
            });
    }

    private static bool ReferencesEnvironment(MemberInfo member, Type environmentInterface)
    {
        return member switch
        {
            ConstructorInfo constructor => constructor.GetParameters().Any(parameter => ReferencesEnvironment(parameter.ParameterType, environmentInterface)),
            MethodInfo method => ReferencesEnvironment(method.ReturnType, environmentInterface)
                || method.GetParameters().Any(parameter => ReferencesEnvironment(parameter.ParameterType, environmentInterface))
                || method.GetGenericArguments().Any(argument => argument.GetGenericParameterConstraints().Any(constraint => ReferencesEnvironment(constraint, environmentInterface))),
            PropertyInfo property => ReferencesEnvironment(property.PropertyType, environmentInterface)
                || property.GetIndexParameters().Any(parameter => ReferencesEnvironment(parameter.ParameterType, environmentInterface)),
            FieldInfo field => ReferencesEnvironment(field.FieldType, environmentInterface),
            EventInfo @event => ReferencesEnvironment(@event.EventHandlerType, environmentInterface),
            _ => false
        };
    }

    private static bool ReferencesEnvironment(Type type, Type environmentInterface)
    {
        if (type is null)
        {
            return false;
        }

        if (type == environmentInterface)
        {
            return true;
        }

        if (type.HasElementType)
        {
            return ReferencesEnvironment(type.GetElementType(), environmentInterface);
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => ReferencesEnvironment(argument, environmentInterface));
    }

    private static string FormatMember(MemberInfo member, Type environmentInterface)
    {
        return member switch
        {
            ConstructorInfo constructor => $"constructor {FormatTypeName(constructor.DeclaringType)}({FormatParameters(constructor.GetParameters(), isExtensionMethod: false)})",
            MethodInfo method => FormatMethod(method, environmentInterface),
            PropertyInfo property => FormatProperty(property),
            FieldInfo field => $"field {FormatTypeName(field.FieldType)} {FormatTypeName(field.DeclaringType)}.{field.Name}",
            EventInfo @event => $"event {FormatTypeName(@event.EventHandlerType)} {FormatTypeName(@event.DeclaringType)}.{@event.Name}",
            _ => throw new InvalidOperationException($"Unsupported public member kind '{member.MemberType}'.")
        };
    }

    private static string FormatMethod(MethodInfo method, Type environmentInterface)
    {
        _ = environmentInterface;

        string genericArguments = method.IsGenericMethodDefinition
            ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
            : string.Empty;
        string parameters = FormatParameters(
            method.GetParameters(),
            method.IsDefined(typeof(ExtensionAttribute), inherit: false));

        return $"method {FormatTypeName(method.ReturnType)} {FormatTypeName(method.DeclaringType)}.{method.Name}{genericArguments}({parameters}){FormatGenericConstraints(method.GetGenericArguments())}";
    }

    private static string FormatProperty(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod?.IsPublic == true)
        {
            accessors.Add("get;");
        }

        if (property.SetMethod?.IsPublic == true)
        {
            accessors.Add("set;");
        }

        string indexParameters = property.GetIndexParameters().Length == 0
            ? string.Empty
            : $"[{FormatParameters(property.GetIndexParameters(), isExtensionMethod: false)}]";

        return $"property {FormatTypeName(property.PropertyType)} {FormatTypeName(property.DeclaringType)}.{property.Name}{indexParameters} {{ {string.Join(" ", accessors)} }}";
    }

    private static string FormatTypeDeclaration(Type type, Type environmentInterface)
    {
        _ = environmentInterface;

        string kind = type.IsInterface ? "interface" : type.IsValueType ? "struct" : "class";
        string baseTypes = string.Join(", ", GetDirectBaseTypes(type).Select(FormatTypeName));
        string suffix = string.IsNullOrEmpty(baseTypes) ? string.Empty : $" : {baseTypes}";

        return $"{kind} {FormatTypeName(type)}{suffix}{FormatGenericConstraints(type.GetGenericArguments())}";
    }

    private static IEnumerable<Type> GetDirectBaseTypes(Type type)
    {
        if (type.BaseType is not null && type.BaseType != typeof(object))
        {
            yield return type.BaseType;
        }

        foreach (Type implementedInterface in type.GetInterfaces()
            .Except(type.BaseType?.GetInterfaces() ?? Type.EmptyTypes)
            .OrderBy(interfaceType => interfaceType.FullName, StringComparer.Ordinal))
        {
            yield return implementedInterface;
        }
    }

    private static string FormatParameters(ParameterInfo[] parameters, bool isExtensionMethod)
    {
        return string.Join(", ", parameters.Select((parameter, index) =>
        {
            string modifier = parameter.IsOut
                ? "out "
                : parameter.ParameterType.IsByRef
                    ? "ref "
                    : isExtensionMethod && index == 0
                        ? "this "
                        : string.Empty;
            string defaultValue = parameter.HasDefaultValue
                ? $" = {FormatDefaultValue(parameter.DefaultValue)}"
                : string.Empty;

            return $"{modifier}{FormatTypeName(parameter.ParameterType)} {parameter.Name}{defaultValue}";
        }));
    }

    private static string FormatGenericConstraints(Type[] genericArguments)
    {
        return string.Concat(genericArguments
            .Where(argument => argument.IsGenericParameter)
            .Select(argument =>
            {
                var constraints = new List<string>();
                GenericParameterAttributes attributes = argument.GenericParameterAttributes;

                if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    constraints.Add("class");
                }

                if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
                {
                    constraints.Add("struct");
                }

                constraints.AddRange(argument.GetGenericParameterConstraints().Select(FormatTypeName));

                if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                    && (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                {
                    constraints.Add("new()");
                }

                return constraints.Count == 0
                    ? string.Empty
                    : $" where {argument.Name} : {string.Join(", ", constraints)}";
            }));
    }

    private static string FormatDefaultValue(object value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{text}\"",
            char character => $"'{character}'",
            bool boolean => boolean ? "true" : "false",
            Missing => "missing",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static string FormatTypeName(Type type)
    {
        if (type is null)
        {
            return "null";
        }

        if (type.IsByRef)
        {
            return FormatTypeName(type.GetElementType());
        }

        if (type.IsArray)
        {
            return $"{FormatTypeName(type.GetElementType())}[{new string(',', type.GetArrayRank() - 1)}]";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (!type.IsGenericType)
        {
            return (type.FullName ?? type.Name).Replace('+', '.');
        }

        string genericName = type.GetGenericTypeDefinition().FullName;
        genericName = genericName[..genericName.IndexOf('`', StringComparison.Ordinal)].Replace('+', '.');

        return $"{genericName}<{string.Join(", ", type.GetGenericArguments().Select(FormatTypeName))}>";
    }
}

/// <summary>
/// The live migration surface of the shipped Release assemblies.
/// </summary>
internal sealed class MigrationSurface
{
    public MigrationSurface(
        IReadOnlyList<string> environmentExtensionHelpers,
        IReadOnlyList<string> publicSignatures,
        bool environmentExtensionsIsPublic)
    {
        EnvironmentExtensionHelpers = environmentExtensionHelpers;
        PublicSignatures = publicSignatures;
        EnvironmentExtensionsIsPublic = environmentExtensionsIsPublic;
    }

    /// <summary>
    /// Gets the public static helpers declared by the internal <c>EnvironmentExtensions</c> class.
    /// </summary>
    public IReadOnlyList<string> EnvironmentExtensionHelpers { get; }

    /// <summary>
    /// Gets the compiled exported migration signatures.
    /// </summary>
    public IReadOnlyList<string> PublicSignatures { get; }

    /// <summary>
    /// Gets a value indicating whether <c>EnvironmentExtensions</c> is externally visible.
    /// </summary>
    public bool EnvironmentExtensionsIsPublic { get; }
}
