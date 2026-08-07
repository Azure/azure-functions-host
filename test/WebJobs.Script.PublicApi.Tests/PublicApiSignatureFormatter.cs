// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Formats compiled reflection metadata into deterministic, invariant public API signature text.
/// </summary>
/// <remarks>
/// The formatter extends the reflection formatting pattern already used by the Phase 0
/// <c>EnvironmentMigrationSourceScanner</c>. It adds no package dependency and never reads
/// per-build identity such as file versions, source paths, timestamps, or module version ids.
/// </remarks>
internal static class PublicApiSignatureFormatter
{
    private const string ReadOnlyAttribute = "System.Runtime.CompilerServices.IsReadOnlyAttribute";
    private const string ExternalInitType = "System.Runtime.CompilerServices.IsExternalInit";

    /// <summary>
    /// Attributes that are reproduced verbatim on a record because they change binary or source compatibility.
    /// </summary>
    private static readonly HashSet<string> ReportedAttributes = new(StringComparer.Ordinal)
    {
        "System.CLSCompliantAttribute",
        "System.ComponentModel.EditorBrowsableAttribute",
        "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute",
        "System.FlagsAttribute",
        "System.ObsoleteAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Runtime.InteropServices.ComVisibleAttribute",
        "System.Runtime.InteropServices.GuidAttribute",
        "System.Runtime.InteropServices.InterfaceTypeAttribute"
    };

    /// <summary>
    /// Gets the effective, externally visible accessibility of a type, walking every containing type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The effective accessibility, or <see langword="null"/> when the type is not externally visible.</returns>
    public static string GetEffectiveTypeAccessibility(Type type)
    {
        string current = null;

        for (Type declaring = type; declaring is not null; declaring = declaring.DeclaringType)
        {
            string level = declaring.IsNested
                ? declaring switch
                {
                    { IsNestedPublic: true } => "public",
                    { IsNestedFamily: true } => "protected",
                    { IsNestedFamORAssem: true } => "protected internal",
                    _ => null
                }
                : declaring.IsPublic ? "public" : null;

            if (level is null)
            {
                return null;
            }

            current = Narrow(current, level);
        }

        return current;
    }

    /// <summary>
    /// Gets the declared accessibility of a member, or <see langword="null"/> when it is not externally visible.
    /// </summary>
    /// <param name="member">The member to inspect.</param>
    /// <returns>The accessibility text, or <see langword="null"/> when the member is internal or private.</returns>
    public static string GetMemberAccessibility(MemberInfo member)
    {
        return member switch
        {
            MethodBase method => FromMethodAttributes(method.Attributes),
            FieldInfo field => FromFieldAttributes(field.Attributes),
            PropertyInfo property => MostVisible(GetAccessorAccessibility(property.GetMethod), GetAccessorAccessibility(property.SetMethod)),
            EventInfo @event => MostVisible(GetAccessorAccessibility(@event.AddMethod), GetAccessorAccessibility(@event.RemoveMethod)),
            Type nested => GetEffectiveTypeAccessibility(nested),
            _ => null
        };
    }

    /// <summary>
    /// Formats a type declaration, including kind, modifiers, generic parameters, base type, and interfaces.
    /// </summary>
    /// <param name="type">The type to format.</param>
    /// <param name="accessibility">The effective accessibility of the type.</param>
    /// <returns>The canonical type declaration text.</returns>
    public static string FormatTypeDeclaration(Type type, string accessibility)
    {
        var builder = new StringBuilder();
        AppendAttributes(builder, FormatTypeAttributes(type));
        builder.Append(accessibility);

        foreach (string modifier in GetTypeModifiers(type))
        {
            builder.Append(' ').Append(modifier);
        }

        builder.Append(' ').Append(GetTypeKind(type));
        builder.Append(' ').Append(FormatDeclarationName(type));

        if (type.IsEnum)
        {
            builder.Append(" : ").Append(FormatTypeName(type.GetEnumUnderlyingType(), nullability: null));
        }
        else
        {
            string bases = string.Join(", ", GetDirectBaseTypes(type).Select(baseType => FormatTypeName(baseType, nullability: null)));
            if (!string.IsNullOrEmpty(bases))
            {
                builder.Append(" : ").Append(bases);
            }
        }

        builder.Append(FormatGenericConstraints(type.IsGenericTypeDefinition ? type.GetGenericArguments() : Type.EmptyTypes));

        return builder.ToString();
    }

