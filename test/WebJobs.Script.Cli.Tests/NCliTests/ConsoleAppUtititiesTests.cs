using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NCli;
using Xunit;

namespace WebJobs.Script.Cli.Tests.NCliTests
{
    public class ConsoleAppUtititiesTests
    {
        public enum TestEnum
        {
            North,
            Houses
        }

        public class TestClass
        {
            public int Number { get; set; }
            public TestEnum Enum { get; set; }
            public string String { get; set; }
            public DateTime Time { get; set; }
            public long LongNumber { get; set; }
            public bool Bool { get; set; }
            public IEnumerable<string> IEnumerableOfStrings { get; set; }
            public string[] ArrayOfStrings { get; set; }
            public List<TestEnum> ListOfEnums { get; set; }
            public ICollection<string> CollectionOfStrings { get; set; }
        }

        [Verb]
        public class TestClass2 { }

        [Verb(HelpText = "help", Scope = 10, ShowInHelp = false, Usage = "usage")]
        public class TestClass3Verb { }

        [Verb("name", HelpText = "help", Usage = "usage")]
        public class TestClass4Verb { }

        [Verb("help", HelpText = "help", Usage = "usage")]
        public class TestClass5 { }

        public enum Scopes
        {
            Scope1,
            Scope2,
            Scope3
        }

        [Verb("scoped", Scope = Scopes.Scope1)]
        public class ScopedClass1 { }

        [Verb("scoped", Scope = Scopes.Scope2)]
        public class ScopedClass2 { }

        [Verb("scoped", Scope = Scopes.Scope3)]
        public class ScopedClass3 { }

        [Verb("scoped")]
        public class ScopedClass4 { }

        [Theory]
        [InlineData(typeof(TestEnum), "value", false)]
        [InlineData(typeof(TestEnum), "north", true)]
        [InlineData(typeof(TestEnum), "North", true)]
        [InlineData(typeof(TestEnum), "House", false)]
        [InlineData(typeof(TestEnum), "Houses", true)]
        [InlineData(typeof(ConsoleColor), "blue", true)]
        [InlineData(typeof(string), "", false)]
        [InlineData(null, null, false)]
        public void TryParseEnumTest(Type type, string value, bool expected)
        {
            // Test
            object obj = null;
            var actual = ConsoleAppUtilities.TryParseEnum(type, value, out obj);

            // Assert
            actual.Should().Be(expected);
        }

        [Theory]
        [InlineData(typeof(TestClass), nameof(TestClass), null, true, null, null)]
        [InlineData(typeof(TestClass2), nameof(TestClass2), null, true, null, null)]
        [InlineData(typeof(TestClass3Verb), "TestClass3", "help", false, "usage", 10)]
        [InlineData(typeof(TestClass4Verb), "name", "help", true, "usage", null)]
        public void TypeToAttributeTest(Type type, string verbName, string help, bool showInHelp, string usage, object scope)
        {
            // Test
            var result = ConsoleAppUtilities.TypeToAttribute(type);

            // Assert
            result.Names.Should().Contain(verbName.ToLowerInvariant(), because: "TypeToAttribute should return a correct VerbAttribute");
            result.HelpText.Should().Be(help);
            result.ShowInHelp.Should().Be(showInHelp);
            result.Usage.Should().Be(usage);
        }

        public static IEnumerable<object[]> TryCastTestData
        {
            get
            {
                yield return new object[] { "string", typeof(string), true, "string" };
                yield return new object[] { string.Empty, typeof(string), true, string.Empty };
                yield return new object[] { "15", typeof(int), true, 15 };
                yield return new object[] { "2016/8/22", typeof(DateTime), true, new DateTime(2016, 8, 22) };
                yield return new object[] { "15", typeof(long), true, 15L };
                yield return new object[] { "north", typeof(TestEnum), true, TestEnum.North};

                yield return new object[] { "test", typeof(int), false, null };
                yield return new object[] { "test", typeof(Dictionary<string, string>), false, null};
                yield return new object[] { "test", typeof(TestEnum), false, null };
                yield return new object[] { null, null, false, null };
            }
        }

