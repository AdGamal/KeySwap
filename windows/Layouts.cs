// Reads the keyboard layouts installed on this PC straight from Windows, so the mapping is exact.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace KeySwap
{
    public class SystemLayout
    {
        public IntPtr Hkl;
        public string Name;
        /// <summary>The keyboard's language, like "en-US".</summary>
        public string Culture;
        public string Id { get { return Layouts.IdOf(Hkl); } }
        public bool IsArabic;
        public KeyMap Map;
    }

    public class LayoutPair
    {
        public SystemLayout Latin;
        public SystemLayout Arabic;
    }

    public static class Layouts
    {
        /// <summary>Scan codes of the main typing keys (digits, letters, punctuation, and the extra ISO/JIS keys).</summary>
        static readonly int[] TypingScanCodes = BuildScanCodes();

        static int[] BuildScanCodes()
        {
            var codes = new List<int>();
            for (int sc = 0x02; sc <= 0x0D; sc++) codes.Add(sc);
            for (int sc = 0x10; sc <= 0x1B; sc++) codes.Add(sc);
            for (int sc = 0x1E; sc <= 0x29; sc++) codes.Add(sc);
            for (int sc = 0x2B; sc <= 0x35; sc++) codes.Add(sc);
            codes.Add(0x56);
            codes.Add(0x73);
            codes.Add(0x7D);
            return codes.ToArray();
        }

        public static string IdOf(IntPtr hkl) { return hkl.ToInt64().ToString("X"); }

        public static bool IsTypingScanCode(int sc) { return Array.IndexOf(TypingScanCodes, sc) >= 0; }

        public static List<SystemLayout> Enabled()
        {
            var result = new List<SystemLayout>();
            int count = GetKeyboardLayoutList(0, null);
            if (count <= 0) return result;
            var list = new IntPtr[count];
            count = GetKeyboardLayoutList(count, list);
            for (int i = 0; i < count; i++) result.Add(FromHkl(list[i]));
            return result;
        }

        /// <summary>Picks the Arabic layout and the Latin layout to convert between, from the enabled keyboards.</summary>
        public static LayoutPair FindPair()
        {
            SystemLayout arabic = null, latin = null;
            foreach (var layout in Enabled())
            {
                if (layout.IsArabic) { if (arabic == null) arabic = layout; }
                else if (latin == null) latin = layout;
            }
            if (arabic == null || latin == null) return null;
            var pair = new LayoutPair();
            pair.Latin = latin;
            pair.Arabic = arabic;
            return pair;
        }

        public static SystemLayout FromHkl(IntPtr hkl)
        {
            int langId = (int)(hkl.ToInt64() & 0xFFFF);
            var layout = new SystemLayout();
            layout.Hkl = hkl;
            layout.IsArabic = (langId & 0x3FF) == 0x01;
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(langId);
                layout.Name = culture.DisplayName;
                layout.Culture = culture.Name;
            }
            catch (ArgumentException)
            {
                layout.Name = "Keyboard " + langId.ToString("X4");
                layout.Culture = "en-US";
            }
            layout.Map = BuildMap(hkl, layout.Name);
            return layout;
        }

        static KeyMap BuildMap(IntPtr hkl, string name)
        {
            var entries = new List<KeyValuePair<KeyStroke, string>>();
            var state = new byte[256];
            var buffer = new StringBuilder(8);
            foreach (bool shift in new[] { false, true })
            {
                foreach (int sc in TypingScanCodes)
                {
                    uint vk = MapVirtualKeyEx((uint)sc, MAPVK_VSC_TO_VK, hkl);
                    if (vk == 0) continue;
                    Array.Clear(state, 0, state.Length);
                    if (shift) { state[VK_SHIFT] = 0x80; state[VK_LSHIFT] = 0x80; }
                    buffer.Length = 0;
                    int n = ToUnicodeEx(vk, (uint)sc, state, buffer, buffer.Capacity, DONT_CHANGE_KEYBOARD_STATE, hkl);
                    if (n < 0)
                    {
                        // A dead key (an accent waiting for the next key) types nothing on its own.
                        // Older Windows ignores the "don't change state" flag, so clear it by pressing Space.
                        ClearDeadKey(hkl);
                        continue;
                    }
                    if (n > 0) entries.Add(new KeyValuePair<KeyStroke, string>(new KeyStroke(sc, shift), buffer.ToString(0, n)));
                }
            }
            return new KeyMap(name, entries);
        }

        static void ClearDeadKey(IntPtr hkl)
        {
            var empty = new byte[256];
            var buffer = new StringBuilder(8);
            uint spaceScan = MapVirtualKeyEx(VK_SPACE, MAPVK_VK_TO_VSC, hkl);
            for (int i = 0; i < 2; i++)
                ToUnicodeEx(VK_SPACE, spaceScan, empty, buffer, buffer.Capacity, DONT_CHANGE_KEYBOARD_STATE, hkl);
        }

        /// <summary>Changes the keyboard of the window the user is typing in.</summary>
        public static void Activate(SystemLayout layout)
        {
            IntPtr window = GetForegroundWindow();
            if (window != IntPtr.Zero) PostMessage(window, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout.Hkl);
        }

        /// <summary>Loads a layout by its Windows ID (e.g. "00000401" for Arabic 101). Used by the CI tests.</summary>
        public static IntPtr Load(string klid) { return LoadKeyboardLayout(klid, KLF_NOTELLSHELL); }

        const uint MAPVK_VK_TO_VSC = 0;
        const uint MAPVK_VSC_TO_VK = 1;
        const uint DONT_CHANGE_KEYBOARD_STATE = 4;
        const uint VK_SPACE = 0x20;
        const int VK_SHIFT = 0x10;
        const int VK_LSHIFT = 0xA0;
        const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        const uint KLF_NOTELLSHELL = 0x80;

        [DllImport("user32.dll")]
        static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKeyEx(uint uCode, uint uMapType, IntPtr dwhkl);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);
    }
}
