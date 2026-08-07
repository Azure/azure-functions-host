// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Microsoft.Azure.WebJobs.Script.PublicApi.Tests;

/// <summary>
/// Purpose-built declarations that exercise every compatibility-significant shape the compiled
/// public API formatter must record.
/// </summary>
public static class FormatterFixtures
{
    /// <summary>
    /// Exercises delegate types.
    /// </summary>
    /// <param name="value">The callback value.</param>
    public delegate void Callback(int value);

    /// <summary>
    /// Exercises flags enums with a non-default underlying type.
    /// </summary>
    [Flags]
    public enum Level : byte
    {
        /// <summary>
        /// No level.
        /// </summary>
        None = 0,

        /// <summary>
        /// The low level.
        /// </summary>
        Low = 1,

        /// <summary>
        /// The high level.
        /// </summary>
        High = 2
    }

    /// <summary>
    /// Exercises generic interfaces with constraints.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    public interface IPair<TKey, TValue>
        where TKey : class
        where TValue : struct
    {
        /// <summary>
        /// Gets the key.
        /// </summary>
        TKey Key { get; }

        /// <summary>
        /// Gets the value.
        /// </summary>
        TValue Value { get; }
    }

    /// <summary>
    /// Exercises contravariant and covariant generic parameters.
    /// </summary>
    /// <typeparam name="TIn">The contravariant input type.</typeparam>
    /// <typeparam name="TOut">The covariant output type.</typeparam>
    public interface IVariant<in TIn, out TOut>
    {
        /// <summary>
        /// Converts an input to an output.
        /// </summary>
        /// <param name="value">The input.</param>
        /// <returns>The output.</returns>
        TOut Convert(TIn value);
    }

    /// <summary>
    /// Exercises record structs.
    /// </summary>
    /// <param name="Amount">The amount.</param>
    public record struct Money(decimal Amount);

    /// <summary>
    /// Exercises readonly structs.
    /// </summary>
    public readonly struct Point
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Point"/> struct.
        /// </summary>
        /// <param name="x">The x coordinate.</param>
        public Point(int x) => X = x;

