using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using System.IO;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>Why the harness could not do something, in one sentence.</summary>
/// <param name="Succeeded">Whether it worked.</param>
/// <param name="Detail">What happened.</param>
public readonly record struct ReviewStep(bool Succeeded, string Detail)
{
    /// <summary>A step that worked.</summary>
    public static ReviewStep Ok(string detail) => new(true, detail);

    /// <summary>A step that did not.</summary>
    public static ReviewStep Failed(string detail) => new(false, detail);
}

/// <summary>
/// The AgencyOS Windows client, running, under review.
/// </summary>
/// <remarks>
/// <para>
/// Launches rather than attaches by default, and launches with an environment the
/// harness controls, so the client is pointed at the synthetic review tenant and
/// cannot be pointed at anything else by whatever happened to be set in the
/// operator's shell.
/// </para>
/// <para>
/// Everything is scoped to this process. The UI Automation root is found by
/// process identifier rather than by window title, because a title is something
/// another application can also have.
/// </para>
/// </remarks>
internal sealed class ReviewApp : IDisposable
{
    private readonly Process _process;

    private ReviewApp(Process process, AutomationElement window, nint handle)
    {
        _process = process;
        Window = window;
        Handle = handle;
        Keys = new Keyboard(process);
    }

    /// <summary>The shell window's automation element.</summary>
    internal AutomationElement Window { get; private set; }

    /// <summary>The shell window's handle.</summary>
    internal nint Handle { get; }

    /// <summary>Keyboard, scoped to this process.</summary>
    internal Keyboard Keys { get; }

    /// <summary>The process identifier, for the evidence record.</summary>
    internal int ProcessId => _process.Id;

    /// <summary>Whether the application is still running.</summary>
    internal bool IsAlive
    {
        get
        {
            _process.Refresh();

            return !_process.HasExited;
        }
    }

