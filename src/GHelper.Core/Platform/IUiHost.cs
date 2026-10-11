namespace GHelper.Platform;

/// <summary>
/// Everything Core needs from the UI layer. Replaces the legacy direct calls to Program.settingsForm / Program.toast /
/// MessageBox / Invoke. Implemented by GHelper.App (Avalonia). Core owns this contract: extend it additively.
/// </summary>
public interface IUiHost
{
    /// <summary>Run on the UI thread (legacy Control.Invoke / BeginInvoke).</summary>
    void Post(Action action);

    /// <summary>Small transient OSD message (legacy ToastForm.RunToast). iconKey names an app image resource, e.g. "eco".</summary>
    void Toast(string text, string? iconKey = null);

    /// <summary>Modal message box. Returns true for Yes/OK.</summary>
    bool Confirm(string title, string text);
    void Alert(string title, string text);

    /// <summary>Show / hide the main settings window (legacy Program.SettingsToggle).</summary>
    void ToggleSettings(bool checkForFocus = true, bool trayClick = false);

    /// <summary>
    /// Ask the UI to re-read model state and repaint a named area, e.g. "mode", "gpu", "screen", "battery", "keyboard",
    /// "fnlock", "sensors", "armoury", "matrix", "icon", "theme". Legacy code called settingsForm.VisualiseXxx().
    /// Cheap, may be called from any thread, coalesced by the UI.
    /// </summary>
    void Refresh(string area);

    /// <summary>Exit the application (legacy Application.Exit).</summary>
    void Quit();

    /// <summary>
    /// Open a secondary window by name (legacy settingsForm.FansToggle / extraForm.Show / ...):
    /// "fans" (arg = tab: 0 cpu, 1 gpu, 2 undervolt), "extra" (arg = 1: services page), "updates", "matrix", "handheld".
    /// Default implementation ignores the request (added by W1-A core-engine; additive).
    /// </summary>
    void ShowWindow(string name, int arg = 0) { }
}

public sealed class NullUiHost : IUiHost
{
    public static readonly NullUiHost Instance = new();

    public void Post(Action action) => action();
    public void Toast(string text, string? iconKey = null) { }
    public bool Confirm(string title, string text) => false;
    public void Alert(string title, string text) { }
    public void ToggleSettings(bool checkForFocus = true, bool trayClick = false) { }
    public void Refresh(string area) { }
    public void Quit() { }
}
