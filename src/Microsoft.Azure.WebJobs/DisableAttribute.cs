// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Attribute that can be applied to job functions, trigger parameters and classes
    /// to conditionally disable triggered functions.
    /// <remarks>
    /// For example, by using this attribute, you can dynamically disable functions temporarily
    /// by changing application settings. Note that the disable check is done on startup only.
    /// If a <see cref="DisableAttribute"/> in the hierarchy (Parameter/Method/Class) exists and
    /// indicates that the function should be disabled, the listener for that function will not be
    /// started. The attribute only affects triggered functions.
    /// </remarks>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Parameter)]
    public sealed class DisableAttribute : Attribute
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public DisableAttribute()
        {
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="settingName">The name of an application setting or environment variable that
        /// governs whether the function(s) should be disabled. If the specified setting exists and its
        /// value is "1" or "True", the function will be disabled. The setting name can contain binding
        /// parameters (e.g. {MethodName}, {MethodShortName}, %test%, etc.).</param>
        public DisableAttribute(string settingName)
        {
            SettingName = settingName;
        }

        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="providerType">A Type which implements a method named "IsDisabled" taking
        /// a <see cref="System.Reflection.MethodInfo"/> and returning <see cref="bool"/>. This
        /// function will be called to determine whether the target function should be disabled.
        /// </param>
        public DisableAttribute(Type providerType)
        {
            ProviderType = providerType;
        }

        /// <summary>
        /// Gets the name of the application setting or environment variable that will
        /// be used to determine whether the function(s) should be disabled.
        /// </summary>
        public string SettingName { get; private set; }

        /// <summary>
        /// Gets the custom <see cref="Type"/> that will be invoked to determine
        /// whether the function(s) should be disabled.
        /// </summary>
        public Type ProviderType { get; private set; }
    }
}
