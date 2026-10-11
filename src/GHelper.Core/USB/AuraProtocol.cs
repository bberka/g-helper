using System.Drawing;
using GHelper.Helpers;

namespace GHelper.USB
{
    /// <summary>
    /// Pure byte-level builders for the ASUS Aura HID protocol (report 0x5D / 0x5A). No device access, no config,
    /// so the packets can be verified in unit tests. <see cref="Aura"/> sends what these produce.
    /// </summary>
    public static class AuraProtocol
    {
        public static byte[] MessageApply => new byte[] { AsusHid.AURA_ID, 0xb4 };
        public static byte[] MessageSet => new byte[] { AsusHid.AURA_ID, 0xb5, 0, 0, 0 };

        /// <summary>Static / effect packet (0xB3).</summary>
        public static byte[] AuraMessage(AuraMode mode, Color color, Color color2, int speed, bool isWhite)
        {
            byte[] msg = new byte[17];
            msg[0] = AsusHid.AURA_ID;
            msg[1] = 0xB3;
            msg[2] = 0x00; // Zone
            msg[3] = (byte)mode; // Aura Mode
            msg[4] = color.R; // R
            msg[5] = isWhite ? (byte)0 : color.G; // G
            msg[6] = isWhite ? (byte)0 : color.B; // B
            msg[7] = (byte)speed; // aura.speed as u8;
            msg[8] = 0x00; // aura.direction as u8;
            msg[9] = (color.R == 0 && color.G == 0 && color.B == 0) ? (byte)0xFF : (mode == AuraMode.AuraBreathe ? (byte)0x01 : (byte)0x00); // random color flag
            msg[10] = color2.R; // R
            msg[11] = isWhite ? (byte)0 : color2.G; // G
            msg[12] = isWhite ? (byte)0 : color2.B; // B
            return msg;
        }

        /// <summary>Speed byte used by the 0xB3 packet.</summary>
        public static int SpeedByte(AuraSpeed speed) => speed == AuraSpeed.Normal ? 0xeb : speed == AuraSpeed.Fast ? 0xf5 : 0xe1;

        /// <summary>Power state packet (0xBD): which parts stay lit on boot / awake / sleep / shutdown.</summary>
        public static byte[] PowerMessage(AuraPower flags)
        {
            byte keyb = 0, bar = 0, lid = 0, rear = 0;

            if (flags.BootLogo) keyb |= 1 << 0;
            if (flags.BootKeyb) keyb |= 1 << 1;
            if (flags.AwakeLogo) keyb |= 1 << 2;
            if (flags.AwakeKeyb) keyb |= 1 << 3;
            if (flags.SleepLogo) keyb |= 1 << 4;
            if (flags.SleepKeyb) keyb |= 1 << 5;
            if (flags.ShutdownLogo) keyb |= 1 << 6;
            if (flags.ShutdownKeyb) keyb |= 1 << 7;

            if (flags.AwakeBar) bar |= 1 << 0;
            if (flags.BootBar) bar |= 1 << 1;
            if (flags.AwakeBar) bar |= 1 << 2;
            if (flags.SleepBar) bar |= 1 << 3;
            if (flags.ShutdownBar) bar |= 1 << 4;

            if (flags.BootLid) lid |= 1 << 0;
            if (flags.AwakeLid) lid |= 1 << 1;
            if (flags.SleepLid) lid |= 1 << 2;
            if (flags.ShutdownLid) lid |= 1 << 3;

            if (flags.BootLid) lid |= 1 << 4;
            if (flags.AwakeLid) lid |= 1 << 5;
            if (flags.SleepLid) lid |= 1 << 6;
            if (flags.ShutdownLid) lid |= 1 << 7;

            if (flags.BootRear) rear |= 1 << 0;
            if (flags.AwakeRear) rear |= 1 << 1;
            if (flags.SleepRear) rear |= 1 << 2;
            if (flags.ShutdownRear) rear |= 1 << 3;

            if (flags.BootRear) rear |= 1 << 4;
            if (flags.AwakeRear) rear |= 1 << 5;
            if (flags.SleepRear) rear |= 1 << 6;
            if (flags.ShutdownRear) rear |= 1 << 7;

            return new byte[] { AsusHid.AURA_ID, 0xBD, 0x01, keyb, bar, lid, rear, 0xFF };
        }

