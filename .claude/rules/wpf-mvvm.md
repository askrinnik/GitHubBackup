---
paths:
  - "src/GitHubBackup.App/**"
  - "src/GitHubBackup.App.Tests/**"
  - "**/*.xaml"
---

<!-- Based on github/awesome-copilot instructions dotnet-wpf and mvvm-toolkit (MIT), adapted to this repository. -->

# WPF and MVVM (CommunityToolkit.Mvvm)

## Structure

- `GitHubBackup.App` is a host: views, ViewModels, the composition root and UI-only services (dialogs, dispatcher, clipboard, Explorer). Backup logic lives in `Core`/`Infrastructure` and is reached through the same services the CLI uses (FR-12.7).
- The app runs on the .NET Generic Host: `App.xaml.cs` builds the host, registers services and ViewModels, resolves the main window from DI and stops the host on exit.
- Views contain no logic beyond what is purely visual. No business logic, no service calls, no `Click` handlers that do work in code-behind — use commands and bindings.
- ViewModels never reference WPF types (`Window`, `MessageBox`, `Dispatcher`, `Visibility`, brushes). Anything UI-specific goes behind an interface (`IDialogService`, `IUiDispatcher`) so ViewModels are unit-testable without a window.

## ViewModels

- Inherit from `ObservableObject`; `ObservableValidator` only for forms that need `INotifyDataErrorInfo` (settings editor); `ObservableRecipient` only when sending or receiving messages.
- Types using source generators are `partial`. `[ObservableProperty]` goes on private fields named `_name`; never hand-write `SetProperty` boilerplate a generator can emit.
- `[NotifyPropertyChangedFor]` for derived properties, `[NotifyCanExecuteChangedFor]` for commands whose `CanExecute` depends on the property; side effects in `OnXxxChanged` partial methods, never by subscribing to your own `PropertyChanged`.
- Replace a value held by an `[ObservableProperty]` rather than mutating the instance in place — the equality check suppresses the notification otherwise.

## Commands and long-running work

- `[RelayCommand]` on methods returning `void` or `Task`; never `async void`.
- A backup run, discovery or token check is an async command that takes a `CancellationToken` and sets `IncludeCancelCommand = true`; the Cancel button binds to the generated `…CancelCommand` (FR-12.3, NFR-4).
- Keep `AllowConcurrentExecutions = false`; bind `IsRunning` to disable controls while work runs.
- Never block the UI thread: no `.Result`/`.Wait()`, no synchronous I/O in a ViewModel. Progress from the core arrives through `IProgress<T>` (created on the UI thread) or the UI dispatcher abstraction; live log lines are batched before they reach an `ObservableCollection` so a chatty git process cannot flood the dispatcher.

## Dependency injection and messaging

- ViewModels and services are constructor-injected; never `Ioc.Default.GetService<T>()` inside a type the container builds.
- Main window and its ViewModel: singleton; dialogs and their ViewModels: transient.
- Messaging only where two ViewModels must stay decoupled; `WeakReferenceMessenger` registered once as `IMessenger`, handlers registered with `static` lambdas `(r, m) => r.On(m)`.

## XAML

- Bind to ViewModel properties and commands; set `Mode` explicitly when it is not the control's default. Use `x:Name` only when code-behind genuinely needs the element.
- Lists that can hold many items (repository tree, history, live log) use virtualization (`VirtualizingPanel.IsVirtualizing="True"`, recycling mode) and are never wrapped in a `ScrollViewer` that defeats it.
- Shared styles, colors and converters live in resource dictionaries merged in `App.xaml`, not repeated per view.
- Every interactive element an automated UI test needs carries an `AutomationProperties.AutomationId`; names are stable, PascalCase and unique within the window.
- User-visible text is English (NFR-7).

## Visual design

These rules hold for every window and view; a change that breaks one is a defect, not a matter of taste.

