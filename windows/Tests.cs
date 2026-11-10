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

                // Windows' own spell checker. The CI machine may not have Arabic spelling installed.
                var spell = new Spelling("en-US");
                Check(spell.LatinLanguage != null, "spell checker: English available");
                Check(spell.IsCorrect("hello", false), "spell checker: hello is a word");
                Check(!spell.IsCorrect("lvpfh", false), "spell checker: lvpfh is not a word");
                if (spell.ArabicLanguage != null)
                {
                    Check(spell.IsCorrect("مرحبا", true), "spell checker: مرحبا is a word");
                    Check(!spell.IsCorrect("اثممخ", true), "spell checker: اثممخ is not a word");
                    var fix = AutoFixer.Decide(Keys("lvpfh", us.Map), us.Map, ar.Map, false, spell.IsCorrect);
                    Check(fix != null && fix.Text == "مرحبا", "real auto-fix مرحبا");
                }
                else Console.WriteLine("note: no Arabic spell checker on this machine");
            }

            RunAutoFixTests(latin, arabic);
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

        static List<Typed> Keys(string text, KeyMap map)
        {
            var keys = new List<Typed>();
            foreach (char c in text) keys.Add(c == ' ' ? Typed.Space() : Typed.Of(map.Reverse[c.ToString()]));
            return keys;
        }

        static void RunAutoFixTests(KeyMap latin, KeyMap arabic)
        {
            var english = new HashSet<string> { "hello", "world", "how", "are", "you", "the", "ok", "hi" };
            var arabicWords = new HashSet<string> { "مرحبا", "كيف", "الحال", "شكرا", "اه" };
            Func<string, bool, bool> isCorrect = delegate (string w, bool ar) { return (ar ? arabicWords : english).Contains(w); };

            AutoFix fix = AutoFixer.Decide(Keys("مرحبا", arabic), latin, arabic, false, isCorrect);
            Check(fix != null && fix.DeleteCount == 5 && fix.Text == "مرحبا", "auto: مرحبا");
            fix = AutoFixer.Decide(Keys("hello world", latin), arabic, latin, true, isCorrect);
            Check(fix != null && fix.DeleteCount == 11 && fix.Text == "hello world", "auto: hello world");
            fix = AutoFixer.Decide(Keys("how are you ", latin), arabic, latin, true, isCorrect);
            Check(fix != null && fix.Text == "how are you ", "auto: trailing space");
            Check(AutoFixer.Decide(Keys("hello", latin), latin, arabic, false, isCorrect) == null, "auto: leaves hello");
            Check(AutoFixer.Decide(Keys("مرحبا", arabic), arabic, latin, true, isCorrect) == null, "auto: leaves مرحبا");
            Check(AutoFixer.Decide(Keys("hi", latin), arabic, latin, true, isCorrect) == null, "auto: 2 letters too short");
            // English on the Arabic keyboard that happens to spell a real Arabic word still gets fixed.
            Func<string, bool, bool> lenient = delegate (string w, bool ar) { return ar || english.Contains(w); };
            fix = AutoFixer.Decide(Keys("how are you", latin), arabic, latin, true, lenient);
            Check(fix != null && fix.Text == "how are you", "auto: common English beats lenient Arabic");
            // Common Arabic typed right is never touched, even if its English letters look like a word.
            Func<string, bool, bool> everything = delegate (string w, bool ar) { return true; };
            Check(AutoFixer.Decide(Keys("في", arabic), arabic, latin, true, everything) == null, "auto: leaves في");
            // English shortcuts and capitals stay English even when their letters spell Arabic...
            foreach (string w in new[] { "tmr", "hmu", "lol", "brb", "ASAP", "OK" })
                Check(AutoFixer.Decide(Keys(w, latin), latin, arabic, false, lenient) == null, "auto: leaves shortcut " + w);
            // ...but inside an Arabic sentence typed on the English keyboard they are fixed with it.
            fix = AutoFixer.Decide(Keys("hkh tmr", latin), latin, arabic, false, lenient);
            Check(fix != null && fix.DeleteCount == 7, "auto: shortcut inside a wrong sentence is fixed");
            // Correct words first, then wrong ones: only the wrong ending is fixed.
            var mixed = Keys("مرحبا ", arabic);
            mixed.AddRange(Keys("how are you", latin));
            fix = AutoFixer.Decide(mixed, arabic, latin, true, isCorrect);
            Check(fix != null && fix.Text == "how are you" && fix.DeleteCount == 11, "auto: fixes only the wrong ending");
            Check(AutoFixer.NormalizeArabic("أكيد") == AutoFixer.NormalizeArabic("اكيد"), "normalize hamza");
            Check(AutoFixer.Decide(Keys("qzxj", latin), latin, arabic, false, isCorrect) == null, "auto: nonsense both ways");
            Check(AutoFixer.Decide(new List<Typed>(), latin, arabic, false, isCorrect) == null, "auto: nothing typed");
            // "b" types لا (2 characters) on Arabic 101: deleting must remove both.
            fix = AutoFixer.Decide(Keys("about", latin), arabic, latin, true, delegate (string w, bool ar) { return !ar && w == "about"; });
            Check(fix != null && fix.DeleteCount == 6 && fix.Text == "about", "auto: لا counts as 2 to delete");

            var buffer = new TypingBuffer();
            buffer.Add(Typed.Of(new KeyStroke(4, false)), "us");
            buffer.Add(Typed.Of(new KeyStroke(5, false)), "us");
            buffer.Backspace(delegate (KeyStroke k) { return 1; });
            Check(buffer.Items.Count == 1, "buffer: backspace");
            buffer.Backspace(delegate (KeyStroke k) { return 2; });
            Check(buffer.Items.Count == 0 && buffer.LayoutId == null, "buffer: backspace over لا starts over");
            buffer.Add(Typed.Space(), "us");
            buffer.Add(Typed.Space(), "ar");
            Check(buffer.Items.Count == 1 && buffer.LayoutId == "ar", "buffer: keyboard change starts over");
            for (int i = 0; i < 100; i++) { buffer.Add(Typed.Of(new KeyStroke(1, false)), "ar"); buffer.Add(Typed.Space(), "ar"); }
            Check(buffer.Items.Count <= TypingBuffer.MaxItems && buffer.Items.Count > 0, "buffer: stays small");
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
