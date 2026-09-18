using System;
using System.Collections.Generic;
using System.Linq;
using AgencyOS.Client;
using AgencyOS.Client.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Shows a refusal inside the dialog that caused it, on the control it is about.
/// </summary>
/// <remarks>
/// <para>
/// <c>AOS-R002-010</c> decided that a recoverable refusal keeps the dialog, its
/// values and its context. <c>AOS-R002-011</c> is the other half: a message about a
/// field has to be reachable *from* that field by an assistive technology, not only
/// visible beside it.
/// </para>
/// <para>
/// One object rather than three copies of the same code-behind. The rules it
/// follows — what is recoverable, and which field a sentence is about — live in
/// <see cref="DialogRefusal"/> in the client assembly, where they are tested.
/// </para>
/// </remarks>
internal sealed class RefusalSurface
{
    private readonly InfoBar _bar;
    private readonly IReadOnlyDictionary<string, Control> _fields;

    private Control? _associated;

    /// <param name="bar">The dialog's own message area.</param>
    /// <param name="fields">
    /// The controls this dialog has, keyed by the name the server uses for them.
    /// The key is the contract's field name, because that is what a refusal says.
    /// </param>
    internal RefusalSurface(InfoBar bar, IReadOnlyDictionary<string, Control> fields)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(fields);

        _bar = bar;
        _fields = fields;
    }

    /// <summary>
    /// Shows a refusal, and says whether the dialog should stay open.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the operator can correct this here, which is when
    /// the caller cancels the close. <see langword="false"/> for a refusal nothing in
    /// this dialog can fix, which the page reports instead.
    /// </returns>
    internal bool Show(AgencyOsApiException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        RefusalPresentation presentation = DialogRefusal.Present(
            failure.StatusCode, failure.Detail, failure.Message, _fields.Keys.ToArray());

        if (!presentation.KeepsDialogOpen)
        {
            return false;
        }

        Detach();

        _bar.Message = presentation.Message;
        _bar.IsOpen = true;

        if (presentation.Field is { } named
            && _fields.TryGetValue(named, out Control? control))
        {
            // Two links, because they say different things: DescribedBy is how the
            // message is found from the field, and moving focus is how somebody
            // arrives at the field at all rather than at the button they pressed.
            AutomationProperties.GetDescribedBy(control).Add(_bar);

            _associated = control;
            control.Focus(FocusState.Programmatic);
        }

        return true;
    }

    /// <summary>Takes the message down once the entry it was about has changed.</summary>
    /// <remarks>
    /// An association that outlives its reason is worse than none: a screen reader
    /// would keep reading a complaint about a value the operator has already fixed.
    /// </remarks>
    internal void Clear()
    {
        Detach();

        _bar.IsOpen = false;
        _bar.Message = string.Empty;
    }

    /// <summary>The control the current message is pinned to, if any.</summary>
    internal Control? Associated => _associated;

    private void Detach()
    {
        if (_associated is null)
        {
            return;
        }

        AutomationProperties.GetDescribedBy(_associated).Remove(_bar);
        _associated = null;
    }
}
