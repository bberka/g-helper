namespace GHelper.App.Views.Peripherals;

/// <summary>
/// The few Core / shell hooks the peripheral windows call (legacy: Program.settingsForm.X, Program.inputDispatcher,
/// MessageBox). Each is guarded because Program's static state needs registered platform services, which the headless
/// tests and the design time previewer do not have.
/// </summary>
public static class PeripheralHost
{
    /// <summary>Overridable for tests: receives (title, text) instead of the shell dialog.</summary>
    public static Action<string, string>? AlertOverride { get; set; }

    public static void Alert(string title, string text)
    {
        if (AlertOverride is { } over)
        {
            over(title, text);
            return;
        }

        try { Program.Ui.Alert(title, text); }
        catch (Exception ex) { Logger.WriteLine("Alert failed: " + ex.Message); }
    }

    /// <summary>Ask the shell to repaint an area ("peripherals", "keyboard" ...).</summary>
    public static void Refresh(string area)
    {
        try { Program.Ui.Refresh(area); }
        catch (Exception ex) { Logger.WriteLine("Refresh failed: " + ex.Message); }
    }

    /// <summary>Button bindings changed: the input dispatcher re-registers the hotkeys (UI thread).</summary>
    public static void RegisterKeys()
    {
        try { Program.inputDispatcher?.RegisterKeys(); }
        catch (Exception ex) { Logger.WriteLine("RegisterKeys failed: " + ex.Message); }
    }
}
