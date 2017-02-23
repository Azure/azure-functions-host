// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.TestCommon;
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

            [AutoResolve(AllowTokens = false)]
            public string ResolvedSetting { get; set; }
        }

        public class AttributeWithResolutionPolicy : Attribute
        {
            [AutoResolve(ResolutionPolicyType = typeof(TestResolutionPolicy))]
            public string PropWithPolicy { get; set; }

            [AutoResolve]
            public string PropWithoutPolicy { get; set; }

            [AutoResolve(ResolutionPolicyType = typeof(WebJobs.ODataFilterResolutionPolicy))]
            public string PropWithMarkerPolicy { get; set; }

            [AutoResolve(ResolutionPolicyType = typeof(AutoResolveAttribute))]
            public string PropWithInvalidPolicy { get; set; }

            [AutoResolve(ResolutionPolicyType = typeof(NoDefaultConstructorResolutionPolicy))]
            public string PropWithConstructorlessPolicy { get; set; }

            internal string ResolutionData { get; set; }
        }

        public class TestResolutionPolicy : IResolutionPolicy
        {
            public string TemplateBind(PropertyInfo propInfo, Attribute attribute, BindingTemplate template, IReadOnlyDictionary<string, object> bindingData)
            {
                // set some internal state for the binding rules to use later
                ((AttributeWithResolutionPolicy)attribute).ResolutionData = "value1";

                return template.Bind(bindingData);
            }
        }

        public class NoDefaultConstructorResolutionPolicy : IResolutionPolicy
        {
            public NoDefaultConstructorResolutionPolicy(string someValue)
            {
            }

            public string TemplateBind(PropertyInfo propInfo, Attribute attribute, BindingTemplate template, IReadOnlyDictionary<string, object> bindingData)
            {
                throw new NotImplementedException();
            }
        }

        // Helper to easily generate a fixed binding contract.
        private static IReadOnlyDictionary<string, Type> GetBindingContract(params string[] names)
        {
            var d = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                d[name] = typeof(string);
            }
            return d;
        }

        private static IReadOnlyDictionary<string, Type> EmptyContract = new Dictionary<string, Type>();

        // Enforce binding contracts statically.
        [Fact]
        public void BindingContractMismatch()
        {
            Attr1 a1 = new Attr1 { Path = "{name}" };

            try
            {
                var cloner = new AttributeCloner<Attr1>(a1, EmptyContract);
                Assert.True(false, "Should have caught binding contract mismatch");
            }
            catch (InvalidOperationException e)
            {
                Assert.Equal("No binding parameter exists for 'name'.", e.Message);
            }
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

            var cloner = new AttributeCloner<Attr1>(a1, EmptyContract, nameResolver);
            Attr1 attr2 = await cloner.ResolveFromInvokeStringAsync("xy");

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
                var cloner = new AttributeCloner<BlobAttribute>(attr, GetBindingContract("name"));
                BlobAttribute attr2 = await cloner.ResolveFromInvokeStringAsync("c/n");

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

            var cloner = new AttributeCloner<Attr2>(attr, GetBindingContract("p1", "p2"));

            Attr2 attrResolved = cloner.ResolveFromBindings(new Dictionary<string, object> { { "p1", "v1" }, { "p2", "v2" } });

            Assert.Equal("v1", attrResolved.ResolvedProp1);
            Assert.Equal("v2", attrResolved.ResolvedProp2);
            Assert.Equal(attr.ConstantProp, attrResolved.ConstantProp);

            var invokeString = cloner.GetInvokeString(attrResolved);
            var attr2 = await cloner.ResolveFromInvokeStringAsync(invokeString);

            Assert.Equal(attrResolved.ResolvedProp1, attr2.ResolvedProp1);
            Assert.Equal(attrResolved.ResolvedProp2, attr2.ResolvedProp2);
            Assert.Equal(attrResolved.ConstantProp, attr2.ConstantProp);
        }

        // Easy case - default ctor and all settable properties.
        [Fact]
        public async Task NameResolver()
        {
            Attr1 a1 = new Attr1 { Path = "x%appsetting%y-{k}" };

            var nameResolver = new FakeNameResolver().Add("appsetting", "ABC");
            var cloner = new AttributeCloner<Attr1>(a1, GetBindingContract("k"), nameResolver);

            // Get the attribute with %% resolved (happens at indexing time), but not {} (not resolved until runtime)
            var attrPre = cloner.GetNameResolvedAttribute();
            Assert.Equal("xABCy-{k}", attrPre.Path);

            Dictionary<string, object> values = new Dictionary<string, object>()
            {
                { "k", "v" }
            };
            var ctx = GetCtx(values);
            var attr2 = await cloner.ResolveFromBindingDataAsync(ctx);

            Assert.Equal("xABCy-v", attr2.Path);
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

            var cloner = new AttributeCloner<Attr1>(a1, GetBindingContract("key1", "key2"));
            var attr2 = await cloner.ResolveFromBindingDataAsync(ctx);

            Assert.Equal("val1-val2", attr2.Path);
        }

        [Fact]
        public void Setting()
        {
            Attr2 a2 = new Attr2(string.Empty, string.Empty) { ResolvedSetting = "appsetting" };

            var nameResolver = new FakeNameResolver().Add("appsetting", "ABC");
            var cloner = new AttributeCloner<Attr2>(a2, EmptyContract, nameResolver);

            var a2Cloned = cloner.GetNameResolvedAttribute();
            Assert.Equal("ABC", a2Cloned.ResolvedSetting);
        }

        [Fact]
        public void Setting_WithNoValueInResolver_Throws()
        {
            Attr2 a2 = new Attr2(string.Empty, string.Empty) { ResolvedSetting = "appsetting" };
            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr2>(a2, EmptyContract, null));
        }

        [Fact]
        public async Task Setting_Null()
        {
            Attr2 a2 = new Attr2(string.Empty, string.Empty);

            var cloner = new AttributeCloner<Attr2>(a2, EmptyContract, null);

            Attr2 a2Clone = await cloner.ResolveFromBindingDataAsync(GetCtx(null));

            Assert.Null(a2Clone.ResolvedSetting);
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

            var cloner = new AttributeCloner<BlobAttribute>(a1, GetBindingContract("name"));
            var attr2 = await cloner.ResolveFromBindingDataAsync(ctx);

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

            var cloner = new AttributeCloner<BlobAttribute>(a1, GetBindingContract("name"));
            var attr2 = await cloner.ResolveFromBindingDataAsync(ctx);

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

            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr1>(attr, EmptyContract));
        }

        [Fact]
        public void Fail_MalformedPath_CtorArg()
        {
            var attr = new Attr2("%bad", "constant");

            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr2>(attr, EmptyContract));
        }

        // Malformed %% fail in ctor.
        // It's important that the failure comes from the attr cloner ctor because that means it
        // will occur during indexing time (ie, sooner than runtime).
        [Fact]
        public void Fail_MissingPath()
        {
            Attr1 attr = new Attr1 { Path = "%missing%" };

            Assert.Throws<InvalidOperationException>(() => new AttributeCloner<Attr1>(attr, EmptyContract));
        }

        [Fact]
        public void TryAutoResolveValue_UnresolvedValue_ThrowsExpectedException()
        {
            var resolver = new FakeNameResolver();
            var attribute = new Attr2(string.Empty, string.Empty)
            {
                ResolvedSetting = "MySetting"
            };
            var prop = attribute.GetType().GetProperty("ResolvedSetting");
            string resolvedValue = null;

            var ex = Assert.Throws<InvalidOperationException>(() => AttributeCloner<Attr2>.TryAutoResolveValue(attribute, prop, resolver, out resolvedValue));
            Assert.Equal("Unable to resolve value for property 'Attr2.ResolvedSetting'.", ex.Message);
        }

        [Fact]
        public void GetPolicy_ReturnsDefault_WhenNoSpecifiedPolicy()
        {
            PropertyInfo propInfo = typeof(AttributeWithResolutionPolicy).GetProperty(nameof(AttributeWithResolutionPolicy.PropWithoutPolicy));

            IResolutionPolicy policy = AttributeCloner<AttributeWithResolutionPolicy>.GetPolicy(propInfo);

            Assert.IsType<DefaultResolutionPolicy>(policy);
        }

        [Fact]
        public void GetPolicy_Returns_SpecifiedPolicy()
        {
            PropertyInfo propInfo = typeof(AttributeWithResolutionPolicy).GetProperty(nameof(AttributeWithResolutionPolicy.PropWithPolicy));

            IResolutionPolicy policy = AttributeCloner<AttributeWithResolutionPolicy>.GetPolicy(propInfo);

            Assert.IsType<TestResolutionPolicy>(policy);
        }

        [Fact]
        public void GetPolicy_ReturnsODataFilterPolicy_ForMarkerType()
        {
            // This is a special-case marker type to handle TableAttribute.Filter. We cannot directly list ODataFilterResolutionPolicy
            // because BindingTemplate doesn't exist in the core assembly.
            PropertyInfo propInfo = typeof(AttributeWithResolutionPolicy).GetProperty(nameof(AttributeWithResolutionPolicy.PropWithMarkerPolicy));

            IResolutionPolicy policy = AttributeCloner<AttributeWithResolutionPolicy>.GetPolicy(propInfo);

            Assert.IsType<Host.Bindings.ODataFilterResolutionPolicy>(policy);
        }

        [Fact]
        public void GetPolicy_Throws_IfPolicyDoesNotImplementInterface()
        {
            PropertyInfo propInfo = typeof(AttributeWithResolutionPolicy).GetProperty(nameof(AttributeWithResolutionPolicy.PropWithInvalidPolicy));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => AttributeCloner<AttributeWithResolutionPolicy>.GetPolicy(propInfo));

            Assert.Equal($"The {nameof(AutoResolveAttribute.ResolutionPolicyType)} on {nameof(AttributeWithResolutionPolicy.PropWithInvalidPolicy)} must derive from {typeof(IResolutionPolicy).Name}.", ex.Message);
        }

        [Fact]
        public void GetPolicy_Throws_IfPolicyHasNoDefaultConstructor()
        {
            PropertyInfo propInfo = typeof(AttributeWithResolutionPolicy).GetProperty(nameof(AttributeWithResolutionPolicy.PropWithConstructorlessPolicy));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => AttributeCloner<AttributeWithResolutionPolicy>.GetPolicy(propInfo));

            Assert.Equal($"The {nameof(AutoResolveAttribute.ResolutionPolicyType)} on {nameof(AttributeWithResolutionPolicy.PropWithConstructorlessPolicy)} must derive from {typeof(IResolutionPolicy).Name} and have a default constructor.", ex.Message);
        }

        private static BindingContext GetCtx(IReadOnlyDictionary<string, object> values)
        {
            BindingContext ctx = new BindingContext(
                new ValueBindingContext(null, CancellationToken.None),
                values);
            return ctx;
        }
    }
}