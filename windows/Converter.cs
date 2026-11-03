// Pure conversion logic, the same algorithm as mac/Sources/Converter.swift.
// Written in C# 5 so it builds with the compiler that ships with Windows (.NET Framework 4).
using System;
using System.Collections.Generic;
using System.Text;

namespace KeySwap
{
    /// <summary>One physical key press: which key (scan code), and whether Shift was held.</summary>
    public struct KeyStroke : IEquatable<KeyStroke>
    {
        public readonly int Key;
        public readonly bool Shift;

        public KeyStroke(int key, bool shift) { Key = key; Shift = shift; }

        public bool Equals(KeyStroke other) { return Key == other.Key && Shift == other.Shift; }
        public override bool Equals(object obj) { return obj is KeyStroke && Equals((KeyStroke)obj); }
        public override int GetHashCode() { return Key * 2 + (Shift ? 1 : 0); }
    }

    /// <summary>What every key of one keyboard layout types.</summary>
    public class KeyMap
    {
        public readonly string Name;
        /// <summary>Keys in priority order (plain keys before shifted ones) so lookups prefer the simplest key.</summary>
        public readonly List<KeyStroke> Strokes = new List<KeyStroke>();
        public readonly Dictionary<KeyStroke, string> Output = new Dictionary<KeyStroke, string>();
        public readonly Dictionary<string, KeyStroke> Reverse = new Dictionary<string, KeyStroke>(StringComparer.Ordinal);
        public int MaxCodePoints = 1;

        public KeyMap(string name, IList<KeyValuePair<KeyStroke, string>> entries)
        {
            Name = name;
            var ordered = new List<KeyValuePair<KeyStroke, string>>();
            foreach (var e in entries) if (!e.Key.Shift) ordered.Add(e);
            foreach (var e in entries) if (e.Key.Shift) ordered.Add(e);
            foreach (var e in ordered)
            {
                if (!IsTypeable(e.Value) || Output.ContainsKey(e.Key)) continue;
                Strokes.Add(e.Key);
                Output[e.Key] = e.Value;
                if (!Reverse.ContainsKey(e.Value)) Reverse[e.Value] = e.Key;
                MaxCodePoints = Math.Max(MaxCodePoints, Text.CodePoints(e.Value).Count);
            }
        }

