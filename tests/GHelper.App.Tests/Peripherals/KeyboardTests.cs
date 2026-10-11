using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GHelper.App.Views.Keyboard;
using GHelper.App.Views.Peripherals;
using GHelper.Peripherals.Keyboard;
using GHelper.Peripherals.Keyboard.Models;
using Xunit;

namespace GHelper.App.Tests.Peripherals;

public class KeyboardTests
{
    public KeyboardTests()
    {
        TestPlatform.Ensure();
    }

    private static KeyboardViewModel Create(AsusKeyboard kb) => new(kb, a => a(), a => a());

    private static StrixScopeII Scope() => new() { TestMode = true };

    [Fact]
    public void Builds_the_key_grid_from_the_layout()
    {
        var kb = Scope();
        var vm = Create(kb);

        Assert.True(vm.GridVisible);
        Assert.Equal("ROG Strix Scope II", vm.Title);
        Assert.True(vm.Tiles.Count > 80);
        Assert.True(vm.GridWidth > 500);

        // LED ids are consecutive grid positions, merged keys cover several
        KeyTile esc = vm.Tiles.First(t => t.Name == "Esc");
        Assert.Equal(0, esc.Led);
        Assert.Equal(AuraKeyboardLayouts.Keys["Esc"], esc.Usage);

        KeyTile space = vm.Tiles.First(t => t.Name == "Space");
        Assert.True(space.Span > 1);

        // keys without a binding code cannot be rebound
        Assert.Equal(vm.Tiles.Count + vm.Tiles.Sum(t => t.Span - 1), kb.LedCount() - kb.MediaKeyCount());

        Assert.NotEmpty(vm.BindingEntries);
        Assert.True(vm.BindingEntries[0].IsSeparator);
        Assert.Contains(vm.BindingEntries, e => e.Text == "Launch App 1");
    }

    [Fact]
    public void Direct_mode_shows_the_paint_row_and_painting_a_key_stores_the_colour()
    {
        var kb = Scope();
        var vm = Create(kb);

        int direct = Array.IndexOf(kb.SupportedLightingModes(), KeyboardLightingMode.Direct);
        vm.ModeIndex = direct;
        Assert.Equal(KeyboardLightingMode.Direct, kb.StoredMode);
        Assert.True(vm.PaintRowVisible);
        Assert.True(vm.PaintEnabled);
        Assert.False(vm.AnimatedPreview);

        vm.SetPaintColor(Color.FromRgb(0, 255, 0));
        KeyTile q = vm.Tiles.First(t => t.Name == "Q");
        vm.ClickKey(q);

        Assert.True(q.Selected);
        Assert.Equal(Color.FromRgb(0, 255, 0), q.BorderColor);
        Assert.Equal(vm.PaintColor, Color.FromRgb(0, 255, 0));
        Assert.Contains("Q", vm.SelectedKeyCaption);

        // fill all paints every key
        vm.FillAllCommand.Execute(null);
        Assert.All(vm.Tiles, t => Assert.Equal(Color.FromRgb(0, 255, 0), t.BorderColor));

        // closing persists the colours
        vm.Dispose();
        Assert.Equal(System.Drawing.Color.FromArgb(255, 0, 255, 0), kb.StoredKeyColors()[q.Led]);
    }

    [Fact]
    public void Aura_sync_disables_manual_controls_and_painting()
    {
        var kb = Scope();
        var vm = Create(kb);
        vm.ModeIndex = Array.IndexOf(kb.SupportedLightingModes(), KeyboardLightingMode.Direct);

        vm.SyncAura = true;
        Assert.False(vm.ManualEnabled);
        Assert.False(vm.PaintEnabled);

        KeyTile q = vm.Tiles.First(t => t.Name == "Q");
        Color before = q.BorderColor;
        vm.ClickKey(q);
        Assert.Equal(before, q.BorderColor);

        vm.SyncAura = false;
        Assert.True(vm.ManualEnabled);
    }

    [Fact]
    public void Colour_buttons_follow_the_mode_capabilities()
    {
        var kb = Scope();
        var vm = Create(kb);

        vm.ModeIndex = Array.IndexOf(kb.SupportedLightingModes(), KeyboardLightingMode.Static);
        Assert.True(vm.Color1Visible);
        Assert.False(vm.Color2Visible);

        vm.ModeIndex = Array.IndexOf(kb.SupportedLightingModes(), KeyboardLightingMode.Breathing);
        Assert.True(vm.Color1Visible);
        Assert.Equal(kb.SupportsColor2Setting(KeyboardLightingMode.Breathing), vm.Color2Visible);

        vm.SetColor(1, Color.FromRgb(9, 8, 7));
        Assert.Equal(System.Drawing.Color.FromArgb(255, 9, 8, 7), kb.StoredColor);
    }