        /// <summary>ROG Ally keyboard power packet, sent on the input report (0x5A).</summary>
        public static byte[] AllyPowerMessage(AuraPower flags)
        {
            byte power = 0x00;
            if (flags.BootKeyb) power |= 0x01;
            if (flags.AwakeKeyb) power |= 0x02;
            if (flags.SleepKeyb) power |= 0x04;
            if (flags.ShutdownKeyb) power |= 0x08;
            return new byte[] { AsusHid.INPUT_ID, 0xD1, 0x09, 0x01, power };
        }


        public const byte KeySet = 167;
        public const byte LedCount = 178;
        public const byte LedsPerPacket = 16;
        public const byte RearCount = 39;

        /// <summary>
        /// Direct (0xBC) packets for an 8 zone colour array (0-3 keyboard, 4-7 lightbar) in send order: per key strix sends the
        /// key block in 16 LED chunks followed by the lightbar/tail block, 4 zone boards send one packet. All packets are 64 bytes.
        /// </summary>
        public static List<byte[]> DirectPackets(Color[] color, bool strix4Zone, bool numpad, bool flipped, bool rearStrip = false)
        {
            var result = new List<byte[]>();

            const ushort mapSize = 3 * LedCount;

            byte[] buffer = new byte[64];
            byte[] keyBuf = new byte[mapSize];

            buffer[0] = AsusHid.AURA_ID;
            buffer[1] = 0xBC;
            buffer[2] = 0;
            buffer[3] = 1;
            buffer[4] = 1;
            buffer[5] = 1;
            buffer[6] = 0;
            buffer[7] = 0x10;

            if (!strix4Zone) // per key
            {
                for (int ledIndex = 0; ledIndex < packetMap.Length; ledIndex++)
                {
                    ushort offset = (ushort)(3 * packetMap[ledIndex]);
                    byte zone = numpad ? packetZoneNumpad[ledIndex] : packetZone[ledIndex];

                    keyBuf[offset] = color[zone].R;
                    keyBuf[offset + 1] = color[zone].G;
                    keyBuf[offset + 2] = color[zone].B;
                }

                for (int i = 0; i < KeySet; i += LedsPerPacket)
                {
                    byte ledsRemaining = (byte)(KeySet - i);

                    if (ledsRemaining < LedsPerPacket)
                    {
                        buffer[7] = ledsRemaining;
                    }

                    buffer[6] = (byte)i;
                    Buffer.BlockCopy(keyBuf, 3 * i, buffer, 9, 3 * buffer[7]);
                    result.Add((byte[])buffer.Clone());
                }
            }

            buffer[4] = 0x04;
            buffer[5] = 0x00;
            buffer[6] = 0x00;
            buffer[7] = 0x00;

            if (strix4Zone)
            { // per zone
                var map = flipped ? packet4ZoneFlipped : packet4Zone;
                var leds_4_zone = map.Length;
                for (int ledIndex = 0; ledIndex < leds_4_zone; ledIndex++)
                {
                    byte zone = map[ledIndex];
                    keyBuf[ledIndex * 3] = color[zone].R;
                    keyBuf[ledIndex * 3 + 1] = color[zone].G;
                    keyBuf[ledIndex * 3 + 2] = color[zone].B;
                }
                Buffer.BlockCopy(keyBuf, 0, buffer, 9, 3 * leds_4_zone);
                result.Add((byte[])buffer.Clone());
                return result;
            }

            Buffer.BlockCopy(keyBuf, 3 * KeySet, buffer, 9, 3 * (LedCount - KeySet));
            result.Add((byte[])buffer.Clone());

            if (!rearStrip) return result;

            // rear strip (39 LEDs, upstream #6055): the four zone colours interpolated along the strip
            buffer[4] = 0x05;

            for (int i = 0; i < RearCount; i += LedsPerPacket)
            {
                byte leds = (byte)Math.Min(LedsPerPacket, RearCount - i);

                buffer[6] = (byte)(i + 1);
                buffer[7] = leds;

                for (int n = 0; n < leds; n++)
                {
                    float f = (i + n) / (float)(RearCount - 1) * 3;
                    int z = Math.Min(2, (int)f);
                    Color c = ColorUtils.GetWeightedAverage(color[z], color[z + 1], f - z);

                    buffer[9 + n * 3] = c.R;
                    buffer[10 + n * 3] = c.G;
                    buffer[11 + n * 3] = c.B;
                }

                Array.Clear(buffer, 9 + leds * 3, buffer.Length - 9 - leds * 3);
                result.Add((byte[])buffer.Clone());
            }

            return result;
        }