    /// <summary>Starts the client against a named environment and waits for its window.</summary>
    internal static ReviewApp Launch(
        string executablePath,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(environment);

        ProcessStartInfo start = new(executablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
        };

        foreach ((string name, string value) in environment)
        {
            start.Environment[name] = value;
        }

        Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The client did not start.");

        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The client exited during startup with code "
                        + process.ExitCode.ToString(CultureInfo.InvariantCulture)
                        + ". A review cannot be run against a process that is not there.");
            }

            if (process.MainWindowHandle != 0)
            {
                nint handle = process.MainWindowHandle;
                AutomationElement window = AutomationElement.FromHandle(handle);

                // A window handle exists before the XAML tree is populated. Waiting
                // for the navigation pane avoids photographing a half-built shell
                // and reporting it as a rendering defect.
                Thread.Sleep(1500);

                return new ReviewApp(process, window, handle);
            }

            Thread.Sleep(250);
        }

        throw new TimeoutException("The client started and showed no window within " + timeout + ".");
    }

    /// <summary>Brings the window forward so keystrokes and captures reach it.</summary>
    internal bool Focus()
    {
        Native.ShowWindow(Handle, 9);
        Native.SetForegroundWindow(Handle);
        Thread.Sleep(220);

        return Native.GetForegroundWindow() == Handle;
    }

    /// <summary>Resizes the window so every capture is comparable.</summary>
    internal void Resize(int width, int height)
    {
        Native.MoveWindow(Handle, 40, 40, width, height, true);
        Thread.Sleep(400);
    }

    /// <summary>The window's current size, as width x height.</summary>
    internal string Size()
    {
        if (!Native.GetWindowRect(Handle, out Native.Rect rect))
        {
            return "unknown";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{rect.Width}x{rect.Height}");
    }

    /// <summary>Reads the whole automation tree below the shell window.</summary>
    internal UiaNode Snapshot() => UiaTree.Read(Window);

    /// <summary>Finds one element by automation identifier.</summary>
    internal AutomationElement? Find(string automationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(automationId);

        try
        {
            return Window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>Finds one element by the name a screen reader would announce.</summary>
    internal AutomationElement? FindByName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            return Window.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, name));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves to a workspace by selecting its navigation item.
    /// </summary>
    /// <remarks>
    /// Through the navigation pane rather than by sending the shortcut, so that
    /// navigating is not the thing being measured when the keyboard pass later
    /// checks whether the shortcut works.
    /// </remarks>
    internal ReviewStep Navigate(string workspaceLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceLabel);

        AutomationElement? item = FindByName(workspaceLabel);

        // The organization surface is not a workspace. Repair Wave 003E-A put it
        // behind the navigation pane's settings item, whose accessible name is
        // whatever the operating system's display language calls "Settings" -
        // this machine announces it as a Hebrew word - so it is found by its
        // automation id instead. Recorded as AOS-R002-015: a surface the command
        // palette cannot reach and a keyboard accelerator cannot address.
        if (item is null
            && string.Equals(workspaceLabel, "Organization", StringComparison.OrdinalIgnoreCase))
        {
            item = Find("SettingsItem");
        }

        if (item is null)
        {
            return ReviewStep.Failed(
                "No navigation item named '" + workspaceLabel + "' is in the automation tree.");
        }

        try
        {
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? pattern)
                && pattern is SelectionItemPattern selection)
            {
                selection.Select();
                Thread.Sleep(900);

                return ReviewStep.Ok("Selected '" + workspaceLabel + "'.");
            }

            if (item.TryGetCurrentPattern(InvokePattern.Pattern, out object? invoke)
                && invoke is InvokePattern invoker)
            {
                invoker.Invoke();
                Thread.Sleep(900);

                return ReviewStep.Ok("Invoked '" + workspaceLabel + "'.");
            }

            return ReviewStep.Failed(
                "'" + workspaceLabel + "' supports neither SelectionItem nor Invoke, so the "
                    + "navigation pane cannot be operated by assistive technology either.");
        }
        catch (ElementNotAvailableException)
        {
            return ReviewStep.Failed("'" + workspaceLabel + "' disappeared while being selected.");
        }
        catch (InvalidOperationException failure)
        {
            return ReviewStep.Failed(failure.Message);
        }
    }

    /// <summary>Invokes a button by its accessible name.</summary>
    internal ReviewStep Invoke(string name)
    {
        AutomationElement? element = FindByName(name);

        if (element is null)
        {
            return ReviewStep.Failed("No control named '" + name + "'.");
        }

        try
        {
            if (!element.Current.IsEnabled)
            {
                return ReviewStep.Failed("'" + name + "' is present and disabled.");
            }

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern)
                && pattern is InvokePattern invoke)
            {
                invoke.Invoke();
                Thread.Sleep(700);

                return ReviewStep.Ok("Invoked '" + name + "'.");
            }

            return ReviewStep.Failed("'" + name + "' exposes no Invoke pattern.");
        }
        catch (ElementNotAvailableException)
        {
            return ReviewStep.Failed("'" + name + "' went away while being invoked.");
        }
    }

    /// <summary>Captures the window to a PNG.</summary>
    internal bool Capture(string path) => ScreenCapture.TryCapture(Handle, path);

    /// <summary>The element that currently holds keyboard focus, if any.</summary>
    internal UiaNode? FocusedNode()
    {
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;

            if (focused is null)
            {
                return null;
            }

            return UiaTree.Read(focused, maxDepth: 0);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Waits until a condition holds, or gives up and says so.</summary>
    internal bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(condition);

        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(150);
        }

        return false;
    }

    /// <summary>Re-reads the window element after a navigation replaced the frame content.</summary>
    internal void Refresh()
    {
        // A busy client is an unreachable provider, and that is not the same as
        // a closed one. Three attempts over three seconds, because the longest
        // unreachable stretch observed was a detail fetch.
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Window = AutomationElement.FromHandle(Handle);

                return;
            }
            catch (ElementNotAvailableException) when (attempt < 3 && IsRunning)
            {
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>Whether the application is still there at all.</summary>
    /// <remarks>
    /// Asked of the operating system rather than of the automation tree, so that
    /// "the window did not answer" and "the window is gone" stay distinct.
    /// </remarks>
    internal bool IsRunning
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