    [Fact]
    public void Key_binding_is_written_and_marks_the_key()
    {
        var kb = Scope();
        var vm = Create(kb);

        KeyTile a = vm.Tiles.First(t => t.Name == "A");
        vm.SelectKey(a);
        Assert.True(vm.BindingComboEnabled);
        Assert.Equal(AuraKeyboardLayouts.Keys["A"], vm.SelectedBinding!.Code);

        BindingEntry target = vm.BindingEntries.First(e => !e.IsSeparator && e.Text == "Mute");
        vm.SelectedBinding = target;

        Assert.Equal(target.Code, kb.GetKeyBinding(a.Usage));
        Assert.True(a.Bound);

        vm.ResetBindingsCommand.Execute(null);
        Assert.Equal(0, kb.GetKeyBinding(a.Usage));
        Assert.False(a.Bound);
    }

    [Fact]
    public void Launch_slots_show_a_command_box()
    {
        var kb = Scope();
        var vm = Create(kb);

        KeyTile f = vm.Tiles.First(t => t.Name == "F");
        vm.SelectKey(f);
        Assert.False(vm.LaunchVisible);

        vm.SelectedBinding = vm.BindingEntries.First(e => e.Text == "Launch App 2");
        Assert.True(vm.LaunchVisible);

        vm.LaunchPath = "notepad";
        Assert.Equal("notepad", AsusKeyboard.LaunchCommand(1));
    }

    [Fact]
    public void Animated_modes_run_the_preview()
    {
        var kb = Scope();
        var vm = Create(kb);

        vm.ModeIndex = Array.IndexOf(kb.SupportedLightingModes(), KeyboardLightingMode.ColorCycle);
        Assert.True(vm.AnimatedPreview);

        Color start = vm.Tiles[0].BorderColor;
        vm.PreviewTick();
        vm.PreviewTick();
        Assert.NotEqual(start, vm.Tiles[0].BorderColor);
    }

    [Fact]
    public void Preview_maths_are_deterministic()
    {
        var preview = new KeyboardPreview(new Random(1));
        var centers = new (float, float)[] { (0.1f, 0.1f), (0.9f, 0.9f) };
        var output = new Color[2];

        preview.Tick(KeyboardLightingMode.Wave, GHelper.USB.AuraSpeed.Normal, Colors.Red, Colors.Black, Colors.Black, centers, output);
        Assert.NotEqual(output[0], output[1]);
        Assert.Equal(0.045f, preview.Phase, 3);

        preview.Tick(KeyboardLightingMode.Breathing, GHelper.USB.AuraSpeed.Fast, Colors.Red, Colors.Black, Colors.Black, centers, output);
        Assert.Equal(output[0], output[1]); // same colour everywhere

        Assert.Equal(Colors.Red, KeyboardPreview.Hsv(0));
        Assert.Equal(Color.FromRgb(0, 255, 0), KeyboardPreview.Hsv(1f / 3));
    }

    [Fact]
    public void Azoth_has_an_oled_section_and_writes_it()
    {
        var kb = new Azoth { TestMode = true };
        var vm = Create(kb);

        Assert.True(vm.OledVisible);
        Assert.Equal(1 + kb.OledAnimationCount() + 1, vm.OledModeItems.Count);

        vm.OledBrightness = 50;
        vm.CommitOledBrightness();
        Assert.Equal(50, kb.OledBrightness);

        vm.OledModeIndex = 2; // second animation
        Assert.True(kb.OledEnabled);
        Assert.Equal(1, kb.OledAnimation);

        vm.OledModeIndex = 0;
        Assert.False(kb.OledEnabled);
    }

    [AvaloniaFact]
    public void Renders_the_window_in_both_themes()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var kb = Scope();
        var window = new KeyboardWindow(kb);
        MainWindowSnapshotTests.Render(window, "keyboard-dark");
        Assert.Equal("ROG Strix Scope II", window.Title);

        var views = window.GetVisualDescendants().OfType<KeyTileView>().ToList();
        Assert.True(views.Count > 80);
        window.Close();

        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window = new KeyboardWindow(Scope());
        window.ViewModel!.ModeIndex = Array.IndexOf(kb.SupportedLightingModes(), KeyboardLightingMode.Direct);
        MainWindowSnapshotTests.Render(window, "keyboard-light-direct");
        window.Close();
    }

    [AvaloniaFact]
    public void Renders_azoth_with_oled()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new KeyboardWindow(new Azoth { TestMode = true });
        MainWindowSnapshotTests.Render(window, "keyboard-azoth-dark");
        window.Close();
    }
}