        [Theory]
        [MemberData(nameof(TryCastTestData))]
        public void TryCastTest(string arg, Type type, bool expectedResult, object expectedObject)
        {
            // Test
            object actualObject = null;
            var actualResult = ConsoleAppUtilities.TryCast(arg, type, out actualObject);

            // Assert
            actualResult.Should().Be(expectedResult);
            actualObject.Should().Be(expectedObject);
        }

        public static IEnumerable<object[]> TryParseOptionTestData
        {
            get
            {
                var type = typeof(TestClass);
                yield return new object[] { type.GetProperty(nameof(TestClass.Number)), "15".Split(' '), true, 15, 0 };
                yield return new object[] { type.GetProperty(nameof(TestClass.Enum)), "north".Split(' '), true, TestEnum.North, 0 };
                yield return new object[] { type.GetProperty(nameof(TestClass.String)), "string".Split(' '), true, "string", 0 };
                yield return new object[] { type.GetProperty(nameof(TestClass.Time)), "2016/08/22".Split(' '), true, new DateTime(2016, 8, 22), 0};
                yield return new object[] { type.GetProperty(nameof(TestClass.LongNumber)), "123165465".Split(' '), true, 123165465L, 0 };
                yield return new object[] { type.GetProperty(nameof(TestClass.Bool)), "test".Split(' '), true, true, 1 };
                yield return new object[] { type.GetProperty(nameof(TestClass.IEnumerableOfStrings)), "string1 string2 string3".Split(' '), true, new[] { "string1", "string2", "string3" }, 0 };
                yield return new object[] { type.GetProperty(nameof(TestClass.IEnumerableOfStrings)), "string1 string2 --another string3".Split(' '), true, new[] { "string1", "string2" }, 2 };
                yield return new object[] { type.GetProperty(nameof(TestClass.ListOfEnums)), "north north houses".Split(' '), true, new[] { TestEnum.North, TestEnum.North, TestEnum.Houses }, 0 };
                yield return new object[] { type.GetProperty(nameof(TestClass.ListOfEnums)), "north other".Split(' '), true, new[] { TestEnum.North, TestEnum.North, TestEnum.Houses }, 1 };


                yield return new object[] { type.GetProperty(nameof(TestClass.ArrayOfStrings)), "string1 string2 string3".Split(' '), false, null, 3 };
                yield return new object[] { null, null, false, null, -1};
            }
        }

        [Theory]
        [MemberData(nameof(TryParseOptionTestData))]
        public void TryParseOptionTest(PropertyInfo option, string[] args, bool expectedResult, object expectedObject, int remainingArgs)
        {
            // Test
            var stack = args != null ? new Stack<string>(args.Reverse()) : null;
            object actualObject = null;
            var actualResult = ConsoleAppUtilities.TryParseOption(option, stack, out actualObject);

            // Assert
            actualResult.Should().Be(expectedResult);
            actualObject.Should().Equals(expectedObject);

            if (remainingArgs != -1)
            {
                stack.Count.Should().Be(remainingArgs);
            }
        }

        public static IEnumerable<object[]> GeneralHelpTestData
        {
            get
            {
                yield return new object[] { new [] { typeof(TestClass) }, 3 };
                yield return new object[] { new [] { typeof(TestClass), typeof(TestClass) }, 3 };
                yield return new object[] { new [] { typeof(TestClass), typeof(TestClass2) }, 4 };
                yield return new object[] { new [] { typeof(TestClass), typeof(TestClass2), typeof(TestClass4Verb) }, 5 };
                yield return new object[] { new [] { typeof(TestClass), typeof(TestClass2), typeof(TestClass3Verb), typeof(TestClass4Verb) }, 5 };

            }
        }

        [Theory]
        [MemberData(nameof(GeneralHelpTestData))]
        public void GeneralHelpTest(IEnumerable<Type> types, int expectedLineCount)
        {
            // Setup
            const string cliName = "testCli";
            var verbTypes = types.Select(ConsoleAppUtilities.TypeToVerbType);

            // Test
            var result = ConsoleAppUtilities.GeneralHelp(verbTypes, cliName);

            // Assert
            result.Should().Contain(l => l.ToString().Contains(cliName));

            result.Count().Should().Be(expectedLineCount);

            foreach (var verbType in verbTypes.Where(t => t.Metadata.ShowInHelp))
            {
                result.Should().Contain(l => l.ToString().Contains(verbType.Metadata.Names.First()));
            }

            foreach (var verbType in verbTypes.Where(t => !t.Metadata.ShowInHelp))
            {
                result.Should().NotContain(l => l.ToString().Contains(verbType.Metadata.Names.First()));
            }
        }

