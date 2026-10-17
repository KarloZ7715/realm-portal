using Avalonia;
using Avalonia.Controls;

namespace RealmPortal.App.Tests;

internal static class Res
{
    public static T Get<T>(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var value), $"resource {key} not found");
        return (T)value!;
    }
}
