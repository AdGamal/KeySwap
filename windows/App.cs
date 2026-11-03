// KeySwap for Windows: a tray app. Select text typed in the wrong language and double-tap Shift.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeySwap
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool firstInstance;
            using (var mutex = new Mutex(true, "KeySwap-single-instance", out firstInstance))
            {
                if (!firstInstance) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    class TrayApp : ApplicationContext
    {
        const string SettingsKey = @"Software\KeySwap";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly NotifyIcon tray = new NotifyIcon();
        readonly Control invoker = new Control();
        readonly DoubleShiftDetector detector = new DoubleShiftDetector();
        readonly KeyboardHook hook;
        bool busy;

        public TrayApp()
        {
            IntPtr unused = invoker.Handle; // create the handle so BeginInvoke works

            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "KeySwap: double-tap Shift to fix selected text";
            tray.ContextMenuStrip = new ContextMenuStrip();
            tray.ContextMenuStrip.Opening += delegate { BuildMenu(); };
            BuildMenu();
            tray.Visible = true;

            detector.OnTrigger = delegate
            {
                if (GetSetting("Enabled", true)) invoker.BeginInvoke(new MethodInvoker(FixSelection));
            };
            hook = new KeyboardHook(detector);
            hook.Start();

            if (!GetSetting("Welcomed", false))
            {
                SetSetting("Welcomed", true);
                tray.ShowBalloonTip(8000, "KeySwap is running",
                    "Select text typed in the wrong language, then double-tap Shift.", ToolTipIcon.Info);
            }
        }

        // MARK: Fixing the selected text

        void FixSelection()
        {
            if (busy) return;
            LayoutPair pair = Layouts.FindPair();
            if (pair == null) { ShowLayoutHelp(); return; }
            busy = true;
            try
            {
                DataObject saved = SaveClipboard();
                uint before = GetClipboardSequenceNumber();
                Input.SendCtrl(Input.VK_C);
                if (!WaitFor(delegate { return GetClipboardSequenceNumber() != before; }, 800))
                {
                    SystemSounds.Beep.Play(); // nothing was selected
                    return;
                }
                Thread.Sleep(30); // let the app finish writing all clipboard formats
                string text = GetClipboardText();
                if (string.IsNullOrEmpty(text))
                {
                    RestoreClipboard(saved);
                    SystemSounds.Beep.Play();
                    return;
                }
                ConversionResult result = new Converter(pair.Latin.Map, pair.Arabic.Map).Convert(text);
                if (!SetClipboardText(result.Text)) { RestoreClipboard(saved); return; }
                Input.SendCtrl(Input.VK_V);
                if (GetSetting("SwitchKeyboard", true)) Layouts.Activate(result.ToArabic ? pair.Arabic : pair.Latin);
                // Give the app time to read the pasted text before putting the user's clipboard back.
                Thread.Sleep(500);
                RestoreClipboard(saved);
            }
            catch (Exception)
            {
                SystemSounds.Beep.Play(); // never let one failed fix close the app
            }
            finally
            {
                busy = false;
            }
        }

        static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(15);
            }
            return condition();
        }

        static DataObject SaveClipboard()
        {
            try
            {
                IDataObject current = Clipboard.GetDataObject();
                if (current == null) return null;
                var copy = new DataObject();
                foreach (string format in current.GetFormats(false))
                {
                    try
                    {
                        object data = current.GetData(format, false);
                        if (data != null) copy.SetData(format, false, data);
                    }
                    catch (Exception) { } // some formats can't be read back; skip them
                }
                return copy;
            }
            catch (Exception) { return null; }
        }

        static void RestoreClipboard(DataObject saved)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (saved == null || saved.GetFormats(false).Length == 0) Clipboard.Clear();
                    else Clipboard.SetDataObject(saved, true);
                    return;
                }
                catch (ExternalException) { Thread.Sleep(30); } // clipboard busy: try again
                catch (Exception) { return; } // a format that can't be put back; leave the clipboard as is
            }
        }

        static string GetClipboardText()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                        ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
                }
                catch (ExternalException) { Thread.Sleep(30); }
            }
            return null;
        }

        static bool SetClipboardText(string text)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (text.Length == 0) Clipboard.Clear();
                    else Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(30); }
            }
            return false;
        }

        // MARK: Menu

        void BuildMenu()
        {
            ToolStripItemCollection items = tray.ContextMenuStrip.Items;
            items.Clear();
            var header = new ToolStripMenuItem("Double-tap Shift to fix selected text");
            header.Enabled = false;
            items.Add(header);
            items.Add(new ToolStripSeparator());
            items.Add(Toggle("Enabled", GetSetting("Enabled", true), delegate { SetSetting("Enabled", !GetSetting("Enabled", true)); }));
            items.Add(Toggle("Switch keyboard after fixing", GetSetting("SwitchKeyboard", true),
                delegate { SetSetting("SwitchKeyboard", !GetSetting("SwitchKeyboard", true)); }));
            items.Add(Toggle("Start with Windows", StartsWithWindows(), delegate { SetStartWithWindows(!StartsWithWindows()); }));
            items.Add(new ToolStripSeparator());
            LayoutPair pair = Layouts.FindPair();
            if (pair != null)
            {
                var info = new ToolStripMenuItem("Keyboards: " + pair.Latin.Name + " ⇄ " + pair.Arabic.Name);
                info.Enabled = false;
                items.Add(info);
            }
            else
            {
                items.Add(new ToolStripMenuItem("⚠ Add an Arabic and an English keyboard…", null,
                    delegate { ShowLayoutHelp(); }));
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Exit KeySwap", null, delegate { Quit(); }));
        }

        static ToolStripMenuItem Toggle(string title, bool on, EventHandler click)
        {
            var item = new ToolStripMenuItem(title, null, click);
            item.Checked = on;
            return item;
        }

        void ShowLayoutHelp()
        {
            MessageBox.Show("KeySwap converts between your Arabic keyboard and your English keyboard.\n\n" +
                "Open Settings → Time & language → Language & region, and add Arabic (with its keyboard) " +
                "next to English.", "KeySwap: add both keyboards", MessageBoxButtons.OK, MessageBoxIcon.Information);
            try { Process.Start("ms-settings:regionlanguage"); } catch (Exception) { }
        }

        void Quit()
        {
            hook.Stop();
            tray.Visible = false;
            tray.Dispose();
            ExitThread();
        }

        // MARK: Settings

        static bool GetSetting(string name, bool fallback)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKey))
            {
                object value = key == null ? null : key.GetValue(name);
                return value is int ? (int)value != 0 : fallback;
            }
        }

        static void SetSetting(string name, bool value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
        }

        static bool StartsWithWindows()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue("KeySwap") != null;
        }

        static void SetStartWithWindows(bool on)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue("KeySwap", "\"" + Application.ExecutablePath + "\"");
                else key.DeleteValue("KeySwap", false);
            }
        }

        [DllImport("user32.dll")]
        static extern uint GetClipboardSequenceNumber();
    }

    /// <summary>
    /// Watches Shift presses system-wide. Runs on its own thread so the hook never waits on the
    /// clipboard work, which would make Windows delay every key press.
    /// </summary>
    class KeyboardHook
    {
        readonly DoubleShiftDetector detector;
        readonly HookProc proc; // kept in a field so the garbage collector doesn't free the callback
        IntPtr handle;
        Thread thread;
        uint threadId;

        public KeyboardHook(DoubleShiftDetector detector)
        {
            this.detector = detector;
            proc = Callback;
        }

        public void Start()
        {
            thread = new Thread(delegate ()
            {
                threadId = GetCurrentThreadId();
                handle = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
                Application.Run(); // message loop for the hook
                if (handle != IntPtr.Zero) UnhookWindowsHookEx(handle);
            });
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop()
        {
            if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                bool injected = (data.flags & LLKHF_INJECTED) != 0;
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                if (!injected)
                {
                    if (data.vkCode == VK_LSHIFT || data.vkCode == VK_RSHIFT || data.vkCode == VK_SHIFT)
                    {
                        bool others = IsDown(VK_CONTROL) || IsDown(VK_MENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);
                        detector.Shift(down, others, data.time / 1000.0);
                    }
                    else if (down)
                    {
                        detector.OtherInput();
                    }
                }
            }
            return CallNextHookEx(handle, nCode, wParam, lParam);
        }

        static bool IsDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const uint WM_QUIT = 0x0012;
        const uint LLKHF_INJECTED = 0x10;
        const uint VK_SHIFT = 0x10;
        const uint VK_LSHIFT = 0xA0;
        const uint VK_RSHIFT = 0xA1;
        const int VK_CONTROL = 0x11;
        const int VK_MENU = 0x12;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();
    }

    /// <summary>Sends Ctrl+C / Ctrl+V to the app the user is typing in.</summary>
    static class Input
    {
        public const ushort VK_C = 0x43;
        public const ushort VK_V = 0x56;
        const ushort VK_CONTROL = 0x11;
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;

        public static void SendCtrl(ushort vk)
        {
            var inputs = new[]
            {
                Key(VK_CONTROL, false), Key(vk, false), Key(vk, true), Key(VK_CONTROL, true)
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        static INPUT Key(ushort vk, bool up)
        {
            var input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            return input;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        // Includes the mouse struct so the union has the size Windows expects on 32- and 64-bit.
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }
}
