// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.AppCapabilities;

public class AppCapabilitiesOptionsTests
{
    [Fact]
    public void Indexer_UpdateExistingKey_DoesNotCountAgainstMaxCapabilities()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        for (int i = 0; i < AppCapabilitiesOptions.MaxCapabilities; i++)
        {
            dict[$"key{i}"] = $"value{i}";
        }

        dict["key0"] = "updatedValue";

        Assert.Equal("updatedValue", dict["key0"]);
    }

    [Fact]
    public void Keys_IsCaseInsensitive()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        dict["MyKey"] = "value1";

        Assert.True(dict.ContainsKey("mykey"));
        Assert.True(dict.ContainsKey("MYKEY"));
        Assert.True(dict.ContainsKey("MyKey"));
        Assert.Equal("value1", dict["mykey"]);
    }

    [Fact]
    public void Add_EmptyKey_ThrowsArgumentException()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        var exception = Assert.Throws<ArgumentException>(() => dict.Add(string.Empty, "value"));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains("cannot be empty", exception.Message);
    }

    [Fact]
    public void Add_KeyExceedsMaxLength_ThrowsArgumentException()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;
        var longKey = new string('a', AppCapabilitiesOptions.MaxKeyLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => dict.Add(longKey, "value"));

        Assert.Equal("key", exception.ParamName);
        Assert.Contains($"cannot exceed {AppCapabilitiesOptions.MaxKeyLength}", exception.Message);
    }

    [Fact]
    public void Add_KeyAtMaxLength_Succeeds()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;
        var maxLengthKey = new string('a', AppCapabilitiesOptions.MaxKeyLength);

        dict.Add(maxLengthKey, "value");

        Assert.Single(dict);
        Assert.Equal("value", dict[maxLengthKey]);
    }

    [Fact]
    public void Add_NullValue_ThrowsArgumentNullException()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        var exception = Assert.Throws<ArgumentNullException>(() => dict.Add("key", null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Add_ValueExceedsMaxLength_ThrowsArgumentException()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;
        var longValue = new string('a', AppCapabilitiesOptions.MaxValueLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => dict.Add("key", longValue));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains($"cannot exceed {AppCapabilitiesOptions.MaxValueLength}", exception.Message);
    }

    [Fact]
    public void Add_ValueAtMaxLength_Succeeds()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;
        var maxLengthValue = new string('a', AppCapabilitiesOptions.MaxValueLength);

        dict.Add("key", maxLengthValue);

        Assert.Single(dict);
        Assert.Equal(maxLengthValue, dict["key"]);
    }

    [Fact]
    public void Add_EmptyValue_Succeeds()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        dict.Add("key", string.Empty);

        Assert.Single(dict);
        Assert.Equal(string.Empty, dict["key"]);
    }

    [Fact]
    public void Add_ExceedsMaxCapabilities_ThrowsInvalidOperationException()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        for (int i = 0; i < AppCapabilitiesOptions.MaxCapabilities; i++)
        {
            dict.Add($"key{i}", $"value{i}");
        }

        var exception = Assert.Throws<InvalidOperationException>(() => dict.Add("extraKey", "extraValue"));

        Assert.Contains($"Cannot add more than {AppCapabilitiesOptions.MaxCapabilities}", exception.Message);
    }

    [Fact]
    public void Indexer_ExceedsMaxCapabilities_ThrowsInvalidOperationException()
    {
        var options = new AppCapabilitiesOptions();
        IDictionary<string, string> dict = options;

        for (int i = 0; i < AppCapabilitiesOptions.MaxCapabilities; i++)
        {
            dict[$"key{i}"] = $"value{i}";
        }

        var exception = Assert.Throws<InvalidOperationException>(() => dict["extraKey"] = "extraValue");

        Assert.Contains($"Cannot add more than {AppCapabilitiesOptions.MaxCapabilities}", exception.Message);
    }

    [Fact]
    public void AddKeyValuePair_ExceedsMaxCapabilities_ThrowsInvalidOperationException()
    {
        var options = new AppCapabilitiesOptions();
        ICollection<KeyValuePair<string, string>> collection = options;

        for (int i = 0; i < AppCapabilitiesOptions.MaxCapabilities; i++)
        {
            collection.Add(new KeyValuePair<string, string>($"key{i}", $"value{i}"));
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            collection.Add(new KeyValuePair<string, string>("extraKey", "extraValue")));

        Assert.Contains($"Cannot add more than {AppCapabilitiesOptions.MaxCapabilities}", exception.Message);
    }
}