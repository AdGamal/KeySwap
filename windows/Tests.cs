// Tests for the converter and the double-tap detector. Run: build\Tests.exe   (add --real-layouts in CI)
using System;
using System.Collections.Generic;

namespace KeySwap
{
    static class Tests
    {
        static int passes, failures;

        static void Check(string got, string want, string label)
        {
            if (got == want) passes++;
            else { failures++; Console.WriteLine("FAIL " + label + ": got \"" + got + "\" want \"" + want + "\""); }
        }

        static void Check(bool ok, string label)
        {
            if (ok) passes++;
            else { failures++; Console.WriteLine("FAIL " + label); }
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Arabic (101) layout written out by hand: here the B key types "لا".
            string qwerty = "qwertyuiop[]asdfghjkl;'zxcvbnm,./";
            string[] arabic101 = { "ض", "ص", "ث", "ق", "ف", "غ", "ع", "ه", "خ", "ح", "ج", "د", "ش", "س", "ي", "ب", "ل", "ا", "ت",
                                   "ن", "م", "ك", "ط", "ئ", "ء", "ؤ", "ر", "لا", "ى", "ة", "و", "ز", "ظ" };
            var latinEntries = new List<KeyValuePair<KeyStroke, string>>();
            var arabicEntries = new List<KeyValuePair<KeyStroke, string>>();
            for (int i = 0; i < qwerty.Length; i++)
            {
                latinEntries.Add(new KeyValuePair<KeyStroke, string>(new KeyStroke(i, false), qwerty[i].ToString()));
                latinEntries.Add(new KeyValuePair<KeyStroke, string>(new KeyStroke(i, true), qwerty[i].ToString().ToUpperInvariant()));
                arabicEntries.Add(new KeyValuePair<KeyStroke, string>(new KeyStroke(i, false), arabic101[i]));
            }
            var latin = new KeyMap("US", latinEntries);
            var arabic = new KeyMap("Arabic 101", arabicEntries);
            RunConverterTests(latin, arabic, "hand-made");

            Check(new Converter(latin, arabic).Convert("hello world").ToArabic, "direction en→ar");
            Check(new Converter(latin, arabic).Convert("hi 👋\nthere").Text, "اه 👋\nفاثقث", "emoji and newline kept");
            Check(new Converter(latin, arabic).Convert("").Text, "", "empty");

            if (Array.IndexOf(args, "--real-layouts") >= 0)
            {
                // Loads the real Windows layouts. Only for CI: it adds them to the keyboard list.
                SystemLayout us = Layouts.FromHkl(Layouts.Load("00000409"));
                SystemLayout ar = Layouts.FromHkl(Layouts.Load("00000401"));
                Check(ar.IsArabic && !us.IsArabic, "real layouts detected");
                RunConverterTests(us.Map, ar.Map, "real Windows");
                var real = new Converter(us.Map, ar.Map);
                Check(real.Convert(real.Convert("Hello, how are you? 123").Text).Text, "Hello, how are you? 123",
                      "real Windows round trip sentence");
            }

            RunDetectorTests();
            Console.WriteLine(passes + " passed, " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void RunConverterTests(KeyMap latin, KeyMap arabic, string label)
        {
            var conv = new Converter(latin, arabic);
            Check(conv.Convert("hello world").Text, "اثممخ صخقمي", label + " en→ar");
            Check(conv.Convert("اثممخ صخقمي").Text, "hello world", label + " ar→en");
            Check(conv.Convert("lvpfh").Text, "مرحبا", label + " typed مرحبا on English");
            Check(conv.Convert("مرحبا").Text, "lvpfh", label + " ar→en word");
            string[] words = { "light", "about", "bought", "enough", "job", "neighbor", "high", "night", "bob", "club",
                               "web", "brightness", "through the night", "big bright lights", "thought", "laugh", "ghost", "ugh" };
            foreach (string word in words)
            {
                string typed = Converter.Retype(word, latin, arabic, false);
                Check(conv.Convert(typed).Text, word, label + " " + word);
            }
        }

        static int Taps(params object[] events)
        {
            var d = new DoubleShiftDetector();
            int fired = 0;
            d.OnTrigger = delegate { fired++; };
            for (int i = 0; i < events.Length; i += 2)
            {
                string e = (string)events[i];
                double t = (double)events[i + 1];
                if (e == "down") d.Shift(true, false, t);
                else if (e == "up") d.Shift(false, false, t);
                else if (e == "cmd") d.Shift(true, true, t);
                else d.OtherInput();
            }
            return fired;
        }

        static void RunDetectorTests()
        {
            Check(Taps("down", 0.0, "up", 0.08, "down", 0.2, "up", 0.28) == 1, "double tap fires");
            Check(Taps("down", 0.0, "up", 0.08, "down", 0.7, "up", 0.78) == 0, "too slow");
            Check(Taps("down", 0.0, "key", 0.05, "up", 0.08, "down", 0.2, "up", 0.28) == 0, "typing a capital");
            Check(Taps("down", 0.0, "up", 0.6, "down", 0.7, "up", 0.78) == 0, "held too long");
            Check(Taps("down", 0.0, "up", 0.08, "key", 0.1, "down", 0.2, "up", 0.28) == 0, "key between taps");
            Check(Taps("down", 0.0, "up", 0.08, "cmd", 0.15, "down", 0.2, "up", 0.28) == 0, "with Ctrl");
            Check(Taps("down", 0.0, "down", 0.03, "up", 0.08, "down", 0.2, "down", 0.22, "up", 0.28) == 1, "key repeat ignored");
            Check(Taps("down", 0.0, "up", 0.08, "down", 0.2, "up", 0.28, "down", 0.4, "up", 0.48) == 1, "triple is one");
        }
    }
}
