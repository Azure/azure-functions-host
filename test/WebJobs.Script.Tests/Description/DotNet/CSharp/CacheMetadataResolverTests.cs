// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.CodeAnalysis;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Description.DotNet.CSharp
{
    public class CacheMetadataResolverTests
    {
        [Fact]
        public async Task ResolveReference_IsThreadSafe()
        {
            var innerMock = new Mock<MetadataReferenceResolver>();
            innerMock
                .Setup(m => m.ResolveReference(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetadataReferenceProperties>()))
                .Returns(() => new[] { new TestRef() }.ToImmutableArray<PortableExecutableReference>());

            var resolver = new CacheMetadataResolver(innerMock.Object);

            // We're testing a race condition, so run the test several times
            // to ensure it does not happen.
            for (int i = 0; i < 10; i++)
            {
                await RunTest(resolver);
            }
        }

        private static async Task RunTest(CacheMetadataResolver resolver)
        {
            var tasks = new List<Task>();
            int max = 10000;
            for (int i = 0; i < max; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    resolver.ResolveReference($"ref{i}", $"path", MetadataReferenceProperties.Assembly);
                }));
            }

            await Task.WhenAll(tasks);
        }

        private class TestRef : PortableExecutableReference
        {
            public TestRef()
                : base(MetadataReferenceProperties.Assembly, "p")
            {
            }

            protected override DocumentationProvider CreateDocumentationProvider() => throw new NotImplementedException();

            protected override Metadata GetMetadataImpl() => throw new NotImplementedException();

            protected override PortableExecutableReference WithPropertiesImpl(MetadataReferenceProperties properties) => throw new NotImplementedException();
        }
    }
}