    /// <summary>
    /// Formats a constructor signature.
    /// </summary>
    /// <param name="constructor">The constructor to format.</param>
    /// <param name="accessibility">The declared accessibility.</param>
    /// <returns>The canonical constructor signature text.</returns>
    public static string FormatConstructor(ConstructorInfo constructor, string accessibility)
    {
        var builder = new StringBuilder();
        AppendAttributes(builder, FormatMemberAttributes(constructor));
        builder.Append(accessibility);

        if (constructor.IsStatic)
        {
            builder.Append(" static");
        }

        builder.Append(' ').Append(GetSimpleTypeName(constructor.DeclaringType));
        builder.Append('(').Append(FormatParameters(constructor)).Append(')');

        return builder.ToString();
    }

    /// <summary>
    /// Formats a method signature, including operators and conversions.
    /// </summary>
    /// <param name="method">The method to format.</param>
    /// <param name="accessibility">The declared accessibility.</param>
    /// <returns>The canonical method signature text.</returns>
    public static string FormatMethod(MethodInfo method, string accessibility)
    {
        var builder = new StringBuilder();
        AppendAttributes(builder, FormatMemberAttributes(method));
        builder.Append(accessibility);

        foreach (string modifier in GetMethodModifiers(method))
        {
            builder.Append(' ').Append(modifier);
        }

        builder.Append(' ').Append(FormatReturnType(method));
        builder.Append(' ').Append(method.Name);

        if (method.IsGenericMethodDefinition)
        {
            builder.Append('<')
                .Append(string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)))
                .Append('>');
        }

        builder.Append('(').Append(FormatParameters(method)).Append(')');
        builder.Append(FormatGenericConstraints(method.IsGenericMethodDefinition ? method.GetGenericArguments() : Type.EmptyTypes));

        return builder.ToString();
    }

    /// <summary>
    /// Formats a property or indexer signature, including accessor-specific visibility.
    /// </summary>
    /// <param name="property">The property to format.</param>
    /// <param name="accessibility">The effective accessibility of the property.</param>
    /// <returns>The canonical property signature text.</returns>
    public static string FormatProperty(PropertyInfo property, string accessibility)
    {
        var builder = new StringBuilder();
        AppendAttributes(builder, FormatMemberAttributes(property));
        builder.Append(accessibility);

        MethodInfo representative = property.GetMethod ?? property.SetMethod;
        foreach (string modifier in GetMethodModifiers(representative))
        {
            builder.Append(' ').Append(modifier);
        }

        if (property.PropertyType.IsByRef)
        {
            builder.Append(HasReadOnlyAttribute(property) ? " ref readonly" : " ref");
        }

        builder.Append(' ').Append(FormatTypeName(property.PropertyType, TryCreateNullability(property)));
        builder.Append(' ').Append(property.Name);

        ParameterInfo[] indexParameters = property.GetIndexParameters();
        if (indexParameters.Length > 0)
        {
            builder.Append('[').Append(FormatParameters(indexParameters, isExtension: false)).Append(']');
        }

        builder.Append(" { ");
        AppendAccessor(builder, property.GetMethod, "get", accessibility);
        AppendAccessor(builder, property.SetMethod, IsInitOnly(property.SetMethod) ? "init" : "set", accessibility);
        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>
    /// Formats an event signature, including accessor visibility.
    /// </summary>
    /// <param name="eventInfo">The event to format.</param>
    /// <param name="accessibility">The effective accessibility of the event.</param>
    /// <returns>The canonical event signature text.</returns>
    public static string FormatEvent(EventInfo eventInfo, string accessibility)
    {
        var builder = new StringBuilder();
        AppendAttributes(builder, FormatMemberAttributes(eventInfo));
        builder.Append(accessibility);

        MethodInfo representative = eventInfo.AddMethod ?? eventInfo.RemoveMethod;
        foreach (string modifier in GetMethodModifiers(representative))
        {
            builder.Append(' ').Append(modifier);
        }

        builder.Append(" event ").Append(FormatTypeName(eventInfo.EventHandlerType, TryCreateNullability(eventInfo)));
        builder.Append(' ').Append(eventInfo.Name);

        builder.Append(" { ");
        AppendAccessor(builder, eventInfo.AddMethod, "add", accessibility);
        AppendAccessor(builder, eventInfo.RemoveMethod, "remove", accessibility);
        builder.Append('}');

        return builder.ToString();
    }

    /// <summary>
    /// Formats a field signature, including constant and enum values.
    /// </summary>
    /// <param name="field">The field to format.</param>
    /// <param name="accessibility">The declared accessibility.</param>
    /// <returns>The canonical field signature text.</returns>
    public static string FormatField(FieldInfo field, string accessibility)
    {
        var builder = new StringBuilder();
        AppendAttributes(builder, FormatMemberAttributes(field));
        builder.Append(accessibility);

        string attributeConstant = TryFormatAttributeConstant(field);
        bool isConstant = field.IsLiteral || attributeConstant is not null;

        if (isConstant)
        {
            builder.Append(" const");
        }
        else
        {
            if (field.IsStatic)
            {
                builder.Append(" static");
            }

            if (field.IsInitOnly)
            {
                builder.Append(" readonly");
            }
        }

        builder.Append(' ').Append(FormatTypeName(field.FieldType, TryCreateNullability(field)));
        builder.Append(' ').Append(field.Name);

        if (isConstant)
        {
            builder.Append(" = ").Append(attributeConstant ?? FormatConstant(GetRawConstantValue(field), field.FieldType));
        }

        return builder.ToString();
    }

    private static string TryFormatAttributeConstant(FieldInfo field)
    {
        foreach (CustomAttributeData attribute in field.CustomAttributes)
        {
            string name = attribute.AttributeType.FullName;

            if (string.Equals(name, "System.Runtime.CompilerServices.DecimalConstantAttribute", StringComparison.Ordinal)
                && attribute.ConstructorArguments.Count == 5)
            {
                byte scale = Convert.ToByte(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture);
                byte sign = Convert.ToByte(attribute.ConstructorArguments[1].Value, CultureInfo.InvariantCulture);
                int high = unchecked((int)Convert.ToUInt32(attribute.ConstructorArguments[2].Value, CultureInfo.InvariantCulture));
                int middle = unchecked((int)Convert.ToUInt32(attribute.ConstructorArguments[3].Value, CultureInfo.InvariantCulture));
                int low = unchecked((int)Convert.ToUInt32(attribute.ConstructorArguments[4].Value, CultureInfo.InvariantCulture));

                return FormatPrimitive(new decimal(low, middle, high, sign != 0, scale));
            }

            if (string.Equals(name, "System.Runtime.CompilerServices.DateTimeConstantAttribute", StringComparison.Ordinal)
                && attribute.ConstructorArguments.Count == 1)
            {
                return FormatPrimitive(new DateTime(Convert.ToInt64(attribute.ConstructorArguments[0].Value, CultureInfo.InvariantCulture)));
            }
        }

        return null;
    }

    /// <summary>
    /// Formats a fully qualified type name with optional nullability annotations.
    /// </summary>
    /// <param name="type">The type to format.</param>
    /// <param name="nullability">The nullability information for the type, or <see langword="null"/> when unavailable.</param>
    /// <returns>The canonical type name.</returns>
    public static string FormatTypeName(Type type, NullabilityInfo nullability)
    {
        if (type is null)
        {
            return "<null>";
        }

        if (type.IsByRef)
        {
            return FormatTypeName(type.GetElementType(), nullability);
        }

        if (type.IsPointer)
        {
            return FormatTypeName(type.GetElementType(), nullability?.ElementType) + "*";
        }

        if (type.IsArray)
        {
            string rank = type.GetArrayRank() == 1 ? "[]" : $"[{new string(',', type.GetArrayRank() - 1)}]";
            return FormatTypeName(type.GetElementType(), nullability?.ElementType) + rank + FormatAnnotation(type, nullability);
        }

        if (type.IsGenericParameter)
        {
            return type.Name + FormatAnnotation(type, nullability);
        }

        string name = GetOpenTypeName(type);

        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            NullabilityInfo[] argumentNullability = nullability?.GenericTypeArguments;
            var formatted = new string[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                NullabilityInfo argumentInfo = argumentNullability is not null && index < argumentNullability.Length
                    ? argumentNullability[index]
                    : null;
                formatted[index] = FormatTypeName(arguments[index], argumentInfo);
            }

            name += "<" + string.Join(", ", formatted) + ">";
        }

        return name + FormatAnnotation(type, nullability);
    }

    /// <summary>
    /// Formats a constant or default value using invariant, deterministically escaped text.
    /// </summary>
    /// <param name="value">The constant value.</param>
    /// <param name="declaredType">The declared type of the value.</param>
    /// <returns>The canonical constant text.</returns>
    public static string FormatConstant(object value, Type declaredType)
    {
        Type target = declaredType is null || declaredType.IsByRef
            ? declaredType?.GetElementType()
            : declaredType;

        if (value is null)
        {
            return target is not null && target.IsValueType && Nullable.GetUnderlyingType(target) is null
                ? "default"
                : "null";
        }

        if (value is Missing || value is DBNull)
        {
            return "default";
        }

        if (target is not null && target.IsEnum)
        {
            return $"({FormatTypeName(target, nullability: null)}){FormatPrimitive(Convert.ChangeType(value, target.GetEnumUnderlyingType(), CultureInfo.InvariantCulture))}";
        }

        Type underlying = target is not null && Nullable.GetUnderlyingType(target) is Type nullableTarget ? nullableTarget : target;
        if (underlying is not null && underlying.IsEnum)
        {
            return $"({FormatTypeName(underlying, nullability: null)}){FormatPrimitive(Convert.ChangeType(value, underlying.GetEnumUnderlyingType(), CultureInfo.InvariantCulture))}";
        }

        return FormatPrimitive(value);
    }

    /// <summary>
    /// Determines whether a method is the accessor of a property or event on the declaring type.
    /// </summary>
    /// <param name="method">The method to inspect.</param>
    /// <param name="accessors">The set of accessor methods already collected for the declaring type.</param>
    /// <returns><see langword="true"/> when the method is represented by its owning property or event.</returns>
    public static bool IsAccessor(MethodInfo method, HashSet<MethodInfo> accessors)
    {
        return accessors.Contains(method);
    }

    /// <summary>
    /// Collects every property and event accessor declared on a type.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The accessor methods.</returns>
    public static HashSet<MethodInfo> CollectAccessors(Type type)
    {
        var accessors = new HashSet<MethodInfo>();

        foreach (PropertyInfo property in type.GetProperties(PublicApiSnapshotBuilder.DeclaredMembers))
        {
            Add(accessors, property.GetMethod);
            Add(accessors, property.SetMethod);
        }

        foreach (EventInfo eventInfo in type.GetEvents(PublicApiSnapshotBuilder.DeclaredMembers))
        {
            Add(accessors, eventInfo.AddMethod);
            Add(accessors, eventInfo.RemoveMethod);
            Add(accessors, eventInfo.RaiseMethod);
        }

        return accessors;

        static void Add(HashSet<MethodInfo> set, MethodInfo method)
        {
            if (method is not null)
            {
                set.Add(method);
            }
        }
    }

    private static string FormatReturnType(MethodInfo method)
    {
        string returnType = FormatTypeName(method.ReturnType, TryCreateNullability(method.ReturnParameter));

        if (!method.ReturnType.IsByRef)
        {
            return returnType;
        }

        return HasReadOnlyAttribute(method.ReturnParameter)
            ? $"ref readonly {returnType}"
            : $"ref {returnType}";
    }

    private static string FormatParameters(MethodBase method)
    {
        bool isExtension = method.CustomAttributes.Any(attribute =>
            string.Equals(attribute.AttributeType.FullName, "System.Runtime.CompilerServices.ExtensionAttribute", StringComparison.Ordinal));

        return FormatParameters(method.GetParameters(), isExtension);
    }

    private static string FormatParameters(ParameterInfo[] parameters, bool isExtension)
    {
        return string.Join(", ", parameters.Select((parameter, index) =>
        {
            var builder = new StringBuilder();

            if (isExtension && index == 0)
            {
                builder.Append("this ");
            }

            if (parameter.ParameterType.IsByRef)
            {
                if (parameter.IsOut)
                {
                    builder.Append("out ");
                }
                else if (HasReadOnlyAttribute(parameter))
                {
                    builder.Append("in ");
                }
                else
                {
                    builder.Append("ref ");
                }
            }

            if (parameter.CustomAttributes.Any(attribute =>
                string.Equals(attribute.AttributeType.FullName, "System.ParamArrayAttribute", StringComparison.Ordinal)))
            {
                builder.Append("params ");
            }

            builder.Append(FormatTypeName(parameter.ParameterType, TryCreateNullability(parameter)));
            builder.Append(' ').Append(parameter.Name ?? "<unnamed>");

            if (parameter.IsOptional)
            {
                builder.Append(" = ").Append(FormatConstant(GetParameterDefault(parameter), parameter.ParameterType));
            }

            return builder.ToString();
        }));
    }

    private static object GetParameterDefault(ParameterInfo parameter)
    {
        try
        {
            return parameter.DefaultValue;
        }
        catch (FormatException)
        {
            return parameter.RawDefaultValue;
        }
        catch (NotSupportedException)
        {
            return parameter.RawDefaultValue;
        }
    }

    private static object GetRawConstantValue(FieldInfo field)
    {
        try
        {
            return field.GetRawConstantValue();
        }
        catch (NotSupportedException)
        {
            return Missing.Value;
        }
    }

    private static string FormatPrimitive(object value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{EscapeString(text)}\"",
            char character => $"'{EscapeString(character.ToString())}'",
            bool boolean => boolean ? "true" : "false",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
    }

    private static string EscapeString(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\'':
                    builder.Append("\\'");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character) || character > 0x7E)
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendAccessor(StringBuilder builder, MethodInfo accessor, string keyword, string ownerAccessibility)
    {
        if (accessor is null)
        {
            return;
        }

        string accessibility = FromMethodAttributes(accessor.Attributes);
        if (accessibility is null)
        {
            return;
        }

        if (!string.Equals(accessibility, ownerAccessibility, StringComparison.Ordinal))
        {
            builder.Append(accessibility).Append(' ');
        }

        builder.Append(keyword).Append("; ");
    }

    private static IEnumerable<string> GetTypeModifiers(Type type)
    {
        if (type.IsEnum || type.IsInterface || IsDelegate(type))
        {
            yield break;
        }

        if (type.IsValueType)
        {
            if (type.IsByRefLike)
            {
                yield return "ref";
            }

            if (HasReadOnlyAttribute(type))
            {
                yield return "readonly";
            }

            yield break;
        }

        if (type.IsAbstract && type.IsSealed)
        {
            yield return "static";
            yield break;
        }

        if (type.IsAbstract)
        {
            yield return "abstract";
        }

        if (type.IsSealed)
        {
            yield return "sealed";
        }
    }

    private static IEnumerable<string> GetMethodModifiers(MethodInfo method)
    {
        if (method is null)
        {
            yield break;
        }

        if (method.IsStatic)
        {
            yield return "static";
        }

        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
        {
            yield return "extern";
        }

        if ((method.CallingConvention & CallingConventions.VarArgs) != 0)
        {
            yield return "varargs";
        }

        if (!method.IsVirtual)
        {
            yield break;
        }

        bool isOverride = (method.Attributes & MethodAttributes.NewSlot) == 0;

        if (method.IsAbstract)
        {
            yield return isOverride ? "abstract override" : "abstract";
            yield break;
        }

        if (method.IsFinal)
        {
            yield return isOverride ? "sealed override" : "sealed";
            yield break;
        }

        yield return isOverride ? "override" : "virtual";
    }

    private static string GetTypeKind(Type type)
    {
        if (type.IsEnum)
        {
            return "enum";
        }

        if (IsDelegate(type))
        {
            return "delegate";
        }

        if (type.IsInterface)
        {
            return "interface";
        }

        bool isRecord = type.GetMethod("<Clone>$", PublicApiSnapshotBuilder.DeclaredMembers) is not null
            || HasCompilerGeneratedPrintMembers(type);

        if (type.IsValueType)
        {
            return isRecord ? "record struct" : "struct";
        }

        return isRecord ? "record class" : "class";
    }

    private static bool HasCompilerGeneratedPrintMembers(Type type)
    {
        MethodInfo printMembers = type
            .GetMethods(PublicApiSnapshotBuilder.DeclaredMembers)
            .FirstOrDefault(method => string.Equals(method.Name, "PrintMembers", StringComparison.Ordinal)
                && method.ReturnType == typeof(bool)
                && method.GetParameters() is [{ ParameterType.FullName: "System.Text.StringBuilder" }]);

        return printMembers is not null
            && printMembers.CustomAttributes.Any(attribute =>
                string.Equals(attribute.AttributeType.FullName, "System.Runtime.CompilerServices.CompilerGeneratedAttribute", StringComparison.Ordinal));
    }

    private static bool IsDelegate(Type type)
    {
        return type.BaseType is not null
            && (string.Equals(type.BaseType.FullName, "System.MulticastDelegate", StringComparison.Ordinal)
                || string.Equals(type.BaseType.FullName, "System.Delegate", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> GetDirectBaseTypes(Type type)
    {
        if (type.BaseType is not null
            && !string.Equals(type.BaseType.FullName, "System.Object", StringComparison.Ordinal)
            && !string.Equals(type.BaseType.FullName, "System.ValueType", StringComparison.Ordinal)
            && !string.Equals(type.BaseType.FullName, "System.Enum", StringComparison.Ordinal)
            && !IsDelegate(type))
        {
            yield return type.BaseType;
        }

        foreach (Type contract in GetDirectInterfaces(type))
        {
            yield return contract;
        }
    }

    private static IEnumerable<Type> GetDirectInterfaces(Type type)
    {
        Type[] all = type.GetInterfaces();
        var inherited = new HashSet<Type>(type.BaseType?.GetInterfaces() ?? Type.EmptyTypes);

        foreach (Type contract in all)
        {
            foreach (Type transitive in contract.GetInterfaces())
            {
                inherited.Add(transitive);
            }
        }

        return all
            .Where(contract => !inherited.Contains(contract))
            .OrderBy(contract => contract.FullName ?? contract.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FormatGenericConstraints(Type[] genericArguments)
    {
        var builder = new StringBuilder();

        foreach (Type argument in genericArguments.Where(argument => argument.IsGenericParameter))
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

            constraints.AddRange(argument.GetGenericParameterConstraints()
                .Where(constraint => !string.Equals(constraint.FullName, "System.ValueType", StringComparison.Ordinal))
                .Select(constraint => FormatTypeName(constraint, nullability: null))
                .OrderBy(constraint => constraint, StringComparer.Ordinal));

            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                && (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0)
            {
                builder.Append(" where ").Append(argument.Name).Append(" : ").Append(string.Join(", ", constraints));
            }
        }

        return builder.ToString();
    }

    private static string FormatAnnotation(Type type, NullabilityInfo nullability)
    {
        if (nullability is null || type.IsValueType || type.IsPointer)
        {
            return string.Empty;
        }

        return nullability.ReadState switch
        {
            NullabilityState.Nullable => "?",
            NullabilityState.NotNull => "!",
            _ => string.Empty
        };
    }

    private static NullabilityInfo TryCreateNullability(ParameterInfo parameter)
    {
        return TryCreate(context => context.Create(parameter));
    }

    private static NullabilityInfo TryCreateNullability(PropertyInfo property)
    {
        return TryCreate(context => context.Create(property));
    }

    private static NullabilityInfo TryCreateNullability(FieldInfo field)
    {
        return TryCreate(context => context.Create(field));
    }

    private static NullabilityInfo TryCreateNullability(EventInfo eventInfo)
    {
        return TryCreate(context => context.Create(eventInfo));
    }

    private static NullabilityInfo TryCreate(Func<NullabilityInfoContext, NullabilityInfo> create)
    {
        try
        {
            return create(new NullabilityInfoContext());
        }
        catch (Exception exception) when (exception is NotSupportedException or ArgumentException or InvalidOperationException or TypeLoadException or FileNotFoundException)
        {
            return null;
        }
    }

    private static bool IsInitOnly(MethodInfo setter)
    {
        return setter is not null
            && setter.ReturnParameter.GetRequiredCustomModifiers()
                .Any(modifier => string.Equals(modifier.FullName, ExternalInitType, StringComparison.Ordinal));
    }

    private static bool HasReadOnlyAttribute(MemberInfo member)
    {
        return member.CustomAttributes.Any(attribute =>
            string.Equals(attribute.AttributeType.FullName, ReadOnlyAttribute, StringComparison.Ordinal));
    }

    private static bool HasReadOnlyAttribute(ParameterInfo parameter)
    {
        return parameter.CustomAttributes.Any(attribute =>
            string.Equals(attribute.AttributeType.FullName, ReadOnlyAttribute, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string> FormatTypeAttributes(Type type)
    {
        var attributes = new List<string>(FormatDeclaredAttributes(type.CustomAttributes));

        if (TryFormatStructLayout(type) is string layout)
        {
            attributes.Add(layout);
        }

        attributes.Sort(StringComparer.Ordinal);

        return attributes;
    }

    private static IReadOnlyList<string> FormatMemberAttributes(MemberInfo member)
    {
        var attributes = new List<string>(FormatDeclaredAttributes(member.CustomAttributes));

        if (member is MethodBase method && (method.Attributes & MethodAttributes.PinvokeImpl) != 0)
        {
            attributes.Add($"[DllImport(CallingConvention={method.CallingConvention})]");
        }

        attributes.Sort(StringComparer.Ordinal);

        return attributes;
    }

    private static IEnumerable<string> FormatDeclaredAttributes(IEnumerable<CustomAttributeData> attributes)
    {
        foreach (CustomAttributeData attribute in attributes)
        {
            string fullName = attribute.AttributeType.FullName;
            if (fullName is null || !ReportedAttributes.Contains(fullName))
            {
                continue;
            }

            string name = GetSimpleTypeName(attribute.AttributeType);
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
            {
                name = name[..^"Attribute".Length];
            }

            var arguments = new List<string>();
            arguments.AddRange(attribute.ConstructorArguments.Select(FormatAttributeArgument));
            arguments.AddRange(attribute.NamedArguments
                .Select(named => $"{named.MemberName}={FormatAttributeArgument(named.TypedValue)}")
                .OrderBy(text => text, StringComparer.Ordinal));

            yield return arguments.Count == 0
                ? $"[{name}]"
                : $"[{name}({string.Join(", ", arguments)})]";
        }
    }

    private static string FormatAttributeArgument(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is IReadOnlyList<CustomAttributeTypedArgument> list)
        {
            return $"[{string.Join(", ", list.Select(FormatAttributeArgument))}]";
        }

        if (argument.Value is Type typeValue)
        {
            return $"typeof({FormatTypeName(typeValue, nullability: null)})";
        }

        return FormatConstant(argument.Value, argument.ArgumentType);
    }

    private static string TryFormatStructLayout(Type type)
    {
        StructLayoutAttribute layout;
        try
        {
            layout = type.StructLayoutAttribute;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (layout is null)
        {
            return null;
        }

        LayoutKind expected = type.IsValueType && !type.IsEnum ? LayoutKind.Sequential : LayoutKind.Auto;
        if (layout.Value == expected && layout.Pack == 0 && layout.Size == 0 && layout.CharSet == CharSet.Ansi)
        {
            return null;
        }

        return $"[StructLayout({layout.Value}, Pack={layout.Pack.ToString(CultureInfo.InvariantCulture)}, Size={layout.Size.ToString(CultureInfo.InvariantCulture)}, CharSet={layout.CharSet})]";
    }

    private static void AppendAttributes(StringBuilder builder, IReadOnlyList<string> attributes)
    {
        foreach (string attribute in attributes)
        {
            builder.Append(attribute).Append(' ');
        }
    }

    private static string GetOpenTypeName(Type type)
    {
        var segments = new List<string>();

        for (Type current = type; current is not null; current = current.DeclaringType)
        {
            segments.Insert(0, TrimArity(current.Name));

            if (!current.IsNested)
            {
                if (!string.IsNullOrEmpty(current.Namespace))
                {
                    segments.Insert(0, current.Namespace);
                }

                break;
            }
        }

        return string.Join(".", segments);
    }

    private static string FormatDeclarationName(Type type)
    {
        string name = GetOpenTypeName(type);

        if (!type.IsGenericTypeDefinition)
        {
            return name;
        }

        IEnumerable<string> parameters = type.GetGenericArguments().Select(argument =>
        {
            string variance = (argument.GenericParameterAttributes & GenericParameterAttributes.VarianceMask) switch
            {
                GenericParameterAttributes.Covariant => "out ",
                GenericParameterAttributes.Contravariant => "in ",
                _ => string.Empty
            };

            return variance + argument.Name;
        });

        return $"{name}<{string.Join(", ", parameters)}>";
    }

    private static string GetSimpleTypeName(Type type)
    {
        return type is null ? "<null>" : TrimArity(type.Name);
    }

    private static string TrimArity(string name)
    {
        int index = name.IndexOf('`', StringComparison.Ordinal);
        return index < 0 ? name : name[..index];
    }

    private static string FromMethodAttributes(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Family => "protected",
            MethodAttributes.FamORAssem => "protected internal",
            _ => null
        };
    }

    private static string FromFieldAttributes(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => "public",
            FieldAttributes.Family => "protected",
            FieldAttributes.FamORAssem => "protected internal",
            _ => null
        };
    }

    private static string GetAccessorAccessibility(MethodInfo accessor)
    {
        return accessor is null ? null : FromMethodAttributes(accessor.Attributes);
    }

    private static string MostVisible(string left, string right)
    {
        return Rank(left) >= Rank(right) ? left : right;
    }

    private static string Narrow(string left, string right)
    {
        if (left is null)
        {
            return right;
        }

        return Rank(left) <= Rank(right) ? left : right;
    }

    private static int Rank(string accessibility)
    {
        return accessibility switch
        {
            "public" => 3,
            "protected internal" => 2,
            "protected" => 1,
            _ => 0
        };
    }
}
