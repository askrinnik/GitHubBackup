using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Tests.Hosting;

/// <summary>
/// Tests <see cref="ConfigurationErrorMessages"/>.
/// </summary>
public sealed class ConfigurationErrorMessagesTests
{
    [Fact]
    public void TryGet_OptionsValidationException_ReturnsEachFailure()
    {
        var exception = new OptionsValidationException("", typeof(object), ["First failure.", "Second failure."]);

        var found = ConfigurationErrorMessages.TryGet(exception, out var messages);

        found.ShouldBeTrue();
        messages.ShouldBe(["First failure.", "Second failure."]);
    }

    [Fact]
    public void TryGet_AggregateOfValidationExceptions_ReturnsFailuresOfAll()
    {
        var exception = new AggregateException(
            new OptionsValidationException("", typeof(object), ["First failure."]),
            new OptionsValidationException("", typeof(string), ["Second failure."]));

        var found = ConfigurationErrorMessages.TryGet(exception, out var messages);

        found.ShouldBeTrue();
        messages.ShouldBe(["First failure.", "Second failure."]);
    }

    [Fact]
    public void TryGet_InvalidDataExceptionWithInner_JoinsMessages()
    {
        var exception = new InvalidDataException("Failed to load the file.", new FormatException("Line 2."));

        var found = ConfigurationErrorMessages.TryGet(exception, out var messages);

        found.ShouldBeTrue();
        messages.ShouldHaveSingleItem().ShouldBe("Failed to load the file. Line 2.");
    }

    [Fact]
    public void TryGet_FileNotFoundException_ReturnsMessage()
    {
        var found = ConfigurationErrorMessages.TryGet(new FileNotFoundException("The file is missing."), out var messages);

        found.ShouldBeTrue();
        messages.ShouldHaveSingleItem().ShouldBe("The file is missing.");
    }

    [Fact]
    public void TryGet_BackupConfigException_ReturnsEachError()
    {
        var exception = new BackupConfigException(
            @"C:\app\backup-config.json", ["backupRoot must be set.", "accounts[1].url must be an account URL."]);

        var found = ConfigurationErrorMessages.TryGet(exception, out var messages);

        found.ShouldBeTrue();
        messages.ShouldBe(["backupRoot must be set.", "accounts[1].url must be an account URL."]);
    }

    [Fact]
    public void TryGet_AggregateWithOtherException_ReturnsFalse()
    {
        var exception = new AggregateException(
            new OptionsValidationException("", typeof(object), ["A failure."]),
            new InvalidOperationException("Not configuration."));

        var found = ConfigurationErrorMessages.TryGet(exception, out var messages);

        found.ShouldBeFalse();
        messages.ShouldBeEmpty();
    }

    [Fact]
    public void TryGet_UnrelatedException_ReturnsFalse()
    {
        var found = ConfigurationErrorMessages.TryGet(new InvalidOperationException("Not configuration."), out var messages);

        found.ShouldBeFalse();
        messages.ShouldBeEmpty();
    }
}
