using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Windows.Storage.Pickers;

namespace AgencyOS.Windows;

/// <summary>
/// Choosing files, through the shell rather than through a text box.
/// </summary>
/// <remarks>
/// <para>
/// The Windows App SDK pickers are used rather than the older Windows.Storage ones
/// because this client ships unpackaged, and the older pickers need a window handle
/// bolted on through interop to work at all. These take a window id and behave the
/// same packaged or not (docs/12_WINDOWS_NATIVE.md).
/// </para>
/// <para>
/// The path a picker returns is a path on the operator's own machine. It is never
/// sent to the server as a location: the server stores bytes under keys it chooses
/// itself, and no AgencyOS response contains a filesystem path on either side
/// (ADR-0024).
/// </para>
/// </remarks>
internal static class FilePicking
{
    /// <summary>Asks the user for one or more files.</summary>
    /// <returns>Full paths, or an empty list when the user cancelled.</returns>
    public static async Task<IReadOnlyList<string>> PickFilesAsync(bool allowMultiple = false)
    {
        if (App.Window is not { } window)
        {
            return [];
        }

        FileOpenPicker picker = new(window.AppWindow.Id)
        {
            CommitButtonText = "Choose",
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        picker.FileTypeFilter.Add("*");

        if (allowMultiple)
        {
            IReadOnlyList<PickFileResult> results = await picker
                .PickMultipleFilesAsync()
                .AsTask()
                .ConfigureAwait(true);

            List<string> paths = [];

            foreach (PickFileResult result in results)
            {
                if (result?.Path is { Length: > 0 } path)
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        PickFileResult? single = await picker.PickSingleFileAsync().AsTask().ConfigureAwait(true);

        return single?.Path is { Length: > 0 } chosen ? [chosen] : [];
    }

    /// <summary>Asks the user where to save a file the server is about to stream back.</summary>
    public static async Task<string?> PickSaveAsync(string suggestedName)
    {
        if (App.Window is not { } window)
        {
            return null;
        }

        FileSavePicker picker = new(window.AppWindow.Id)
        {
            CommitButtonText = "Save",
            SuggestedFileName = suggestedName,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        PickFileResult? result = await picker.PickSaveFileAsync().AsTask().ConfigureAwait(true);

        return result?.Path;
    }
}
