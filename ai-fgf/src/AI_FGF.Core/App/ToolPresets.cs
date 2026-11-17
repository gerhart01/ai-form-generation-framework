namespace AI_FGF;

/// <summary>
/// Saving and loading a form's field values as a JSON preset, with the outcome reported in the run log — the same on
/// every front end (the Avalonia form used to say nothing on success, and its messages differed from the WinForms
/// ones). No UI types: the fields are a <see cref="FieldSetBase{TSelf, TControl}"/>, the log a callback.
/// </summary>
public static class ToolPresets
{
    /// <summary>
    /// Write the field values to <paramref name="path"/>. A read-only, locked or unreachable target is reported in the
    /// log and returns false — never an exception out of a click handler.
    /// </summary>
    public static bool Save<TSet, TControl>(string path, FieldSetBase<TSet, TControl> fields, Action<string, RunLogTone> log)
        where TSet : FieldSetBase<TSet, TControl> where TControl : class
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            AiFgfSettingsStore.Save(path, new FormSettingsData { Fields = fields.GetPersistableValues() });   // never a password
            log($"Preset saved: {path}", RunLogTone.Success);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            log($"Could not save preset {path}: {ex.Message}", RunLogTone.Error);
            return false;
        }
    }

    /// <summary>Apply the values of a preset to the fields; false (with a log line) when it holds none.</summary>
    public static bool Load<TSet, TControl>(string path, FieldSetBase<TSet, TControl> fields, Action<string, RunLogTone> log)
        where TSet : FieldSetBase<TSet, TControl> where TControl : class
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(log);
        FormSettingsData data = AiFgfSettingsStore.Load(path);
        if (data.Fields.Count == 0)
        {
            log($"No field values in {path}", RunLogTone.Warning);
            return false;
        }
        fields.SetValues(data.Fields);
        log($"Preset loaded: {path}", RunLogTone.Success);
        return true;
    }
}