        /// <summary>Keeps printable characters only (no control codes or whitespace, which are the same on every layout).</summary>
        public static bool IsTypeable(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string cp in Text.CodePoints(text))
            {
                int v = Text.Value(cp);
                if (v < 0x20 || v == 0x7F || (v >= 0x80 && v <= 0x9F) || char.IsWhiteSpace(cp, 0)) return false;
            }
            return true;
        }
    }

    public static class Text
    {
        /// <summary>Splits a string into code points, keeping surrogate pairs (emoji) together.</summary>
        public static List<string> CodePoints(string s)
        {
            var list = new List<string>();
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    list.Add(s.Substring(i, 2));
                    i++;
                }
                else list.Add(s.Substring(i, 1));
            }
            return list;
        }

        /// <summary>The code point's number; a broken half of a surrogate pair is returned as is.</summary>
        public static int Value(string cp)
        {
            return cp.Length == 2 ? char.ConvertToUtf32(cp[0], cp[1]) : cp[0];
        }

        public static bool IsArabic(string cp)
        {
            int v = Text.Value(cp);
            return (v >= 0x0600 && v <= 0x06FF) || (v >= 0x0750 && v <= 0x077F) || (v >= 0x08A0 && v <= 0x08FF)
                || (v >= 0xFB50 && v <= 0xFDFF) || (v >= 0xFE70 && v <= 0xFEFF);
        }

        public static bool IsLatinLetter(string cp)
        {
            int v = Text.Value(cp);
            bool latinRange = (v >= 0x41 && v <= 0x5A) || (v >= 0x61 && v <= 0x7A) || (v >= 0xC0 && v <= 0x24F);
            return latinRange && char.IsLetter(cp, 0);
        }

        public static bool AllLatinLetters(string s)
        {
            if (s.Length == 0) return false;
            foreach (string cp in CodePoints(s)) if (!IsLatinLetter(cp)) return false;
            return true;
        }
    }

    public class ConversionResult
    {
        public string Text;
        /// <summary>true when the text was turned into Arabic, false when it was turned into the Latin layout.</summary>
        public bool ToArabic;
    }

    public class Converter
    {
        readonly KeyMap latin;
        readonly KeyMap arabic;

        public Converter(KeyMap latin, KeyMap arabic) { this.latin = latin; this.arabic = arabic; }

        /// <summary>Detects which layout the text was typed with and retypes it on the other one.</summary>
        public ConversionResult Convert(string text)
        {
            int arabicCount = 0, latinCount = 0;
            foreach (string cp in Text.CodePoints(text))
            {
                if (Text.IsArabic(cp)) arabicCount++;
                else if (Text.IsLatinLetter(cp)) latinCount++;
            }
            bool toArabic = latinCount >= arabicCount;
            var result = new ConversionResult();
            result.ToArabic = toArabic;
            result.Text = toArabic ? Retype(text, latin, arabic, false) : Retype(text, arabic, latin, true);
            return result;
        }

        class Piece
        {
            public string Primary;
            public string Alternative;
            public bool UseAlternative;
            public string Chosen { get { return UseAlternative ? Alternative : Primary; } }
        }

        /// <summary>
        /// Finds the keys that would type the text on the source layout, then types those keys on the target.
        /// Characters the source layout can't type (emoji, newlines, other scripts) are kept as they are.
        /// </summary>
        public static string Retype(string text, KeyMap source, KeyMap target, bool targetIsLatin)
        {
            List<string> cps = Text.CodePoints(text);
            var pieces = new List<Piece>();
            int i = 0;
            while (i < cps.Count)
            {
                bool matched = false;
                for (int len = Math.Min(source.MaxCodePoints, cps.Count - i); len >= 1; len--)
                {
                    string chunk = string.Concat(cps.GetRange(i, len).ToArray());
                    KeyStroke stroke;
                    if (!source.Reverse.TryGetValue(chunk, out stroke)) continue;
                    var piece = new Piece();
                    string typed;
                    piece.Primary = target.Output.TryGetValue(stroke, out typed) ? typed : chunk;
                    // A single key that types several characters (e.g. لا) could also have been typed as
                    // separate keys. Keep that reading too so the word check below can pick between them.
                    if (len > 1)
                    {
                        var alt = new StringBuilder();
                        bool all = true;
                        for (int k = i; k < i + len; k++)
                        {
                            KeyStroke part;
                            if (!source.Reverse.TryGetValue(cps[k], out part)) { all = false; break; }
                            string partTyped;
                            if (target.Output.TryGetValue(part, out partTyped)) alt.Append(partTyped);
                        }
                        if (all) piece.Alternative = alt.ToString();
                    }
                    pieces.Add(piece);
                    i += len;
                    matched = true;
                    break;
                }
                if (!matched)
                {
                    var keep = new Piece();
                    keep.Primary = cps[i];
                    pieces.Add(keep);
                    i++;
                }
            }
            if (targetIsLatin) ResolveEnglishAmbiguity(pieces);
            var sb = new StringBuilder();
            foreach (var p in pieces) sb.Append(p.Chosen);
            return sb.ToString();
        }

        /// <summary>
        /// On the Arabic (101) keyboard the B key types "لا", which is also what G then H type.
        /// "b" is the default; "gh" wins only when it forms a known English spelling (light, through...).
        /// </summary>
        static readonly string[] GhSpellings = {
            "ght", "high", "sigh", "thigh", "nigh", "through", "though", "thorough",
            "borough", "enough", "laugh", "tough", "rough", "cough", "trough", "dough",
            "plough", "weigh", "neigh", "sleigh", "ghost", "ghast", "ghetto", "ghoul",
            "spaghetti", "afghan", "yoghurt", "dinghy", "sorghum", "burgh" };
        static readonly HashSet<string> GhWords = new HashSet<string> { "ugh", "hugh" };

        static void ResolveEnglishAmbiguity(List<Piece> pieces)
        {
            for (int idx = 0; idx < pieces.Count; idx++)
            {
                if (pieces[idx].Alternative == null) continue;
                // Find the word around this piece: neighbouring pieces made only of letters.
                int start = idx, end = idx;
                while (start > 0 && Text.AllLatinLetters(pieces[start - 1].Chosen)) start--;
                while (end < pieces.Count - 1 && Text.AllLatinLetters(pieces[end + 1].Chosen)) end++;

                var before = new StringBuilder();
                for (int k = start; k < idx; k++) before.Append(pieces[k].Chosen);
                var after = new StringBuilder();
                for (int k = idx + 1; k <= end; k++) after.Append(pieces[k].Chosen);
                string b = before.ToString().ToLowerInvariant();
                string alt = pieces[idx].Alternative.ToLowerInvariant();
                string word = b + alt + after.ToString().ToLowerInvariant();
                int altStart = b.Length, altEnd = b.Length + alt.Length;

                bool keep = GhWords.Contains(word);
                foreach (string spelling in GhSpellings)
                {
                    if (keep) break;
                    for (int pos = word.IndexOf(spelling, StringComparison.Ordinal); pos >= 0;
                         pos = word.IndexOf(spelling, pos + 1, StringComparison.Ordinal))
                    {
                        if (pos <= altStart && pos + spelling.Length >= altEnd) { keep = true; break; }
                    }
                }
                if (keep) pieces[idx].UseAlternative = true;
            }
        }
    }

    /// <summary>Recognises Shift pressed and released twice quickly, with no other key in between.</summary>
    public class DoubleShiftDetector
    {
        public double MaxPress = 0.35;
        public double MaxGap = 0.40;
        public Action OnTrigger = delegate { };

        bool shiftDown;
        double shiftDownAt;
        int taps;
        double lastTapAt;

        public void Shift(bool down, bool otherModifiersHeld, double time)
        {
            if (otherModifiersHeld) { Reset(); return; }
            if (down)
            {
                if (shiftDown) return; // key repeat or the second Shift key
                if (taps > 0 && time - lastTapAt > MaxGap) taps = 0;
                shiftDown = true;
                shiftDownAt = time;
            }
            else
            {
                if (!shiftDown) return;
                shiftDown = false;
                if (time - shiftDownAt > MaxPress) { taps = 0; return; }
                taps++;
                lastTapAt = time;
                if (taps == 2)
                {
                    taps = 0;
                    OnTrigger();
                }
            }
        }

        /// <summary>Any other key cancels a tap in progress.</summary>
        public void OtherInput() { Reset(); }

        void Reset()
        {
            shiftDown = false;
            taps = 0;
        }
    }
}
