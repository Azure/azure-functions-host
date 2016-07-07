using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using WebJobs.Script.Cli.Extensions;
using Xunit;

namespace WebJobs.Script.Cli.Tests.ExtensionsTests
{
    public class GenericExtensionsTests
    {
        class Source
        {
            public string name { get; set; }
            public int value { get; set; }
            public DateTime timestamp { get; set; }
            public Test direction { get; set; }
            public Test from { get; set; }
        }

        class Target
        {
            public string Name { get; set; }
            public int Value { get; set; }
            public DateTime? Timestamp { get; set; }
            public Test? Direction { get; set; }
            public Test From { get; set; }
        }

        enum Test
        {
            North,
            South
        }

        [Fact]
        public void MergeWithTest()
        {
            var source = new Source { name = "Original", value = 10, timestamp = DateTime.UtcNow, direction = Test.South, from = Test.South };
            var target = new Target();

            target = target.MergeWith(source, t => t);

            target.Name.Should().Be(source.name);
            target.Value.Should().Be(source.value);
            target.Timestamp.Should().Be(source.timestamp);
            target.Direction.Should().Be(source.direction);
            target.From.Should().Be(source.from);
        }
    }
}
