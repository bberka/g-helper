using GHelper.Input;
using Xunit;

namespace GHelper.Tests;

public class FoundationTests
{
    [Fact]
    public void KeysEnumKeepsWindowsVirtualKeyValues()
    {
        Assert.Equal(0x41, (int)Keys.A);
        Assert.Equal(0x70, (int)Keys.F1);
        Assert.Equal(0x0D, (int)Keys.Enter);
    }
}
