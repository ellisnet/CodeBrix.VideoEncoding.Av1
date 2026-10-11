using System.Collections.Generic;
using System.IO;
using CodeBrix.VideoEncoding.Av1.Interop;
using SilverAssertions;
using Xunit;

namespace CodeBrix.VideoEncoding.Av1.Tests;

public class Av1LibraryTests
{
    [Fact]
    public void LibraryName_is_the_contract_name() => Av1Library.LibraryName.Should().Be("codebrix_av1");

    [Fact]
    public void ExpectedApiVersion_is_1() => Av1Library.ExpectedApiVersion.Should().Be(1);

    [Fact]
    public void EnumerateProbePaths_tries_the_directory_then_its_runtimes_folder()
    {
        //Arrange
        string root = Path.Combine(Path.GetTempPath(), "app");

        //Act
        IReadOnlyList<string> paths = Av1Library.EnumerateProbePaths(new[] { root, null, root }, "linux-x64", "libcodebrix_av1.so");

        //Assert
        paths.Should().Equal(
            Path.Combine(root, "libcodebrix_av1.so"),
            Path.Combine(root, "runtimes", "linux-x64", "native", "libcodebrix_av1.so"));
    }

    [Fact]
    public void DescribeLoadFailure_lists_every_path_and_the_rid()
    {
        //Arrange
        string root = Path.Combine(Path.GetTempPath(), "app");

        //Act
        string message = Av1Library.DescribeLoadFailure(new[] { root }, "osx-arm64", "libcodebrix_av1.dylib");

        //Assert
        message.Should().Contain("'osx-arm64'");
        message.Should().Contain(Path.Combine(root, "runtimes", "osx-arm64", "native", "libcodebrix_av1.dylib"));
        message.Should().Contain("runtimes/osx-arm64/native/");
    }

    [Fact]
    public void DescribeVersionMismatch_names_both_versions()
    {
        //Act
        string message = Av1Library.DescribeVersionMismatch("/x/libcodebrix_av1.so", 2, "9.9.9");

        //Assert
        message.Should().Contain("contract version 2");
        message.Should().Contain("contract version 1");
        message.Should().Contain("9.9.9");
    }

    [Fact]
    public void BaseDirectories_starts_with_the_application_directory()
        => Av1Library.BaseDirectories()[0].Should().Be(System.AppContext.BaseDirectory);
}
