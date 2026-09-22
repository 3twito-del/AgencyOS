using System;
using System.Globalization;
using AgencyOS.Client.ViewModels;
using AgencyOS.Contracts.Finance;
using Microsoft.UI.Xaml.Controls;

namespace AgencyOS.Windows.Dialogs;

/// <summary>
/// Records that an invoice was issued.
/// </summary>
/// <remarks>
/// <para>
/// Not every receivable gets one: a studio settling on the schedule the contract
/// sets may never be invoiced. So an invoice is an optional instrument pointing at
/// receivables rather than a stage between the obligation and the money, and
/// issuing it is the act of saying it went out (ADR-0023).
/// </para>
/// <para>
/// The number is the operator's. The domain refuses to issue without one, and this
/// asks for it rather than letting the refusal arrive after the fact.
/// </para>
/// </remarks>
public sealed partial class IssueInvoiceDialog : ContentDialog
{
    public IssueInvoiceDialog(InvoiceResponse invoice, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        InitializeComponent();

        HeadlineText.Text = invoice.Reference is { Length: > 0 } number
            ? number
            : invoice.ContractTitle;

        InvoiceText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{invoice.DebtorDisplayName} - {MoneyFormatting.Format(invoice.Total)} "
                + $"across {invoice.Lines.Count} line(s) on {invoice.ContractTitle}.");

        ReferenceBox.Text = invoice.Reference ?? string.Empty;

        // Today, because an invoice is normally recorded as issued on the day it
        // goes out and the domain refuses a future date outright. Changing it is
        // one interaction; retyping the common case every time is not.
        IssuedPicker.Date = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        UpdateReady();
    }

    public IssueInvoiceRequest ToRequest(int expectedVersion) =>
        new(
            IssuedPicker.Date is { } issued
                ? DateOnly.FromDateTime(issued.DateTime)
                : default,
            expectedVersion,
            Empty(ReferenceBox.Text));

    private void OnReferenceChanged(object sender, TextChangedEventArgs e) => UpdateReady();

    private void OnDateChanged(
        CalendarDatePicker sender,
        CalendarDatePickerDateChangedEventArgs args) => UpdateReady();

    /// <summary>
    /// Whether the request can be built, and nothing further.
    /// </summary>
    /// <remarks>
    /// Whether the invoice is still a draft, whether it has lines and whether the
    /// date is in the future are the server's to answer. Copying those here would
    /// put a second edition of each rule where it could drift.
    /// </remarks>
    private void UpdateReady()
    {
        if (ReferenceBox is null)
        {
            return;
        }

        IsPrimaryButtonEnabled =
            !string.IsNullOrWhiteSpace(ReferenceBox.Text) && IssuedPicker.Date is not null;
    }

    private static string? Empty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
