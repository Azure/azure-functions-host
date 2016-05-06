// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class AttributeClonerTests
    {
        // Basic attribute. 1 property. No ctor. 
        public class Attr1 : Attribute
        {
            [AutoResolve]
            public string Path { get; set; }
        }

        // 2 attributes, set in ctor. And a non-resolved attribute. 
        public class Attr2 : Attribute
        {
            public Attr2(string resolvedProp2, string constantProp)
            {
                this.ResolvedProp2 = resolvedProp2;
                this.ConstantProp = constantProp;
            }

            [AutoResolve]
            public string ResolvedProp1 { get; set; }

            [AutoResolve]
            public string ResolvedProp2 { get; private set; }
            public string ConstantProp { get; private set; }
        }


        // Test on an attribute that does NOT implement IAttributeInvokeDescriptor
        // Key parameter is a property (not ctor)
        [Fact]
        public async Task InvokeString()
        {
            Attr1 a1 = new Attr1 { Path = "%test%" };

            Assert.Null(a1 as IAttributeInvokeDescriptor<Attr1>); // Does not implement the interface

            var nameResolver = new FakeNameResolver();
            nameResolver._dict["test"] = "ABC";

            var cloner = new AttributeCloner<Attr1>(a1, nameResolver);
            Attr1 attr2 = await cloner.ResolveFromInvokeString("xy");

            Assert.Equal("xy", attr2.Path);
        }

        // Test on an attribute that does NOT implement IAttributeInvokeDescriptor
        // Key parameter is on the ctor
        [Fact]
        public async Task InvokeStringBlobAttribute()
        {
            foreach (var attr in new BlobAttribute[] {
                new BlobAttribute("container/{name}"),
                new BlobAttribute("container/constant", FileAccess.ReadWrite),
                new BlobAttribute("container/{name}", FileAccess.Write)
            })
            {
                var cloner = new AttributeCloner<BlobAttribute>(attr);
                BlobAttribute attr2 = await cloner.ResolveFromInvokeString("c/n");

                Assert.Equal("c/n", attr2.BlobPath);
                Assert.Equal(attr.Access, attr2.Access);
            }
        }     

        // Test on an attribute that does NOT implement IAttributeInvokeDescriptor
        // Multiple resolved properties.
        [Fact]
        public async Task InvokeStringMultipleResolvedProperties()
        {
            Attr2 attr = new Attr2("{p2}", "constant") { ResolvedProp1 = "{p1}" };

            var cloner = new AttributeCloner<Attr2>(attr);

            Attr2 attrResolved = cloner.ResolveFromBindings(new Dictionary<string, object> {
                { "p1", "v1" }, { "p2", "v2" }});

            Assert.Equal("v1", attrResolved.ResolvedProp1);
            Assert.Equal("v2", attrResolved.ResolvedProp2);
            Assert.Equal(attr.ConstantProp, attrResolved.ConstantProp);

            var invokeString = cloner.GetInvokeString(attrResolved);
            var attr2 = await cloner.ResolveFromInvokeString(invokeString);

            Assert.Equal(attrResolved.ResolvedProp1, attr2.ResolvedProp1);
            Assert.Equal(attrResolved.ResolvedProp2, attr2.ResolvedProp2);
            Assert.Equal(attrResolved.ConstantProp, attr2.ConstantProp);
        }

        // Easy case - default ctor and all settable properties. 
        [Fact]
        public async Task NameResolver()
        {
            Attr1 a1 = new Attr1 { Path = "x%appsetting%y" };

            var nameResolver = new FakeNameResolver();
            nameResolver._dict["appsetting"] = "ABC";

            Dictionary<string, object> values = new Dictionary<string, object>();
            var ctx = GetCtx(values);

            var cloner = new AttributeCloner<Attr1>(a1, nameResolver);
            var attr2 = await cloner.ResolveFromBindingData(ctx);

            Assert.Equal("xABCy", attr2.Path);
        }

        // Easy case - default ctor and all settable properties. 
        [Fact]
        public async Task Easy()
        {
            Attr1 a1 = new Attr1 { Path = "{key1}-{key2}" };

            Dictionary<string, object> values = new Dictionary<string, object>()
            {
                { "key1", "val1" },
                { "key2", "val2" }
            };
            var ctx = GetCtx(values);

            var cloner = new AttributeCloner<Attr1>(a1);
            var attr2 = await cloner.ResolveFromBindingData(ctx);

            Assert.Equal("val1-val2", attr2.Path);
        }

        [Fact]
        public async Task CloneNoDefaultCtor()
        {
            var a1 = new BlobAttribute("container/{name}.txt", FileAccess.Write);

            Dictionary<string, object> values = new Dictionary<string, object>()
            {
                { "name", "green" },
            };
            var ctx = GetCtx(values);

            var cloner = new AttributeCloner<BlobAttribute>(a1);
            var attr2 = await cloner.ResolveFromBindingData(ctx);

            Assert.Equal("container/green.txt", attr2.BlobPath);
            Assert.Equal(a1.Access, attr2.Access);
        }

        [Fact]
        public async Task CloneNoDefaultCtorShortList()
        {
            // Use shorter parameter list. 
            var a1 = new BlobAttribute("container/{name}.txt");

            Dictionary<string, object> values = new Dictionary<string, object>()
            {
                { "name", "green" },
            };
            var ctx = GetCtx(values);

            var cloner = new AttributeCloner<BlobAttribute>(a1);
            var attr2 = await cloner.ResolveFromBindingData(ctx);

            Assert.Equal("container/green.txt", attr2.BlobPath);
            Assert.Equal(a1.Access, attr2.Access);
        }


        // Malformed %% fail in ctor.  
        // It's important that the failure comes from the attr cloner ctor because that means it       
        // will occur during indexing time (ie, sooner than runtime). 
        [Fact]
        public void Fail_MalformedPath_MutableProperty()
        {
            Attr1 attr = new Attr1 { Path = "%bad" };

            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr1>(attr));            
        }

        [Fact]
        public void Fail_MalformedPath_CtorArg()
        {
            var attr = new Attr2("%bad", "constant");

            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr2>(attr));
        }

        // Malformed %% fail in ctor.  
        // It's important that the failure comes from the attr cloner ctor because that means it       
        // will occur during indexing time (ie, sooner than runtime). 
        [Fact]
        public void Fail_MissingPath()
        {
            Attr1 attr = new Attr1 { Path = "%missing%" };

            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr1>(attr));

        }

        static BindingContext GetCtx(IReadOnlyDictionary<string,object> values)
        {
            BindingContext ctx= new BindingContext(
                new ValueBindingContext(null, CancellationToken.None),
                values);
            return ctx;
        }
    }
}