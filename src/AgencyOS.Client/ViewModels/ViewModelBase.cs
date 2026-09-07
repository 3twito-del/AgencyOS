using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AgencyOS.Client.ViewModels;

/// <summary>
/// Shared state every screen needs: loading, empty and error.
/// </summary>
/// <remarks>
/// These three are modelled explicitly rather than inferred from a null list,
/// because "still loading", "loaded and genuinely empty" and "failed" look
/// identical to a binding otherwise, and a user who cannot tell them apart will
/// assume the worst one.
/// </remarks>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    private bool _isLoading;
    private string? _errorMessage;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets a value indicating whether a request is in flight.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>Gets the last failure, or null when the last operation succeeded.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    /// <summary>
    /// Gets a value indicating whether the screen loaded successfully and has
    /// nothing to show.
    /// </summary>
    public abstract bool IsEmpty { get; }

    /// <summary>
    /// Runs an operation, keeping loading and error state honest.
    /// </summary>
    /// <remarks>
    /// A refusal by the server is a normal outcome here, not a crash: an
    /// unauthorized action or an out-of-date build both surface as the server's own
    /// explanation. Anything unrecognized is allowed to propagate rather than being
    /// flattened into a misleading message.
    /// </remarks>
    protected async Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            await operation(cancellationToken).ConfigureAwait(true);
        }
        catch (AgencyOsApiException ex)
        {
            ErrorMessage = ex.RequiresClientUpdate
                ? $"This build must be updated before it can make changes. {ex.Detail ?? ex.Message}"
                : ex.Detail ?? ex.Message;
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Cannot reach the AgencyOS server. {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            // A cancelled load is not a failure worth showing.
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
