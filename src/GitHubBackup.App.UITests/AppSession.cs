using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Runs the executable of an <see cref="AppInstallation"/> and drives its windows through UI Automation patterns,
/// without the mouse, the keyboard or the focus.
/// </summary>
/// <remarks>
/// Every wait polls the UI until its condition holds and fails the test when its time limit runs out. Disposing the
/// session saves a screenshot of the desktop when the test failed and kills the process tree when it still runs.
/// </remarks>
internal sealed class AppSession : IDisposable
{
    /// <summary>The automation id of the main window.</summary>
    public const string MainWindowId = "MainWindow";

    /// <summary>The automation id of the static text of a Win32 message box.</summary>
    private const string _messageTextId = "65535";

    /// <summary>The environment variable that names the folder of the screenshots of failed tests.</summary>
    private const string _screenshotFolderVariable = "UI_TEST_SCREENSHOTS";

    /// <summary>The name UI Automation gives an item of the validation errors of a field.</summary>
    private const string _validationErrorItemName = "System.Windows.Controls.ValidationError";

    /// <summary>The distance in pixels within which an error text counts as aligned with its field.</summary>
    private const int _fieldErrorTolerance = 4;

    private readonly UIA3Automation _automation = new();
    private readonly Process _process;
    private AutomationElement? _mainWindow;

    /// <summary>
    /// Wraps a started process.
    /// </summary>
    /// <param name="process">The process of the application.</param>
    private AppSession(Process process) => _process = process;

    /// <summary>Gets the main window, waiting until it appears.</summary>
    public AutomationElement MainWindow =>
        _mainWindow ??= WaitUntilFound(() => FindWindow(MainWindowId), MainWindowId, UiTimeouts.Launch);

    /// <summary>
    /// Starts the executable of <paramref name="installation"/> in its folder.
    /// </summary>
    /// <remarks>
    /// The process gets the environment of the test process without the variables that configure the application or
    /// git, so that a value set on the developer's machine cannot change the scenario, plus the variables of the
    /// installation.
    /// </remarks>
    /// <param name="installation">The installation to run.</param>
    /// <returns>The session.</returns>
    public static AppSession Start(AppInstallation installation)
    {
        var startInfo = new ProcessStartInfo(installation.ExecutablePath)
        {
            WorkingDirectory = installation.FolderPath,
            UseShellExecute = false,
        };
        foreach (var name in startInfo.Environment.Keys.Where(IsInheritedConfiguration).ToList())
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var (name, value) in installation.Environment)
        {
            startInfo.Environment[name] = value;
        }

