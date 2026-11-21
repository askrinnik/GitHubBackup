using System.IO.Abstractions.TestingHelpers;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace GitHubBackup.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests <see cref="AppSettingsFile"/>.
/// </summary>
public sealed class AppSettingsFileTests : IDisposable
{
    private const string _contentRootPath = @"C:\app";
    private const string _filePath = @"C:\app\" + GitHubBackupHostBuilder.AppSettingsFileName;

    private readonly MockFileSystem _fileSystem = new();

    private readonly AppSettingsFile _file;

    /// <summary>
    /// Creates the content root and the subject.
    /// </summary>
    public AppSettingsFileTests()
    {
        _fileSystem.AddDirectory(_contentRootPath);
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRootPath);
        _file = new(_fileSystem, environment, new AtomicFileWriter(_fileSystem));
    }

    /// <inheritdoc />
    public void Dispose() => _file.Dispose();

    [Fact]
    public void FilePath_ContentRoot_IsAppSettingsInContentRoot() => _file.FilePath.ShouldBe(_filePath);

    [Fact]
    public async Task WriteSectionValuesAsync_NoFile_CreatesFileWithSection()
    {
        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              }
            }
            """));
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public async Task WriteSectionValuesAsync_BlankFile_WritesSection(string content)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(content));

        await WriteToolsAsync();

        ReadFile().ShouldStartWith(Text("""
            {
              "Tools": {
            """).TrimEnd());
    }

    [Fact]
    public async Task WriteSectionValuesAsync_OtherKeysAndSections_KeepsThemInOrder()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "GitHub": { "ApiBaseUrl": "https://api.github.com", "Token": null },
              "Tools": { "Extra": [ 1, "two" ], "SevenZipPath": "C:\\7z\\7z.exe" },
              "Serilog": { "MinimumLevel": { "Default": "Debug", "Override": { "Microsoft": "Warning" } } }
            }
            """));

        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "GitHub": {
                "ApiBaseUrl": "https://api.github.com",
                "Token": null
              },
              "Tools": {
                "Extra": [
                  1,
                  "two"
                ],
                "SevenZipPath": null,
                "GitPath": "D:\\Git\\git.exe"
              },
              "Serilog": {
                "MinimumLevel": {
                  "Default": "Debug",
                  "Override": {
                    "Microsoft": "Warning"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_SectionMissing_AppendsItAtEnd()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""{ "Backup": { "ShortHashLength": 12 } }"""));

        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "Backup": {
                "ShortHashLength": 12
              },
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_KeysInOtherCase_ReplacesThemWithCanonicalNamesWithoutDuplicates()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            """{ "tools": { "gitpath": "C:\\Old\\git.exe", "GITPATH": "C:\\Older\\git.exe", "sevenzippath": "C:\\7z\\7z.exe" } }"""));

        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_SectionTwiceInOtherCase_WritesFirstAndRemovesKeysFromSecond()
    {
        _fileSystem.AddFile(_filePath, new MockFileData(
            """{ "Tools": { "Extra": 1 }, "TOOLS": { "GitPath": "C:\\Old\\git.exe", "Other": 2 } }"""));

        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "Tools": {
                "Extra": 1,
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              },
              "TOOLS": {
                "Other": 2
              }
            }
            """));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"C:\\\\Git\"")]
    [InlineData("[ 1 ]")]
    public async Task WriteSectionValuesAsync_SectionNotObject_ReplacesItWithSection(string value)
    {
        _fileSystem.AddFile(_filePath, new MockFileData($$"""{ "Tools": {{value}}, "Backup": { "VerifyArchive": false } }"""));

        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              },
              "Backup": {
                "VerifyArchive": false
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_CommentsAndTrailingCommas_ReadsFileAndDropsComments()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              // The archive check.
              "Backup": { "VerifyArchive": false, },
            }
            """));

        await WriteToolsAsync();

        ReadFile().ShouldBe(Text("""
            {
              "Backup": {
                "VerifyArchive": false
              },
              "Tools": {
                "GitPath": "D:\\Git\\git.exe",
                "SevenZipPath": null
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_ByteOrderMark_ReadsFileAndWritesWithout()
    {
        _fileSystem.AddFile(_filePath, new MockFileData([0xEF, 0xBB, 0xBF, .. """{ "Backup": {} }"""u8.ToArray()]));

        await WriteToolsAsync();

        _fileSystem.File.ReadAllBytes(_filePath)[0].ShouldBe((byte)'{');
        ReadFile().ShouldContain("\"Backup\": {}");
    }

    [Theory]
    [InlineData("{ \"Tools\": ")]
    [InlineData("not json")]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"text\"")]
    public async Task WriteSectionValuesAsync_FileNotJsonObject_ThrowsWithPathAndLeavesFileIntact(string content)
    {
        _fileSystem.AddFile(_filePath, new MockFileData(content));

        var exception = await Should.ThrowAsync<IOException>(WriteToolsAsync);

        exception.Message.ShouldContain(_filePath);
        ReadFile().ShouldBe(content);
        _fileSystem.File.Exists(_filePath + AtomicFileWriter.BackupExtension).ShouldBeFalse();
    }

    [Fact]
    public async Task WriteSectionValuesAsync_FileExists_KeepsPreviousAsBackup()
    {
        const string previous = """{ "GitHub": { "Token": "kept" } }""";
        _fileSystem.AddFile(_filePath, new MockFileData(previous));

        await WriteToolsAsync();

        _fileSystem.File.ReadAllText(_filePath + AtomicFileWriter.BackupExtension).ShouldBe(previous);
    }

    [Fact]
    public async Task WriteSectionValuesAsync_BooleanAndNonAsciiText_WritesThemReadably()
    {
        await _file.WriteSectionValuesAsync(
            "OpenTelemetry",
            [AppSettingValue.FromFlag("Enabled", true), AppSettingValue.FromText("OtlpEndpoint", "http://сервер:4317")],
            TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "OpenTelemetry": {
                "Enabled": true,
                "OtlpEndpoint": "http://сервер:4317"
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_RemovedKey_DropsItAndKeepsOtherKeys()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            { "OpenTelemetry": { "Enabled": false, "headers": "api-key=secret", "Extra": 1 } }
            """));

        await _file.WriteSectionValuesAsync(
            "OpenTelemetry",
            [AppSettingValue.FromFlag("Enabled", true), AppSettingValue.Remove("Headers")],
            TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "OpenTelemetry": {
                "Enabled": true,
                "Extra": 1
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_RemovedKeyInSecondOccurrence_DropsItThere()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            { "OpenTelemetry": { "Enabled": false }, "OpenTelemetry": { "Headers": "api-key=secret", "Extra": 1 } }
            """));

        await _file.WriteSectionValuesAsync(
            "OpenTelemetry",
            [AppSettingValue.FromFlag("Enabled", true), AppSettingValue.Remove("Headers")],
            TestContext.Current.CancellationToken);

        ReadFile().ShouldNotContain("Headers");
        ReadFile().ShouldContain("\"Extra\": 1");
    }

    [Fact]
    public async Task WriteSectionValuesAsync_RemovedKeyMissing_IsNotAdded()
    {
        await _file.WriteSectionValuesAsync(
            "OpenTelemetry",
            [AppSettingValue.FromFlag("Enabled", false), AppSettingValue.Remove("Headers")],
            TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "OpenTelemetry": {
                "Enabled": false
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionValuesAsync_NestedPathMissing_AddsParentSections()
    {
        await _file.WriteSectionValuesAsync(
            "Serilog:Endpoints:File", [AppSettingValue.FromText("Level", "Debug")], TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "Serilog": {
                "Endpoints": {
                  "File": {
                    "Level": "Debug"
                  }
                }
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionsAsync_NestedSections_KeepsOverridesAndOtherKeys()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "Serilog": {
                "MinimumLevel": { "Default": "Warning", "Override": { "Microsoft": "Error" } },
                "Endpoints": {
                  "File": { "Level": "Error", "Override": { "GitHubBackup.Infrastructure": "Verbose" } },
                  "Custom": 1
                }
              },
              "Tools": { "GitPath": null }
            }
            """));

        await _file.WriteSectionsAsync(
            [
                new("Serilog:Endpoints:File", [AppSettingValue.FromText("Level", "Debug")]),
                new("Serilog:Endpoints:Console", [AppSettingValue.FromText("Level", null)]),
            ],
            TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "Serilog": {
                "MinimumLevel": {
                  "Default": "Warning",
                  "Override": {
                    "Microsoft": "Error"
                  }
                },
                "Endpoints": {
                  "File": {
                    "Level": "Debug",
                    "Override": {
                      "GitHubBackup.Infrastructure": "Verbose"
                    }
                  },
                  "Custom": 1,
                  "Console": {
                    "Level": null
                  }
                }
              },
              "Tools": {
                "GitPath": null
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionsAsync_NestedNamesInOtherCase_ReplacesWithCanonicalNamesAndRemovesKeysFromCopies()
    {
        _fileSystem.AddFile(_filePath, new MockFileData("""
            {
              "serilog": { "endpoints": { "file": { "level": "Error", "override": { "A": "Off" } } } },
              "SERILOG": { "Endpoints": { "FILE": { "LEVEL": "Fatal", "Extra": 2 } } }
            }
            """));

        await _file.WriteSectionValuesAsync(
            "Serilog:Endpoints:File", [AppSettingValue.FromText("Level", "Debug")], TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "Serilog": {
                "Endpoints": {
                  "File": {
                    "Level": "Debug",
                    "override": {
                      "A": "Off"
                    }
                  }
                }
              },
              "SERILOG": {
                "Endpoints": {
                  "FILE": {
                    "Extra": 2
                  }
                }
              }
            }
            """));
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public async Task WriteSectionValuesAsync_TextInPlaceOfNestedSection_ReplacesItWithSection(string value)
    {
        _fileSystem.AddFile(_filePath, new MockFileData($$"""{ "Serilog": { "Endpoints": {{value}}, "Other": true } }"""));

        await _file.WriteSectionValuesAsync(
            "Serilog:Endpoints:File", [AppSettingValue.FromText("Level", "Off")], TestContext.Current.CancellationToken);

        ReadFile().ShouldBe(Text("""
            {
              "Serilog": {
                "Endpoints": {
                  "File": {
                    "Level": "Off"
                  }
                },
                "Other": true
              }
            }
            """));
    }

    [Fact]
    public async Task WriteSectionsAsync_SeveralSections_ReplacesFileOnceWithOneBackup()
    {
        const string previous = """{ "Tools": { "GitPath": "C:\\Old\\git.exe" } }""";
        _fileSystem.AddFile(_filePath, new MockFileData(previous));

        await _file.WriteSectionsAsync(
            [
                new("Tools", [AppSettingValue.FromText("GitPath", null)]),
                new("Serilog:Endpoints:LiveLog", [AppSettingValue.FromText("Level", "Warning")]),
            ],
            TestContext.Current.CancellationToken);

        _fileSystem.File.ReadAllText(_filePath + AtomicFileWriter.BackupExtension).ShouldBe(previous);
        _fileSystem.Directory.GetFiles(_contentRootPath).Length.ShouldBe(2);
        ReadFile().ShouldContain("\"GitPath\": null");
        ReadFile().ShouldContain("\"Level\": \"Warning\"");
    }

    [Theory]
    [InlineData("Serilog::File")]
    [InlineData(":Tools")]
    [InlineData("")]
    public async Task WriteSectionValuesAsync_EmptyNameInPath_ThrowsAndWritesNothing(string path)
    {
        await Should.ThrowAsync<ArgumentException>(() => _file.WriteSectionValuesAsync(
            path, [AppSettingValue.FromText("Level", "Debug")], TestContext.Current.CancellationToken));

        _fileSystem.File.Exists(_filePath).ShouldBeFalse();
    }

    /// <summary>
    /// Writes the two tool paths through the subject: a set Git path and a cleared 7-Zip path.
    /// </summary>
    /// <returns>A task that completes when the file is written.</returns>
    private Task WriteToolsAsync() => _file.WriteSectionValuesAsync(
        "Tools",
        [AppSettingValue.FromText("GitPath", @"D:\Git\git.exe"), AppSettingValue.FromText("SevenZipPath", null)],
        TestContext.Current.CancellationToken);

    /// <summary>
    /// Reads the file as text.
    /// </summary>
    /// <returns>The content of the file.</returns>
    private string ReadFile() => _fileSystem.File.ReadAllText(_filePath);

    /// <summary>
    /// Returns the text the subject writes for <paramref name="json"/>: Windows line breaks and a final line break.
    /// </summary>
    /// <param name="json">The expected JSON.</param>
    /// <returns>The expected content of the file.</returns>
    private static string Text(string json) => json.ReplaceLineEndings("\r\n") + "\r\n";
}
