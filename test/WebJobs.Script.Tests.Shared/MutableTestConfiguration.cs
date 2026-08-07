// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script.Tests;

/// <summary>
/// Provides mutable in-memory configuration with explicit reload behavior for tests.
/// </summary>
public sealed class MutableTestConfiguration
{
    private readonly IDictionary<string, string> _values;

    /// <summary>
    /// Initializes a new instance of the <see cref="MutableTestConfiguration"/> class.
    /// </summary>
    /// <param name="values">The initial configuration values.</param>
    /// <param name="configureLowerProviders">An optional callback that adds lower-precedence providers.</param>
    public MutableTestConfiguration(
        IDictionary<string, string> values = null,
        Action<IConfigurationBuilder> configureLowerProviders = null)
    {
        _values = values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        var builder = new ConfigurationBuilder();
        configureLowerProviders?.Invoke(builder);
        builder.Add(new MutableConfigurationSource(_values));
        Configuration = builder.Build();
    }

    /// <summary>
    /// Gets the configuration root observed by production code.
    /// </summary>
    public IConfigurationRoot Configuration { get; }

    /// <summary>
    /// Adds a provider backed by the same mutable source to another configuration builder.
    /// </summary>
    public void AddTo(IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(new MutableConfigurationSource(_values));
    }

    /// <summary>
    /// Changes a source value without implicitly reloading the configuration root.
    /// </summary>
    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _values[key] = value;
    }

    /// <summary>
    /// Removes a source value without implicitly reloading the configuration root.
    /// </summary>
    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _values.Remove(key);
    }

    /// <summary>
    /// Reloads the configuration root from the current source values.
    /// </summary>
    public void Reload()
    {
        Configuration.Reload();
    }

    private sealed class MutableConfigurationSource : IConfigurationSource
    {
        private readonly IDictionary<string, string> _values;

        public MutableConfigurationSource(IDictionary<string, string> values)
        {
            _values = values;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new MutableConfigurationProvider(_values);
        }
    }

    private sealed class MutableConfigurationProvider : ConfigurationProvider
    {
        private readonly IDictionary<string, string> _values;

        public MutableConfigurationProvider(IDictionary<string, string> values)
        {
            _values = values;
        }

        public override void Load()
        {
            Data = new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        }
    }
}
