using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProMapCargo.Mobile.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingField, value))
        {
            return false;
        }

        backingField = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    // Many view model methods continue on a background thread after
    // ConfigureAwait(false). Bound views must be updated on the main thread, or
    // Android throws / silently ignores the change, so the event is marshalled.
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var args = new PropertyChangedEventArgs(propertyName);

        if (MainThread.IsMainThread)
        {
            PropertyChanged?.Invoke(this, args);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => PropertyChanged?.Invoke(this, args));
        }
    }
}
