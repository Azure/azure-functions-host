// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WindowsAzure.Storage.Blob;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Binding
{
    public class BlobBindingTests
    {
        [Theory]
        [InlineData(typeof(ICloudBlob), FileAccess.Read, FileAccess.ReadWrite)]
        [InlineData(typeof(CloudBlockBlob), FileAccess.Read, FileAccess.ReadWrite)]
        [InlineData(typeof(CloudPageBlob), FileAccess.Read, FileAccess.ReadWrite)]
        [InlineData(typeof(CloudBlobDirectory), FileAccess.Read, FileAccess.ReadWrite)]
        [InlineData(typeof(ICloudBlob), FileAccess.Write, FileAccess.ReadWrite)]
        [InlineData(typeof(CloudBlockBlob), FileAccess.Write, FileAccess.ReadWrite)]
        [InlineData(typeof(CloudPageBlob), FileAccess.Write, FileAccess.ReadWrite)]
        [InlineData(typeof(CloudBlobDirectory), FileAccess.Write, FileAccess.ReadWrite)]
        [InlineData(typeof(Stream), FileAccess.Write, FileAccess.Write)]
        [InlineData(typeof(Stream), FileAccess.Read, FileAccess.Read)]
        [InlineData(typeof(TextReader), FileAccess.Read, FileAccess.Read)]
        [InlineData(typeof(TextWriter), FileAccess.Write, FileAccess.Write)]
        [InlineData(typeof(string), FileAccess.Read, FileAccess.Read)]
        public void GetCustomAttributes_WithReadWriteTypes_AppliesReadWriteAccess(Type parameterType, FileAccess metadataAccess, FileAccess expectedAccess)
        {
            var metadata = new BlobBindingMetadata()
            {
                Path = "my/blob"
            };
            var binding = new BlobBinding(new ScriptHostConfiguration(), metadata, metadataAccess);

            Collection<CustomAttributeBuilder> attributeBuilders = binding.GetCustomAttributes(parameterType);

            // Get blob attribute
            var builder = attributeBuilders.FirstOrDefault(b =>
            string.CompareOrdinal(GetCustomAttributeBuilderFieldValue<ConstructorInfo>("m_con", b).DeclaringType.FullName,
            "Microsoft.Azure.WebJobs.BlobAttribute") == 0);

            Assert.NotNull(builder);

            var attributeParameters = GetCustomAttributeBuilderFieldValue<object[]>("m_constructorArgs", builder);

            Assert.Equal(2, attributeParameters.Length);
            Assert.IsType(typeof(FileAccess), attributeParameters[1]);

            FileAccess access = (FileAccess)attributeParameters[1];

            Assert.Equal(expectedAccess, access);
        }

        private T GetCustomAttributeBuilderFieldValue<T>(string fieldName, object instance)
        {
            object result = typeof(CustomAttributeBuilder)
                .GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(instance);

            return result != null ? (T)result : default(T);
        }
    }
}