        public static IEnumerable<object[]> GetVerbTypeTestData
        {
            get
            {
                var allVerbs = new[] 
                {
                    typeof(TestClass),
                    typeof(TestClass2),
                    typeof(TestClass3Verb),
                    typeof(TestClass4Verb),
                    typeof(ScopedClass1),
                    typeof(ScopedClass2),
                    typeof(ScopedClass3)
                };

                var allWithHelp = allVerbs.Concat(new[] { typeof(TestClass5) });

                yield return new object[] { "testclass".Split(' '), allVerbs, typeof(TestClass) };
                yield return new object[] { "name".Split(' '), allVerbs, typeof(TestClass4Verb) };
                yield return new object[] { "notfound".Split(' '), allVerbs, typeof(DefaultHelp) };
                yield return new object[] { "notfound".Split(' '), allWithHelp, typeof(TestClass5) };
                yield return new object[] { "help file".Split(' '), allVerbs, typeof(DefaultHelp) };
                yield return new object[] { "help file".Split(' '), allWithHelp, typeof(TestClass5) };
                yield return new object[] { "help name".Split(' '), allWithHelp, typeof(TestClass5) };
                yield return new object[] { null, allWithHelp, typeof(TestClass5) };
                yield return new object[] { Array.Empty<string>(), allWithHelp, typeof(TestClass5) };

                yield return new object[] { "scoped".Split(' '), allWithHelp, typeof(ScopedClass1) };
                yield return new object[] { "scoped scope1".Split(' '), allWithHelp, typeof(ScopedClass1) };
                yield return new object[] { "scoped scope2".Split(' '), allWithHelp, typeof(ScopedClass2) };
                yield return new object[] { "scoped scope3".Split(' '), allWithHelp, typeof(ScopedClass3) };
                yield return new object[] { "scoped notscope".Split(' '), allWithHelp, typeof(TestClass5) };
            }
        }

        [Theory]
        [MemberData(nameof(GetVerbTypeTestData))]
        public void GetVerbTypeTest(string[] args, IEnumerable<Type> types, Type expectedType)
        {
            // Setup
            var verbTypes = types.Select(ConsoleAppUtilities.TypeToVerbType);

            // Test
            var actualType = ConsoleAppUtilities.GetVerbType(args, verbTypes);

            // Assert
            actualType.Type.Should().Be(expectedType);
        }

        public static IEnumerable<object[]> ValidateVerbsTestData
        {
            get
            {
                yield return new object[] { new[] { typeof(TestClass), typeof(TestClass2) }, false, null };
                yield return new object[] { new[] { typeof(TestClass), typeof(TestClass2), typeof(TestClass4Verb), typeof(TestClass5), typeof(ScopedClass1), typeof(ScopedClass2), typeof(ScopedClass3) }, false, null };
                yield return new object[] { new[] { typeof(ScopedClass1), typeof(ScopedClass2), typeof(ScopedClass3) }, false, null };

                yield return new object[] { new[] { typeof(TestClass), typeof(TestClass3Verb) }, true, "Scope attribute can only be an Enum." };
                yield return new object[] { new[] { typeof(ScopedClass1), typeof(ScopedClass4) }, true, $"Verb 'ScopedClass4' shares the same name with other verb(s), but doesn't have Scope defined" };
                yield return new object[] { new[] { typeof(ScopedClass1), typeof(ScopedClass2), typeof(ScopedClass3) }, false, null };
                yield return new object[] { new[] { typeof(ScopedClass1), typeof(ScopedClass2), typeof(ScopedClass3) }, false, null };
            }
        }

        [Theory]
        [MemberData(nameof(ValidateVerbsTestData))]
        public void ValidateVerbsTest(IEnumerable<Type> types, bool error, string message)
        {
            // Setup
            var verbTypes = types.Select(ConsoleAppUtilities.TypeToVerbType);
            Action action = () => ConsoleAppUtilities.ValidateVerbs(verbTypes);

            // Test and Assert
            if (error)
            {
                action
                    .ShouldThrow<ParseException>()
                    .WithMessage(message);
            }
        }
    }
}
