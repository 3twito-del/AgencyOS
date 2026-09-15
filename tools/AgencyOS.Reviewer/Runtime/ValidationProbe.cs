using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using System.IO;
using AgencyOS.Reviewer.Surface;

namespace AgencyOS.Reviewer.Runtime;

/// <summary>
/// Submits a dialog that is not ready and records what the dialog says about it.
/// </summary>
/// <remarks>
/// <para>
/// Audit 002 §8 asks whether an error is <em>associated</em> with the control it
/// is about, whether attention moves to it, and whether the rejected entry
/// survives. Markup can answer the first question on its own — an association a
/// screen reader can follow has to be declared — but the other two only exist at
/// runtime, so they are observed here rather than inferred.
/// </para>
/// <para>
/// The probe deliberately submits an <em>empty</em> form first. That is the one
/// invalid state every dialog shares, so the same gesture is comparable across
/// all of them, and whether the primary button is even enabled in that state is
/// itself part of the answer.
/// </para>
/// </remarks>
internal sealed class ValidationProbe
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ReviewApp _app;
    private readonly DialogPass _pass;
    private readonly string _runDirectory;

    internal ValidationProbe(ReviewApp app, DialogPass pass, string runDirectory)
    {
        _app = app;
        _pass = pass;
        _runDirectory = runDirectory;
    }

    /// <summary>Opens one dialog and tries to submit it empty.</summary>
    /// <param name="dialog">The dialog to probe.</param>
    /// <returns>What the dialog did about an entry it could not accept.</returns>
    internal ValidationObservation Probe(DialogRecord dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        string directory = Path.Combine(
            _runDirectory, "evidence", "validation." + dialog.DialogId);

        Directory.CreateDirectory(directory);

        (bool opened, string how, string detail, UiaNode? modal) = _pass.OpenFor(dialog);

        if (!opened || modal is null)
        {
            return ValidationObservation.NotOpened(dialog.DialogId, how + ": " + detail);
        }

        UiaNode[] before = [.. modal.Flatten()];

        // What the dialog is showing before anything is submitted. An InfoBar that
        // is already open is guidance, not a complaint, and separating the two is
        // the whole point of taking this reading first.
        IReadOnlyList<string> textBefore = Detectors.Prose([.. before.Where(Detectors.Refusal)]);

        UiaNode? primary = Primary(modal);

        string primaryName = primary?.Name ?? "(none)";
        bool primaryEnabled = primary?.IsEnabled ?? false;

        _app.Capture(Path.Combine(directory, "01-empty.png"));

        File.WriteAllText(
            Path.Combine(directory, "01-empty.json"), JsonSerializer.Serialize(modal, Json));

        // The fields that could carry a wrong value.
        UiaNode[] inputs =
        [
            .. before.Where(x =>
                x.ControlType is "Edit" or "ComboBox" && x.IsKeyboardFocusable),
        ];

        // Longer than any name the domain accepts, so the server refuses it and
        // the dialog has something to report. A short value is not invalid input:
        // an earlier reading typed one, the server accepted it, and the dialog
        // closed because the work had been done.
        string sample = "review-"
            + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture)
            + new string('x', 600);

        bool typed = false;

        if (inputs.FirstOrDefault(x => x.ControlType == "Edit") is { Name: { } firstField })
        {
            typed = SetText(firstField, sample);
            Thread.Sleep(400);
            _app.Refresh();
        }

        // Submit with the form still short of what it needs.
        string submitted;
        bool stillOpen = true;
        IReadOnlyList<string> textAfter = textBefore;
        string? focusAfter = null;
        string? survived = null;
        bool editable = false;

        if (primaryEnabled && primary?.Name is { } label)
        {
            submitted = (typed ? "typed 607 characters into a name, then " : "nothing typed, then ")
                + (Invoke(label) ? "invoked " + label : "could not invoke " + label);

            Thread.Sleep(1600);
            _app.Refresh();

            UiaNode after = _app.Snapshot();
            UiaNode? stillThere = after.Flatten().FirstOrDefault(x =>
                x.ControlType == "Window" && x.ClassName == "Popup" && !x.IsOffscreen);

            stillOpen = stillThere is not null;

            // When the dialog is gone, what is left is the page, and the page's
            // standing prose is not a message about this submission. Keeping the
            // two apart is why the closed case is read separately.
            UiaNode[] nodes = [.. (stillThere ?? after).Flatten()];

            // Only refusals, from wherever they were put. The page's standing
            // prose is not news about this submission, and when the dialog is
            // gone the page is all that is left to read.
            textAfter = Detectors.Prose([.. nodes.Where(Detectors.Refusal)]);
            focusAfter = _app.FocusedNode()?.Describe();

            if (typed)
            {
                UiaNode? held = nodes.FirstOrDefault(x =>
                    x.ControlType == "Edit"
                    && x.Value is { } value
                    && value.Contains(sample, StringComparison.Ordinal));

                survived = held is null
                    ? "the typed text is gone"
                    : "the typed text is still there";

                editable = held is not null && held.IsEnabled && held.IsKeyboardFocusable;
            }

            _app.Capture(Path.Combine(directory, "02-submitted.png"));

            File.WriteAllText(
                Path.Combine(directory, "02-submitted.json"),
                JsonSerializer.Serialize(after, Json));
        }
        else
        {
            submitted = "the primary button is disabled while the form is empty";
        }

        string[] appeared = [.. textAfter.Except(textBefore, StringComparer.Ordinal)];

        // An association a screen reader can follow has to be declared. Nothing in
        // the tree can be read as one by standing near something else, so this is
        // asked of the automation properties rather than of the geometry.
        bool describedBy = before.Any(x => !string.IsNullOrWhiteSpace(x.HelpText));

        string verdict = Detectors.Validation(
            primaryEnabled, appeared.Length > 0, describedBy, stillOpen);

        // Said plainly, because "no message" and "nothing to report" look alike
        // in a count and mean opposite things.
        if (verdict == ValidationVerdict.Inconclusive && primaryEnabled)
        {
            submitted += "; nothing refused it, so there was no error to place";
        }

        _app.Keys.Press(ReviewKey.Escape);
        Thread.Sleep(500);

        return new ValidationObservation(
            dialog.DialogId,
            "OPENED",
            primaryName,
            primaryEnabled,
            inputs.Length,
            submitted,
            stillOpen,
            textBefore,
            appeared,
            focusAfter,
            survived,
            editable,
            describedBy,
            verdict);
    }

    private static UiaNode? Primary(UiaNode modal) =>
        modal.Flatten().FirstOrDefault(x =>
            x.ControlType == "Button" && x.AutomationId == "PrimaryButton")
        ?? modal.Flatten().FirstOrDefault(x =>
            x.ControlType == "Button"
            && !x.IsOffscreen
            && x.Name is not null
            && x.Name is not "Cancel" and not "Close");

    private bool SetText(string fieldName, string text)
    {
        try
        {
            AutomationElement? field = _app.FindByName(fieldName);

            if (field is null
                || !field.TryGetCurrentPattern(ValuePattern.Pattern, out object? pattern)
                || pattern is not ValuePattern value
                || value.Current.IsReadOnly)
            {
                return false;
            }

            value.SetValue(text);

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool Invoke(string label)
    {
        try
        {
            AutomationElement? button = _app.FindByName(label);

            if (button is null
                || !button.TryGetCurrentPattern(InvokePattern.Pattern, out object? pattern)
                || pattern is not InvokePattern invoke)
            {
                return false;
            }

            invoke.Invoke();

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>The verdicts Audit 002 §8 asks a validation observation to end in.</summary>
internal static class ValidationVerdict
{
    internal const string Associated = "ASSOCIATED";
    internal const string VisuallyNearOnly = "VISUALLY_NEAR_ONLY";
    internal const string Unassociated = "UNASSOCIATED";
    internal const string ManualGate = "MANUAL_GATE";
    internal const string Inconclusive = "INCONCLUSIVE";
}

/// <summary>What one dialog did about an entry it could not accept.</summary>
/// <param name="DialogId">Which dialog.</param>
/// <param name="Outcome">Whether it could be opened at all.</param>
/// <param name="PrimaryButton">The button that would commit it.</param>
/// <param name="PrimaryEnabledWhenEmpty">Whether that button was available on an empty form.</param>
/// <param name="Inputs">How many fields could carry a wrong value.</param>
/// <param name="Submitted">What the probe did.</param>
/// <param name="StillOpen">Whether the dialog survived the submission.</param>
/// <param name="TextBefore">The prose the dialog showed beforehand.</param>
/// <param name="MessagesThatAppeared">The prose that was not there before.</param>
/// <param name="FocusAfter">Where the keyboard ended up.</param>
/// <param name="TypedTextSurvived">Whether a rejected entry was still there.</param>
/// <param name="StillEditable">Whether it could still be corrected in place.</param>
/// <param name="AccessibleAssociation">Whether any declared association exists to follow.</param>
/// <param name="Verdict">The §8 classification.</param>
public sealed record ValidationObservation(
    string DialogId,
    string Outcome,
    string PrimaryButton,
    bool PrimaryEnabledWhenEmpty,
    int Inputs,
    string Submitted,
    bool StillOpen,
    IReadOnlyList<string> TextBefore,
    IReadOnlyList<string> MessagesThatAppeared,
    string? FocusAfter,
    string? TypedTextSurvived,
    bool StillEditable,
    bool AccessibleAssociation,
    string Verdict)
{
    /// <summary>A dialog the probe could not reach, said as such.</summary>
    /// <param name="dialogId">Which dialog.</param>
    /// <param name="why">What stopped it.</param>
    /// <returns>An observation that claims nothing about validation.</returns>
    public static ValidationObservation NotOpened(string dialogId, string why) =>
        new(dialogId, "NOT_OPENED", "(none)", false, 0, why, false, [], [], null, null,
            false, false, ValidationVerdict.Inconclusive);
}