        /// <summary>Lightbar-only direct packet (zone test).</summary>
        public static byte[] LightbarPacket(Color[] color, bool flipped)
        {
            var map = flipped ? packet4ZoneFlipped : packet4Zone;
            byte[] buffer = new byte[64];
            buffer[0] = AsusHid.AURA_ID;
            buffer[1] = 0xBC;
            buffer[2] = 0;
            buffer[3] = 1;
            buffer[4] = 0x04;

            for (int i = 0; i < map.Length; i++)
            {
                byte zone = map[i];
                int o = 9 + i * 3;
                buffer[o] = color[zone].R;
                buffer[o + 1] = color[zone].G;
                buffer[o + 2] = color[zone].B;
            }

            return buffer;
        }

        /// <summary>Direct packet for single zone boards.</summary>
        public static byte[] DirectSingleColorPacket(Color color)
        {
            byte[] buffer = new byte[12];
            buffer[0] = AsusHid.AURA_ID;
            buffer[1] = 0xbc;
            buffer[2] = 1;
            buffer[3] = 1;
            buffer[9] = color.R;
            buffer[10] = color.G;
            buffer[11] = color.B;
            return buffer;
        }

        static readonly byte[] packetMap = new byte[]
        {
                    /* VDN   VUP   MICM  HPFN  ARMC  */
                         2,    3,    4,    5,    6,
        /* ESC          F1    F2    F3    F4    F5    F6    F7    F8    F9   F10   F11   F12              DEL15 DEL17  PAUS  PRT   HOME  */
            21,         23,   24,   25,   26,   28,   29,   30,   31,   33,   34,   35,   36,               37,   38,   39,   40,   41,
        /* BKTK    1     2     3     4     5     6     7     8     9     0     -     =   BSPC  BSPC  BSPC PLY15  NMLK  NMDV  NMTM  NMMI  */
            42,   43,   44,   45,   46,   47,   48,   49,   50,   51,   52,   53,   54,   55,   56,   57,   58,   59,   60,   61,   62,
        /* TAB     Q     W     E     R     T     Y     U     I     O     P     [     ]     \              STP15  NM7   NM8   NM9   NMPL  */
            63,   64,   65,   66,   67,   68,   69,   70,   71,   72,   73,   74,   75,   76,               79,   80,   81,   82,   83,
        /* CPLK    A     S     D     F     G     H     J     K     L     ;     "     #   ENTR  ENTR  ENTR PRV15  NM4   NM5   NM6   NMPL  */
            84,   85,   86,   87,   88,   89,   90,   91,   92,   93,   94,   95,   96,   97,   98,   99,  100,  101,  102,  103,  104,
        /* LSFT  ISO\    Z     X     C     V     B     N     M     ,     .     /   RSFT  RSFT  RSFT  ARWU NXT15  NM1   NM2   NM3   NMER  */
           105,  106,  107,  108,  109,  110,  111,  112,  113,  114,  115,  116,  117,  118,  119,  139,  121,  122,  123,  124,  125,
        /* LCTL  LFNC  LWIN  LALT              SPC               RALT  RFNC  RCTL        ARWL  ARWD  ARWR PRT15        NM0   NMPD  NMER  */
           126,  127,  128,  129,              131,              135,  136,  137,        159,  160,  161,  142,        144,  145,  146,
        /* LB1   LB2   LB3                                                               ARW?  ARWL? ARWD? ARWR?       LB4   LB5   LB6   */
           174,  173,  172,                                                              120,  140,  141,  143,        171,  170,  169,
        /* KSTN  LOGO  LIDL  LIDR  */
             0,  167,  176,  177,

        };


