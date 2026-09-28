using System.IO;
using System.Text.Json.Serialization;
using CabiDock.Services;

namespace ToolKeeper.Services;

internal sealed class DesktopPreferences
{
    [JsonRequired]
    public bool DesktopEnabled { get; set; } = true;
}

internal sealed class DesktopPreferencesStore
{
    private readonly JsonFileStore<DesktopPreferences> _file;
    private bool _dirty;
    public DesktopPreferences Current { get; }
    public string? Warning { get; private set; }

    public DesktopPreferencesStore(string directory)
    {
        _file = new(Path.Combine(directory, "platform.json"));
        var result = _file.Load(() => new DesktopPreferences());
        Current = result.Value;
        Warning = result.Error;
        // Corrupt preferences do not silently turn desktop takeover back on.
        if (!result.CanSave) Current.DesktopEnabled = false;
    }

    public void SetEnabled(bool enabled)
    {
        Current.DesktopEnabled = enabled;
        _dirty = true;
        RetrySave();
    }

    public void RetrySave()
    {
        if (!_dirty) return;
        var result = _file.Save(Current);
        Warning = result.Error;
        if (result.Success) _dirty = false;
    }
}
