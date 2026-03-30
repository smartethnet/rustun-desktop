using Microsoft.Windows.Storage;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Rustun.Helpers;

public partial class ApplicationDataSettingsProvider : ISettingsProvider
{
    private readonly ApplicationDataContainer container;

    public ApplicationDataSettingsProvider(ApplicationDataContainer container)
    {
        this.container = container ?? throw new ArgumentNullException(nameof(container));
    }

    public bool Contains(string key) => container.Values.ContainsKey(key);

    public object? Get(string key) => container.Values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, object value) => container.Values[key] = value;

    public T? Get<T>(string key)
    {
        if (!container.Values.TryGetValue(key, out var value))
            return default;

        if (value is T t)
            return t;

        if (value is string str && !IsSimpleType(typeof(T)))
        {
            try
            {
                var typeInfo = SettingsJsonContext.Default.GetTypeInfo(typeof(T));
                if (typeInfo is null)
                {
                    HandleCorruptedKey(key);
                    return default;
                }
                var deserialized = JsonSerializer.Deserialize(str, typeInfo);
                if (deserialized is T result)
                    return result;
                HandleCorruptedKey(key);
                return default;
            }
            catch (Exception)
            {
                HandleCorruptedKey(key);
                return default;
            }
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    private void HandleCorruptedKey(string key)
    {
        try
        {
            container.Values.Remove(key);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove corrupted key '{key}': {ex}");
        }
    }

    public void Set<T>(string key, T value)
    {
        if (IsSimpleType(typeof(T)))
        {
            container.Values[key] = value;
        }
        else
        {
            var typeInfo = SettingsJsonContext.Default.GetTypeInfo(typeof(T));
            if (typeInfo is null)
            {
                // 处理无法获取类型信息的情况，移除该键
                HandleCorruptedKey(key);
                return;
            }
            container.Values[key] = JsonSerializer.Serialize(value, typeInfo);
        }
    }

    private static readonly HashSet<Type> ExtraSimpleTypes = new()
    {
        typeof(string),
        typeof(DateTimeOffset),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(Windows.Foundation.Point),
        typeof(Windows.Foundation.Size),
        typeof(Windows.Foundation.Rect)
    };

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive || ExtraSimpleTypes.Contains(type);
    }
}
