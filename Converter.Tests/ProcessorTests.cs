using System;
using FluentAssertions;
using Moq;
using Xunit;

namespace Converter.Tests
{
    public class ProcessorTests
    {
        private readonly Mock<Files> _files;
        private readonly Processor _processor;
        private string _written;

        public ProcessorTests()
        {
            _files = new Mock<Files>();
            _processor = new Processor("", "", _files.Object);
            _files.Setup(f => f.GetDirectories(It.IsAny<string>()))
                .Returns(Array.Empty<string>());
            _files.Setup(f => f.GetFiles(It.IsAny<string>()))
                .Returns(new[] {"foo.cs"});
            _files.Setup(f => f.WriteContents(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string,string>((_, contents) => _written = contents);
        }
        
        [Theory]
        [InlineData(
            "    internal class Foo", 
            "    public class Foo")]
        [InlineData(
            "    protected internal string Bar", 
            "    public string Bar")]
        [InlineData(
            "    internal set;}", 
            "    set;}")]
        [InlineData(
            "    internal Foo Foo {get; set;}", 
            "    public Foo Foo {get; set;}")]
        public void PublicizeWorks(string input, string expected)
        {
            _files.Setup(f => f.GetContents(It.IsAny<string>())).Returns(input);
            _processor.Publicize();

            _written.Should().Be(expected);

        }
    }
}