        static readonly byte[] packetZone = new byte[]
        {
                    /* VDN   VUP   MICM  HPFN  ARMC  */
                         0,    0,    1,    1,    1,
        /* ESC          F1    F2    F3    F4    F5    F6    F7    F8    F9   F10   F11   F12              DEL15 DEL17  PAUS  PRT   HOM   */
             0,          0,    0,    1,    1,    1,    1,    2,    2,    2,    2,    3,    3,                3,   3,    3,    3,    3,
        /* BKTK    1     2     3     4     5     6     7     8     9     0     -     =   BSPC  BSPC  BSPC PLY15  NMLK  NMDV  NMTM  NMMI  */
             0,    0,    0,    0,    1,    1,    1,    1,    2,    2,    2,    2,    3,    3,    3,    3,    3,    3,    3,    3,    3,
        /* TAB     Q     W     E     R     T     Y     U     I     O     P     [     ]     \              STP15  NM7   NM8   NM9   NMPL  */
             0,    0,    0,    0,    1,    1,    1,    1,    2,    2,    2,    2,    3,    3,                3,    3,    3,    3,    3,
        /* CPLK    A     S     D     F     G     H     J     K     L     ;     "     #   ENTR  ENTR  ENTR PRV15  NM4   NM5   NM6   NMPL  */
             0,    0,    0,    0,    1,    1,    1,    1,    2,    2,    2,    2,    3,    3,    3,    3,    3,    3,    3,    3,    3,
        /* LSFT  ISO\    Z     X     C     V     B     N     M     ,     .     /   RSFT  RSFT  RSFT  ARWU NXT15  NM1   NM2   NM3   NMER  */
             0,    0,    0,    0,    1,    1,    1,    1,    2,    2,    2,    2,    3,    3,    3,    3,    3,    3,    3,    3,    3,
        /* LCTL  LFNC  LWIN  LALT              SPC               RALT  RFNC  RCTL        ARWL  ARWD  ARWR PRT15        NM0   NMPD  NMER  */
             0,    0,    0,    0,              1,                  2,    2,    2,          3,    3,    3,    3,          3,    3,    3,
        /* LB1   LB1   LB3                                                               ARW?  ARW?  ARW?  ARW?        LB4   LB5   LB6   */
             5,    5,    4,                                                                3,    3,    3,    3,          6,    7,    7,
        /* KSTN  LOGO  LIDL  LIDR  */
             3,    0,    0,    3,

        };


        static readonly byte[] packetZoneNumpad = new byte[]
        {
                    /* VDN   VUP   MICM  HPFN  ARMC  */
                         0,    0,    0,    1,    1,
        /* ESC          F1    F2    F3    F4    F5    F6    F7    F8    F9   F10   F11   F12              DEL15 DEL17  PAUS  PRT   HOM   */
             0,          0,    0,    0,    1,    1,    1,    1,    1,    2,    2,    2,    2,                3,   3,    3,    3,    3,
        /* BKTK    1     2     3     4     5     6     7     8     9     0     -     =   BSPC  BSPC  BSPC PLY15  NMLK  NMDV  NMTM  NMMI  */
             0,    0,    0,    0,    0,    1,    1,    1,    1,    1,    2,    2,    2,    2,    2,    2,    3,    3,    3,    3,    3,
        /* TAB     Q     W     E     R     T     Y     U     I     O     P     [     ]     \              STP15  NM7   NM8   NM9   NMPL  */
             0,    0,    0,    0,    0,    1,    1,    1,    1,    1,    2,    2,    2,    2,                3,    3,    3,    3,    3,
        /* CPLK    A     S     D     F     G     H     J     K     L     ;     "     #   ENTR  ENTR  ENTR PRV15  NM4   NM5   NM6   NMPL  */
             0,    0,    0,    0,    0,    1,    1,    1,    1,    1,    2,    2,    2,    2,    2,    2,    3,    3,    3,    3,    3,
        /* LSFT  ISO\    Z     X     C     V     B     N     M     ,     .     /   RSFT  RSFT  RSFT  ARWU NXT15  NM1   NM2   NM3   NMER  */
             0,    0,    0,    0,    0,    1,    1,    1,    1,    1,    2,    2,    2,    2,    2,     2,   3,    3,    3,    3,    3,
        /* LCTL  LFNC  LWIN  LALT              SPC               RALT  RFNC  RCTL        ARWL  ARWD  ARWR PRT15        NM0   NMPD  NMER  */
             0,    0,    0,    0,              1,                  1,    2,    2,          2,    2,    2,    3,          3,    3,    3,
        /* LB1   LB1   LB3                                                               ARW?  ARW?  ARW?  ARW?        LB4   LB5   LB6   */
             5,    5,    4,                                                                2,    2,    2,    3,          6,    7,    7,
        /* KSTN  LOGO  LIDL  LIDR  */
             3,    0,    0,    3,

        };

        static readonly byte[] packet4Zone = new byte[]
        {
/*01        Z1  Z2  Z3  Z4  NA  NA  KeyZone */
            0,  1,  2,  3,  0,  0,

/*02        R1  R2  R3  L3  L2  L1  LightBar (wire ascending = R->L physical, matches OpenRGB Value 169..174) */
            7,  7,  6,  5,  4,  4,

        };

        static readonly byte[] packet4ZoneFlipped = new byte[]
        {
/*01        Z1  Z2  Z3  Z4  NA  NA  KeyZone */
            0,  1,  2,  3,  0,  0,

/*02        L1  L2  L3  R3  R2  R1  LightBar (wire ascending = L->R physical, G513 quirk) */
            4,  4,  5,  6,  7,  7,

        };
    }
}