        /// <summary>
        /// Gets the x coordinate.
        /// </summary>
        public int X { get; }
    }

    /// <summary>
    /// Exercises byref-like structs.
    /// </summary>
    public ref struct Span
    {
        /// <summary>
        /// Gets or sets the length.
        /// </summary>
        public int Length { get; set; }
    }

    /// <summary>
    /// Exercises record classes.
    /// </summary>
    /// <param name="Code">The code.</param>
    public record class Receipt(string Code);

    /// <summary>
    /// Exercises constructors, constants, accessor visibility, indexers, events, operators,
    /// parameter modifiers, default values, and generic constraints.
    /// </summary>
    public class Sample : IDisposable
    {
        private static string? _nullableValue;

        /// <summary>
        /// A public string constant.
        /// </summary>
        public const string DefaultName = "widget";

        /// <summary>
        /// A public decimal constant, which the compiler emits through an attribute rather than a constant blob.
        /// </summary>
        public const decimal Ratio = 1.5m;

#pragma warning disable SA1401 // Fields should be private: the gate must record protected fields.
        /// <summary>
        /// A protected read-only field.
        /// </summary>
        protected readonly char Marker;
#pragma warning restore SA1401

        /// <summary>
        /// Initializes a new instance of the <see cref="Sample"/> class.
        /// </summary>
        /// <param name="name">The sample name.</param>
        public Sample(string name)
        {
            Name = name;
            Marker = '#';
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Sample"/> class.
        /// </summary>
        protected Sample()
        {
            Name = DefaultName;
            Marker = '#';
        }

        /// <summary>
        /// An event with a nullable handler type.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Gets a non-nullable value with a protected setter.
        /// </summary>
        public string Name { get; protected set; }

        /// <summary>
        /// Gets a nullable, init-only value.
        /// </summary>
        public string? Tag { get; init; }

        private string Secret => Name;

        private protected string PrivateProtected => Name;

        /// <summary>
        /// Gets a value by index.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns>The value.</returns>
        public string this[int index] => Name;

        /// <summary>
        /// Converts a sample to its name.
        /// </summary>
        /// <param name="value">The sample.</param>
        public static implicit operator string(Sample value) => value.Name;

        /// <summary>
        /// Exercises an <c>out</c> parameter and a nullable input.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <param name="value">The parsed value.</param>
        /// <returns><see langword="true"/> when parsing succeeded.</returns>
        public static bool TryParse(string? text, out int value) => int.TryParse(text, out value);

        /// <summary>
        /// Exercises a <c>params</c> array parameter.
        /// </summary>
        /// <param name="values">The values to add.</param>
        /// <returns>The sum.</returns>
        public static int Sum(params int[] values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }

        /// <summary>
        /// Exercises an <c>in</c> parameter.
        /// </summary>
        /// <param name="factor">The scale factor.</param>
        public static void Scale(in double factor) => _ = factor;

        /// <summary>
        /// Exercises a nullable reference <c>out</c> parameter.
        /// </summary>
        /// <param name="value">The output value.</param>
        public static void CreateNullable(out string? value) => value = null;

        /// <summary>
        /// Exercises a non-nullable reference <c>ref</c> parameter.
        /// </summary>
        /// <param name="value">The value to update.</param>
        public static void Update(ref string value) => value = value.ToUpperInvariant();

        /// <summary>
        /// Exercises a nullable reference <c>in</c> parameter.
        /// </summary>
        /// <param name="value">The value to inspect.</param>
        public static void Inspect(in string? value) => _ = value;

        /// <summary>
        /// Exercises a nullable reference <c>ref readonly</c> return.
        /// </summary>
        /// <returns>The current nullable value.</returns>
        public static ref readonly string? PeekNullable() => ref _nullableValue;

        /// <summary>
        /// Exercises escaped, numeric, boolean, character, and enum default values.
        /// </summary>
        /// <param name="text">A string default containing characters that require escaping.</param>
        /// <param name="count">A numeric default.</param>
        /// <param name="flag">A boolean default.</param>
        /// <param name="separator">A character default.</param>
        /// <param name="level">An enum default.</param>
        /// <returns>The rendered description.</returns>
        public static string Describe(
            string? text = "a\\b\"c",
            int count = 7,
            bool flag = true,
            char separator = ';',
            Level level = Level.High)
        {
            return $"{text}{separator}{count}{flag}{level}";
        }

        /// <summary>
        /// Exercises a generic method with constraints.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The source sequence.</param>
        /// <returns>The first element, or a new instance.</returns>
        public static T Pick<T>(IEnumerable<T> source)
            where T : IComparable<T>, new()
        {
            foreach (T item in source)
            {
                return item;
            }

            return new T();
        }

        /// <inheritdoc />
        public void Dispose() => GC.SuppressFinalize(this);

        /// <summary>
        /// A protected virtual member that derived types may override.
        /// </summary>
        /// <returns>The rendered value.</returns>
        protected virtual string Render()
        {
            Changed?.Invoke(this, EventArgs.Empty);

            return Name + Secret + PrivateProtected + Marker;
        }
    }

    /// <summary>
    /// Exercises sealed overrides.
    /// </summary>
    public sealed class Derived : Sample
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Derived"/> class.
        /// </summary>
        /// <param name="name">The sample name.</param>
        public Derived(string name)
            : base(name)
        {
        }

        /// <inheritdoc />
        protected sealed override string Render() => Name.ToUpperInvariant();
    }

    internal class HiddenType
    {
        public int Visible { get; set; }
    }
}

/// <summary>
/// Exercises extension methods and attribute reporting from a top-level static class.
/// </summary>
public static class FormatterFixtureHelpers
{
    /// <summary>
    /// Doubles a value.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The doubled value.</returns>
    public static int Twice(this int value) => value * 2;

    /// <summary>
    /// An obsolete, hidden member.
    /// </summary>
    [Obsolete("gone", false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void Legacy()
    {
    }
}
