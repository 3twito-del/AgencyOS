using System.Globalization;
using System.Windows;
using System.Windows.Automation;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>One node of a running window's automation tree.</summary>
/// <param name="AutomationId">The stable identifier, when the control has one.</param>
/// <param name="Name">The accessible name a screen reader would announce.</param>
/// <param name="ControlType">The control type, as UI Automation reports it.</param>
/// <param name="ClassName">The implementation class, for correlating with markup.</param>
/// <param name="IsEnabled">Whether the control accepts input.</param>
/// <param name="IsOffscreen">Whether it is scrolled or clipped out of view.</param>
/// <param name="IsKeyboardFocusable">Whether the keyboard can reach it.</param>
/// <param name="HasKeyboardFocus">Whether it has focus right now.</param>
/// <param name="AcceleratorKey">The accelerator the control advertises.</param>
/// <param name="AccessKey">The access key the control advertises.</param>
/// <param name="HelpText">Supplementary description.</param>
/// <param name="Bounds">Screen rectangle, as left,top,width,height.</param>
/// <param name="Patterns">Automation patterns the control supports.</param>
/// <param name="Value">Current value, for controls that have one.</param>
/// <param name="Children">Child nodes, in tree order.</param>
public sealed record UiaNode(
    string? AutomationId,
    string? Name,
    string ControlType,
    string? ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsKeyboardFocusable,
    bool HasKeyboardFocus,
    string? AcceleratorKey,
    string? AccessKey,
    string? HelpText,
    string? Bounds,
    IReadOnlyList<string> Patterns,
    string? Value,
    IReadOnlyList<UiaNode> Children)
{
    /// <summary>Every node in the subtree, this one first.</summary>
    public IEnumerable<UiaNode> Flatten()
    {
        yield return this;

        foreach (UiaNode child in Children)
        {
            foreach (UiaNode descendant in child.Flatten())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>How a finding cites this node.</summary>
    public string Describe()
    {
        string identity = AutomationId is { Length: > 0 }
            ? AutomationId
            : Name is { Length: > 0 } ? "\"" + Name + "\"" : "«unnamed»";

        return string.Create(CultureInfo.InvariantCulture, $"{ControlType} {identity}");
    }
}

/// <summary>
/// Reads a running window's automation tree.
/// </summary>
/// <remarks>
/// <para>
/// Uses the managed UI Automation client, which is the same interface a screen
/// reader uses. That matters for the accessibility pass: a defect found here is a
/// defect assistive technology would meet, not an inference from markup.
/// </para>
/// <para>
/// Depth and breadth are bounded. A WinUI list with thousands of realized items
/// would otherwise produce a tree nobody can read and a traversal that outlives
/// the audit.
/// </para>
/// </remarks>
internal static class UiaTree
{
    /// <summary>Reads the tree below an element.</summary>
    internal static UiaNode Read(AutomationElement element, int maxDepth = 14, int maxChildren = 60)
    {
        ArgumentNullException.ThrowIfNull(element);

        return Read(element, 0, maxDepth, maxChildren);
    }

    private static UiaNode Read(AutomationElement element, int depth, int maxDepth, int maxChildren)
    {
        List<UiaNode> children = [];

        if (depth < maxDepth)
        {
            try
            {
                AutomationElementCollection found = element.FindAll(
                    TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);

                int taken = 0;

                foreach (AutomationElement? child in found)
                {
                    if (child is null || taken >= maxChildren)
                    {
                        break;
                    }

                    taken++;
                    children.Add(Read(child, depth + 1, maxDepth, maxChildren));
                }
            }
            catch (ElementNotAvailableException)
            {
                // The element went away mid-walk. An incomplete subtree is a
                // truthful answer; inventing one would not be.
            }
            catch (COMExceptionWrapper)
            {
                // Same.
            }
        }

        return new UiaNode(
            Text(element, AutomationElement.AutomationIdProperty),
            Text(element, AutomationElement.NameProperty),
            ControlTypeName(element),
            Text(element, AutomationElement.ClassNameProperty),
            Flag(element, AutomationElement.IsEnabledProperty, true),
            Flag(element, AutomationElement.IsOffscreenProperty, false),
            Flag(element, AutomationElement.IsKeyboardFocusableProperty, false),
            Flag(element, AutomationElement.HasKeyboardFocusProperty, false),
            Text(element, AutomationElement.AcceleratorKeyProperty),
            Text(element, AutomationElement.AccessKeyProperty),
            Text(element, AutomationElement.HelpTextProperty),
            BoundsOf(element),
            Patterns(element),
            ValueOf(element),
            children);
    }

    private static string? Text(AutomationElement element, AutomationProperty property)
    {
        try
        {
            object? value = element.GetCurrentPropertyValue(property, true);

            if (value is null || value == AutomationElement.NotSupported)
            {
                return null;
            }

            string text = value.ToString() ?? string.Empty;

            return text.Length == 0 ? null : text;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static bool Flag(AutomationElement element, AutomationProperty property, bool fallback)
    {
        try
        {
            object? value = element.GetCurrentPropertyValue(property, true);

            return value is bool flag ? flag : fallback;
        }
        catch (ElementNotAvailableException)
        {
            return fallback;
        }
    }

    private static string ControlTypeName(AutomationElement element)
    {
        try
        {
            return element.Current.ControlType?.ProgrammaticName?.Replace(
                "ControlType.", string.Empty, StringComparison.Ordinal) ?? "Unknown";
        }
        catch (ElementNotAvailableException)
        {
            return "Unavailable";
        }
    }

    private static string? BoundsOf(AutomationElement element)
    {
        try
        {
            Rect rect = element.Current.BoundingRectangle;

            if (double.IsInfinity(rect.Width) || rect.IsEmpty)
            {
                return null;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{rect.Left:F0},{rect.Top:F0},{rect.Width:F0},{rect.Height:F0}");
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// The short names of the patterns a control supports.
    /// </summary>
    /// <remarks>
    /// UI Automation names a pattern <c>InvokePatternIdentifiers.Pattern</c>, and
    /// the whole harness asks questions like "does this support Invoke". Run 001
    /// trimmed the wrong suffix, so every such question answered no: three of the
    /// five accessibility checks and both clipping checks could not fire, and
    /// their zeroes were evidence of nothing. <see cref="ShortName"/> is the
    /// single place that trimming happens, and ScannerTests pins it.
    /// </remarks>
    private static IReadOnlyList<string> Patterns(AutomationElement element)
    {
        try
        {
            return
            [
                .. element
                    .GetSupportedPatterns()
                    .Select(x => ShortName(x.ProgrammaticName))
                    .OrderBy(x => x, StringComparer.Ordinal),
            ];
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }
    }

    /// <summary>Turns a pattern's programmatic name into the name a reader uses.</summary>
    /// <param name="programmaticName">The name UI Automation reports.</param>
    /// <returns>The pattern's short name, such as <c>Invoke</c>.</returns>
    internal static string ShortName(string programmaticName)
    {
        ArgumentNullException.ThrowIfNull(programmaticName);

        const string Suffix = "PatternIdentifiers.Pattern";

        return programmaticName.EndsWith(Suffix, StringComparison.Ordinal)
            ? programmaticName[..^Suffix.Length]
            : programmaticName;
    }

    private static string? ValueOf(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern)
                && pattern is ValuePattern value)
            {
                string current = value.Current.Value ?? string.Empty;

                return current.Length == 0 ? null : current;
            }

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? selection)
                && selection is SelectionItemPattern item)
            {
                return item.Current.IsSelected
                    ? "selected"
                    : "not selected";
            }

            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out object? toggle)
                && toggle is TogglePattern toggled)
            {
                return toggled.Current.ToggleState.ToString();
            }

            return null;
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
}

/// <summary>A COM failure during a tree walk, named so the catch reads clearly.</summary>
internal sealed class COMExceptionWrapper : Exception
{
    public COMExceptionWrapper()
    {
    }

    public COMExceptionWrapper(string message)
        : base(message)
    {
    }

    public COMExceptionWrapper(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