- **Separate regions visibly.** A panel that is not the main content (the run summary, a notice) sits in a card: `CardBackgroundFillColorDefaultBrush` fill, a 1 px `SystemColors.ActiveBorderBrushKey` border, `CornerRadius="4"`, padding 8,6. Inside a card, a 1 px line of the same brush with a margin of 6 above and below separates a table from the text lines around it. The Fluent divider brushes are too faint for this in the dark theme.
- **One baseline per table row.** In a `DataGrid`, text columns get an `ElementStyle` based on `DataGridTextColumn.DefaultElementStyle` with `VerticalAlignment="Center"`, and the controls and text blocks of template columns are centred too.
- **Selection keeps the text color.** Selected rows use the translucent accent of `App.xaml`, never a fill that needs a different text color.
- **Nothing is cut.** A table inside a resizable panel keeps at least its column header and one row (`RowDefinition.MinHeight`). A splitter stops at the height of the content below it; when the window has no room left, the panel scrolls as a whole instead of clipping a line.
- **Theme brushes only.** Colors come from Fluent theme resources (`DynamicResource`) or the shared styles in `App.xaml`, so the light and the dark theme both work. Fixed colors are allowed only for status text (`Firebrick`, `DarkGoldenrod`), as the existing views use them.

## Verifying the running app

- **Every visual change is checked with screenshots before and after, in the light and the dark theme.** "Looks better" is not a check: measure. Crop or zoom the changed region and compare pixel rows: the top of the text of every cell in a row is the same pixel row, margins are the same on both sides, and no glyph touches or crosses a border.
- Walk the states of the changed view, each with a screenshot: empty, one row, many rows, a long text, selected and focused, dry run, error, and the smallest size of every panel and splitter around it (start with a small saved panel height in `ui-settings.json` and drag the splitter to its limit). A change is not done while one of these states cuts text or misaligns it.
- Run the app on the test bench: `pwsh -File scripts/test-bench.ps1 -Scenario <name>` (`-Theme light|dark`, `-SummaryPanelHeight 80`, `-List` for the scenarios) copies the build output to a temporary folder with its own `appsettings.json`, `backup-config.json` and `ui-settings.json`, a local stub of the GitHub API and a test token, and removes it when the bench stops (the app exits, Enter or Ctrl+C). Never the user's installation, real repositories or a real token. A state the bench lacks becomes a new scenario in `UiScenarios` (`GitHubBackup.TestUtilities`).
- The screenshot tests (`MainWindowScreenshotTests`, `DiscoveryDialogScreenshotTests`) measure alignment, cut text and border contrast of the main window and the discovery dialog in both themes; a change to those windows keeps them green on an unlocked desktop.

- When an issue changes a window or view, its verify step also checks the visible acceptance items in the running app through the `netwright` MCP server, next to the ViewModel and FlaUI tests.
- Start the bench with `-NoLaunch`, then launch the executable it prints with `desktop_app action=launch path=<bench folder>\GitHubBackup.App.exe`, not with `project=`: the build output folder can hold the developer's own settings. The bench scenarios run no git, so the git variables it prints for that launch matter only to a run that applies a discovery choice. Target elements by AutomationId selectors (`#RepositoryTree`), assert each item with `desktop_expect`, and close the app with `desktop_app action=close` when done.
- Netwright 2.1.0 puts only `{"ok":true}` in `structuredContent`, and Claude Code shows that instead of the text result: snapshots, Change Reports and `pass:` lines arrive empty, while errors arrive in full. So a successful `desktop_expect` returns `{"ok":true}` and a failed one returns `EXPECTATION_FAILED` with the actual state; verify through `desktop_expect`, not by reading snapshots, and use `desktop_screenshot` when you need to see the window.
- On a failure, read `desktop_logs` for the app's output and exception.
- Never point the app at real repositories or a real token during verification; use a temporary content root, as the tests do.
- The check records how an item was verified; it does not replace a test. A flow worth keeping becomes a FlaUI test in `GitHubBackup.App.UITests`.
