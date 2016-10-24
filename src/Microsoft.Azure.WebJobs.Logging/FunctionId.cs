// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    /// <summary>
    /// Strongly typing for a function Id. 
    /// FunctionIds include HostName and FunctionName
    /// </summary>
    public struct FunctionId
    {
        internal string Value { get; set; }

        /// <summary>
        /// Get string representation. This will be consist of valid Azure Table row key characters. 
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return this.Value;
        }

        /// <summary>
        /// Throw exception if this instance is bad. 
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrEmpty(this.Value))
            {
                throw new InvalidOperationException("FunctionId is not valid.");
            }
        }

        /// <summary>
        /// Build directly from a string. This skips escaping and should only be used by the reader for interop. 
        /// This is the direct inverse of ToString().
        /// </summary>
        /// <param name="value">raw value from a previous ToString() call. </param>
        /// <returns></returns>
        public static FunctionId Parse(string value)
        {
            return new FunctionId { Value = value };
        }

        /// <summary>
        /// Build a FunctionId from the host and function name. 
        /// </summary>
        /// <param name="hostName"></param>
        /// <param name="functionName"></param>
        /// <returns></returns>
        public static FunctionId Build(string hostName, string functionName)
        {
            var value = string.Concat(
                TableScheme.NormalizeFunctionName(hostName),
                "-",
                TableScheme.NormalizeFunctionName(functionName));
            return Parse(value);
        }

        /// <summary>
        /// Get a hashcode. 
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }

        /// <summary>
        /// Equality. This is comparing normalized names. 
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (obj is FunctionId)
            {
                var other = (FunctionId)obj;
                return other.Value == this.Value;
            }
            return false;
        }

        /// <summary>
        /// Equality 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(FunctionId left, FunctionId right)
        {
            return left.Value == right.Value;
        }

        /// <summary>
        /// Inquality 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(FunctionId left, FunctionId right)
        {
            return !(left == right);
        }
    }
}