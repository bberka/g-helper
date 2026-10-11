using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using GHelper.App.Controls;

namespace GHelper.App.Views.Peripherals;

/// <summary>
/// Entry of a key / button binding drop down: either an action (legacy BindingItem) or a group heading
/// (legacy BindingSeparator, never selectable).
/// </summary>
public sealed class BindingEntry
{
    private BindingEntry(ushort code, string text, ushort[]? comboKeys, bool separator)
    {
        Code = code;
        Text = text;
        ComboKeys = comboKeys;
        IsSeparator = separator;
    }

    public static BindingEntry Item(ushort code, string name, ushort[]? comboKeys = null) => new(code, name, comboKeys, false);

    public static BindingEntry Separator(string label) => new(0, label, null, true);

    public ushort Code { get; }
    public string Text { get; }

    /// <summary>Key codes sent for a combo entry (keyboard window), null for plain actions.</summary>
    public ushort[]? ComboKeys { get; }

    public bool IsSeparator { get; }

    public override string ToString() => Text;
}

/// <summary>
/// Themed combo box (RComboBox) whose group headings are bold and cannot be picked - the legacy code stepped the
/// selection forward to the first real item of the group instead.
/// </summary>
public class BindingComboBox : RComboBox
{
    protected override Type StyleKeyOverride => typeof(RComboBox);

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);

        bool heading = item is BindingEntry { IsSeparator: true };
        container.IsEnabled = !heading;
        if (container is TemplatedControl tc)
            tc.FontWeight = heading ? FontWeight.Bold : FontWeight.Normal;
    }
}
