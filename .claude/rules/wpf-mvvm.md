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
