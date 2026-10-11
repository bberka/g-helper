using System.Text;

namespace GHelper.Platform.Linux;

internal static class Edid
{
    /// <summary>Monitor name descriptor (tag 0xFC) of an EDID block, null when absent.</summary>
    public static string? MonitorName(byte[]? edid)
    {
        if (edid is null || edid.Length < 128) return null;

        for (int offset = 54; offset <= 108; offset += 18)
        {
            // display descriptor: 00 00 00 <tag> 00 <13 bytes of data>
            if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == 0xFC)
            {
                string name = Encoding.ASCII.GetString(edid, offset + 5, 13);
                int end = name.IndexOf('\n');
                if (end >= 0) name = name[..end];
                return name.Trim();
            }
        }
        return null;
    }

    /// <summary>Three letter PNP manufacturer id (bytes 8-9).</summary>
    public static string? Manufacturer(byte[]? edid)
    {
        if (edid is null || edid.Length < 10) return null;
        int v = (edid[8] << 8) | edid[9];
        char Letter(int x) => (char)('A' + x - 1);
        return new string([Letter((v >> 10) & 0x1F), Letter((v >> 5) & 0x1F), Letter(v & 0x1F)]);
    }

    /// <summary>Heuristic: names Samsung (ATNA/ATNL), "OLED"/"AMOLED" panels use.</summary>
    public static bool LooksLikeOled(string? monitorName)
    {
        if (string.IsNullOrEmpty(monitorName)) return false;
        string n = monitorName.ToUpperInvariant();
        return n.Contains("OLED") || n.StartsWith("ATNA", StringComparison.Ordinal) || n.StartsWith("ATNL", StringComparison.Ordinal);
    }
}
