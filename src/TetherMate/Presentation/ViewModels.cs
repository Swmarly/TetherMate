using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TetherMate.Presentation;

public sealed record ActivityEntry(DateTimeOffset Timestamp, string Message)
{
    public string Time => Timestamp.ToString("HH:mm:ss");
}

public sealed record DiagnosticItem(string Name, string Detail, string Tone);

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
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
