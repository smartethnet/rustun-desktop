using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rustun.Helpers;

public partial class ObservableSettings(ISettingsProvider provider) : INotifyPropertyChanged
{
    private readonly ISettingsProvider provider = provider;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(T value, [CallerMemberName] string? propertyName = null)
    {
        if (propertyName == null)
            throw new System.ArgumentNullException(nameof(value));

        if (provider.Contains(propertyName))
        {
            var currentValue = provider.Get<T>(propertyName);
            if (Equals(currentValue, value))
                return false;
        }

        provider.Set(propertyName, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected T Get<T>([CallerMemberName] string? propertyName = null)
    {
        if (propertyName == null)
            throw new System.ArgumentNullException(nameof(propertyName));

        var value = provider.Get<T>(propertyName);
        if (value is null)
            throw new System.InvalidOperationException($"Setting '{propertyName}' not found or is null.");

        return value;
    }

    protected T GetOrCreateDefault<T>(T defaultValue, [CallerMemberName] string? propertyName = null)
    {
        if (propertyName == null)
            throw new System.ArgumentNullException(nameof(propertyName));

        if (!provider.Contains(propertyName))
            Set(defaultValue, propertyName);

        return Get<T>(propertyName);
    }
}