        return new(Process.Start(startInfo)!);
    }

    /// <summary>
    /// Waits until an element of the main window with <paramref name="automationId"/> is shown.
    /// </summary>
    /// <param name="automationId">The automation id.</param>
    /// <param name="timeout">The time limit, or <see langword="null"/> for <see cref="UiTimeouts.Element"/>.</param>
    /// <returns>The element.</returns>
    public AutomationElement WaitFor(string automationId, TimeSpan? timeout = null) =>
        WaitFor(MainWindow, automationId, timeout);

    /// <summary>
    /// Waits until a descendant of <paramref name="root"/> with <paramref name="automationId"/> is shown.
    /// </summary>
    /// <param name="root">The element to search in.</param>
    /// <param name="automationId">The automation id.</param>
    /// <param name="timeout">The time limit, or <see langword="null"/> for <see cref="UiTimeouts.Element"/>.</param>
    /// <returns>The element.</returns>
    public static AutomationElement WaitFor(AutomationElement root, string automationId, TimeSpan? timeout = null) =>
        WaitUntilFound(
            () => root.FindFirstDescendant(condition => condition.ByAutomationId(automationId)),
            automationId,
            timeout ?? UiTimeouts.Element);

    /// <summary>
    /// Waits until the main window shows no element with <paramref name="automationId"/>.
    /// </summary>
    /// <param name="automationId">The automation id.</param>
    public void WaitUntilGone(string automationId) =>
        WaitUntil(
            () => MainWindow.FindFirstDescendant(condition => condition.ByAutomationId(automationId)) is null,
            $"'{automationId}' to disappear");

    /// <summary>
    /// Waits until <paramref name="condition"/> holds.
    /// </summary>
    /// <param name="condition">The condition, checked repeatedly.</param>
    /// <param name="description">What the test waits for, for the failure message.</param>
    /// <param name="timeout">The time limit, or <see langword="null"/> for <see cref="UiTimeouts.Element"/>.</param>
    /// <exception cref="TimeoutException">The condition still does not hold when the time limit runs out.</exception>
    public static void WaitUntil(Func<bool> condition, string description, TimeSpan? timeout = null)
    {
        var limit = timeout ?? UiTimeouts.Element;
        var result = Retry.WhileFalse(condition, limit, UiTimeouts.PollInterval, ignoreException: true);
        if (!result.Success)
        {
            throw new TimeoutException($"Waited {limit} for {description}.");
        }
    }

    /// <summary>
    /// Waits until a window of the application with <paramref name="automationId"/> is shown.
    /// </summary>
    /// <param name="automationId">The automation id of the window.</param>
    /// <param name="timeout">The time limit, or <see langword="null"/> for <see cref="UiTimeouts.Element"/>.</param>
    /// <returns>The window.</returns>
    public AutomationElement WaitForWindow(string automationId, TimeSpan? timeout = null) =>
        WaitUntilFound(() => FindWindow(automationId), automationId, timeout ?? UiTimeouts.Element);

    /// <summary>
    /// Waits until the application shows a message box with <paramref name="title"/>.
    /// </summary>
    /// <param name="title">The title of the message box.</param>
    /// <param name="timeout">The time limit, or <see langword="null"/> for <see cref="UiTimeouts.Element"/>.</param>
    /// <returns>The message box window.</returns>
    public AutomationElement WaitForMessageBox(string title, TimeSpan? timeout = null) =>
        WaitUntilFound(
            () => ApplicationWindows().FirstOrDefault(window =>
                window.Name == title && window.FindFirstChild(condition => condition.ByAutomationId(_messageTextId)) is not null),
            $"message box '{title}'",
            timeout ?? UiTimeouts.Element);

    /// <summary>
    /// Returns the text of a message box.
    /// </summary>
    /// <param name="messageBox">The message box window.</param>
    /// <returns>The text.</returns>
    public static string MessageText(AutomationElement messageBox) => WaitFor(messageBox, _messageTextId).Name;

    /// <summary>
    /// Presses a button of a message box by its dialog result id, which does not depend on the language of Windows.
    /// </summary>
    /// <param name="messageBox">The message box window.</param>
    /// <param name="result">The button.</param>
    public static void Answer(AutomationElement messageBox, MessageBoxButtonId result) =>
        Invoke(WaitFor(messageBox, ((int)result).ToString(System.Globalization.CultureInfo.InvariantCulture)));

    /// <summary>
    /// Presses a button of the main window once it is enabled.
    /// </summary>
    /// <param name="automationId">The automation id of the button.</param>
    public void Invoke(string automationId) => Invoke(WaitFor(automationId));

    /// <summary>
    /// Presses a button through its invoke pattern once it is enabled.
    /// </summary>
    /// <param name="element">The button.</param>
    public static void Invoke(AutomationElement element)
    {
        WaitUntil(() => element.IsEnabled, $"'{element.AutomationId}' to be enabled");
        element.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>
    /// Replaces the text of a text box of the main window through its value pattern.
    /// </summary>
    /// <param name="automationId">The automation id of the text box.</param>
    /// <param name="text">The new text.</param>
    public void SetText(string automationId, string text) => SetText(WaitFor(automationId), text);

    /// <summary>
    /// Replaces the text of a text box through its value pattern once it is enabled.
    /// </summary>
    /// <param name="element">The text box.</param>
    /// <param name="text">The new text.</param>
    public static void SetText(AutomationElement element, string text)
    {
        WaitUntil(() => element.IsEnabled, $"'{element.AutomationId}' to be enabled");
        element.Patterns.Value.Pattern.SetValue(text);
    }

    /// <summary>
    /// Returns the validation error the main window shows below <paramref name="field"/>, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// The error template of a form field lists its validation errors in the adorner layer, which UI Automation shows
    /// apart from the field; an error belongs to the field when it starts at the left edge of the field, just below
    /// it.
    /// </remarks>
    /// <param name="field">The text box.</param>
    /// <returns>The error text, or <see langword="null"/> while the field shows no error.</returns>
    public string? FieldError(AutomationElement field)
    {
        var bounds = field.BoundingRectangle;
        return MainWindow
            .FindAllDescendants(condition => condition.ByName(_validationErrorItemName))
            .SelectMany(item => item.FindAllDescendants(condition => condition.ByControlType(ControlType.Text)))
            .FirstOrDefault(text =>
                Math.Abs(text.BoundingRectangle.Left - bounds.Left) <= _fieldErrorTolerance
                && text.BoundingRectangle.Top >= bounds.Bottom - _fieldErrorTolerance
                && text.BoundingRectangle.Top <= bounds.Bottom + bounds.Height)
            ?.Name;
    }

    /// <summary>
    /// Closes the main window through its window pattern.
    /// </summary>
    public void CloseMainWindow() => MainWindow.Patterns.Window.Pattern.Close();

    /// <summary>
    /// Closes a window through its window pattern; a message box with only an OK button closes as if OK was pressed.
    /// </summary>
    /// <param name="window">The window.</param>
    public static void Close(AutomationElement window) => window.Patterns.Window.Pattern.Close();

    /// <summary>
    /// Waits until the process exits.
    /// </summary>
    /// <returns>The exit code of the process.</returns>
    /// <exception cref="TimeoutException">The process still runs when <see cref="UiTimeouts.Exit"/> runs out.</exception>
    public async Task<int> WaitForExitAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(UiTimeouts.Exit);
        try
        {
            await _process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The application did not exit within {UiTimeouts.Exit}.");
        }

        return _process.ExitCode;
    }

    /// <summary>
    /// Saves a screenshot when the test failed, then kills the process tree when it still runs.
    /// </summary>
    public void Dispose()
    {
        if (TestContext.Current.TestState?.Result == TestResult.Failed)
        {
            SaveScreenshot();
        }

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }

        _process.Dispose();
        _automation.Dispose();
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> configures the application or git.
    /// </summary>
    /// <param name="name">The name of an environment variable.</param>
    /// <returns><see langword="true"/> for a <c>GitHubBackup__</c> or a <c>GIT_</c> variable.</returns>
    private static bool IsInheritedConfiguration(string name) =>
        name.StartsWith(GitHubBackupHostBuilder.EnvironmentVariablePrefix, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Polls <paramref name="find"/> until it returns an element.
    /// </summary>
    /// <param name="find">Looks for the element; returns <see langword="null"/> while it is not shown.</param>
    /// <param name="description">What the test waits for, for the failure message.</param>
    /// <param name="timeout">The time limit.</param>
    /// <returns>The element.</returns>
    /// <exception cref="TimeoutException">The element is not shown when the time limit runs out.</exception>
    private static AutomationElement WaitUntilFound(Func<AutomationElement?> find, string description, TimeSpan timeout)
    {
        var result = Retry.WhileNull(find, timeout, UiTimeouts.PollInterval, ignoreException: true);
        return result.Success && result.Result is { } element
            ? element
            : throw new TimeoutException($"Waited {timeout} for '{description}'.");
    }

    /// <summary>
    /// Returns the window of the application with <paramref name="automationId"/>, or <see langword="null"/>.
    /// </summary>
    /// <param name="automationId">The automation id of the window.</param>
    /// <returns>The window, or <see langword="null"/> while it is not shown.</returns>
    private AutomationElement? FindWindow(string automationId) =>
        ApplicationWindows().FirstOrDefault(window => window.AutomationId == automationId);

    /// <summary>
    /// Returns the top-level windows of the process and the windows they own, which UI Automation shows as their
    /// children.
    /// </summary>
    /// <returns>The windows.</returns>
    private IEnumerable<AutomationElement> ApplicationWindows()
    {
        var topLevel = _automation.GetDesktop().FindAllChildren(condition =>
            condition.ByProcessId(_process.Id).And(condition.ByControlType(ControlType.Window)));
        foreach (var window in topLevel)
        {
            yield return window;
            foreach (var owned in window.FindAllChildren(condition => condition.ByControlType(ControlType.Window)))
            {
                yield return owned;
            }
        }
    }

    /// <summary>
    /// Saves a screenshot of each window of the application, named after the test, in the folder named by
    /// <see cref="_screenshotFolderVariable"/> or in <c>Screenshots</c> next to the test assembly.
    /// </summary>
    /// <remarks>
    /// Only the windows of the application are captured: a shot of the whole desktop would also hold whatever
    /// else is on the screen, and on CI the files are uploaded as a downloadable artifact.
    /// </remarks>
    private void SaveScreenshot()
    {
        if (_process.HasExited)
        {
            return;
        }

        var folder = System.Environment.GetEnvironmentVariable(_screenshotFolderVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "Screenshots");
        Directory.CreateDirectory(folder);
        var testName = TestContext.Current.Test?.TestDisplayName ?? "unknown";
        var fileName = string.Concat(testName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var index = 0;
        foreach (var window in ApplicationWindows())
        {
            using var image = Capture.Element(window);
            image.ToFile(Path.Combine(folder, $"{fileName}-{index++}.png"));
        }
    }
}
