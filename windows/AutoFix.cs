// Fixing while typing, the same logic as mac/Sources/AutoFix.swift: remembers the keys pressed since
// the last pause and decides whether they were meant for the other keyboard.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KeySwap
{
    public struct Typed : IEquatable<Typed>
    {
        public readonly bool IsSpace;
        public readonly KeyStroke Key;

        Typed(bool isSpace, KeyStroke key) { IsSpace = isSpace; Key = key; }

        public static Typed Space() { return new Typed(true, new KeyStroke(0, false)); }
        public static Typed Of(KeyStroke key) { return new Typed(false, key); }

        public bool Equals(Typed other) { return IsSpace == other.IsSpace && (IsSpace || Key.Equals(other.Key)); }
        public override bool Equals(object obj) { return obj is Typed && Equals((Typed)obj); }
        public override int GetHashCode() { return IsSpace ? -1 : Key.GetHashCode(); }
    }

    public class AutoFix
    {
        /// <summary>How many characters to delete with Backspace.</summary>
        public int DeleteCount;
        /// <summary>What to type instead.</summary>
        public string Text;
    }

    /// <summary>The keys typed since the last click, Enter, shortcut or window switch, all on one layout.</summary>
    public class TypingBuffer
    {
        public const int MaxItems = 80;

        public readonly List<Typed> Items = new List<Typed>();
        public string LayoutId { get; private set; }

        public void Add(Typed item, string layoutId)
        {
            if (layoutId != LayoutId)
            {
                Items.Clear();
                LayoutId = layoutId;
            }
            Items.Add(item);
            if (Items.Count > MaxItems)
            {
                // Drop the oldest word so we only ever look at the most recent text.
                int space = Items.IndexOf(Typed.Space());
                if (space >= 0) Items.RemoveRange(0, space + 1); else Items.Clear();
            }
        }

        /// <summary>
        /// Backspace removes the last key, unless that key typed several characters (like لا): then one
        /// Backspace only removes part of it, so we can no longer tell what's on screen and start over.
        /// </summary>
        public void Backspace(Func<KeyStroke, int> charactersTypedBy)
        {
            if (Items.Count == 0) return;
            Typed last = Items[Items.Count - 1];
            if (!last.IsSpace && charactersTypedBy(last.Key) != 1) { Reset(); return; }
            Items.RemoveAt(Items.Count - 1);
        }

        public void Reset()
        {
            Items.Clear();
            LayoutId = null;
        }
    }

    public static class AutoFixer
    {
        /// <summary>
        /// Decides whether the last words typed were meant for the other keyboard. isCorrect(word, arabic)
        /// asks the spell checker about one word. The same rule as mac/Sources/AutoFix.swift:
        ///
        /// Each word is judged twice: does it look right as typed, and does it look right on the other
        /// keyboard? Spell checkers, the Arabic ones most of all, accept many letter mixes, so:
        /// - a common word as typed always looks right, and a common word on the other keyboard is proof;
        /// - an English word of 4 letters or more against Arabic that is merely accepted is "weak proof":
        ///   it counts only next to real proof ("turn off the lights": "the" is common, so "turn" counts);
        /// - English shortcuts and capitals always look right as English;
        /// - anything with punctuation inside is not English (Arabic letters sit on punctuation keys).
        ///
        /// Only the trailing words are fixed, starting at the first word that looks wrong as typed. In that
        /// run there must be no word that is right as typed and wrong on the other keyboard, at least 3
        /// letters of proof words (wrong as typed, right on the other keyboard), and at least as many proof
        /// and weak-proof words as other words that are right both ways, and as words that are wrong both
        /// ways (names, typos), which are then fixed along with the rest. The run then grows backwards over
        /// words that are right both ways but not common as typed ("pls" before "send me the file").
        ///
        /// A last word with no space after it may still be half-typed: on the English keyboard, if it
        /// starts like an English word ("hel", "tomo") it proves nothing and is never fixed on its own,
        /// unless the other keyboard gives a common Arabic word.
        /// </summary>
        public static AutoFix Decide(IList<Typed> typed, KeyMap current, KeyMap other, bool currentIsArabic,
                                     Func<string, bool, bool> isCorrect)
        {
            // Per word, what each key types on both keyboards.
            var words = new List<List<KeyValuePair<string, string>>> { new List<KeyValuePair<string, string>>() };
            foreach (Typed item in typed)
            {
                if (item.IsSpace)
                {
                    words.Add(new List<KeyValuePair<string, string>>());
                    continue;
                }
                string a, b;
                if (!current.Output.TryGetValue(item.Key, out a) || !other.Output.TryGetValue(item.Key, out b)) return null;
                words[words.Count - 1].Add(new KeyValuePair<string, string>(a, b));
            }
            int n = words.Count;
            var judged = new Judgement[n];
            for (int i = 0; i < n; i++) judged[i] = Judge(words[i], currentIsArabic, isCorrect);
            if (!currentIsArabic && typed.Count > 0 && !typed[typed.Count - 1].IsSpace && !judged[n - 1].MeantIsCommon
                && CouldBeUnfinishedEnglish(judged[n - 1].Typed ?? ""))
                judged[n - 1].Unfinished = true;

            for (int first = 0; first < n; first++)
            {
                if (!judged[first].Checked || judged[first].TypedLooksRight || judged[first].Unfinished) continue;
                int proof = 0, proofLetters = 0, rightBothWays = 0, wrongBothWays = 0;
                bool contradicted = false;
                for (int i = first; i < n; i++)
                {
                    Judgement j = judged[i];
                    if (!j.Checked || j.Unfinished) continue;
                    if (!j.TypedLooksRight && j.MeantIsRight) { proof++; proofLetters += j.Letters; }
                    else if (j.TypedLooksRight && j.MeantIsRight) { if (j.WeakProof) proof++; else rightBothWays++; }
                    else if (!j.TypedLooksRight) wrongBothWays++;
                    else contradicted = true;
                }
                if (contradicted || proofLetters < 3 || proof < rightBothWays || proof < wrongBothWays) continue;
                int start = first;
                while (start > 0 && judged[start - 1].Checked && judged[start - 1].MeantIsRight && !judged[start - 1].TypedIsCommon)
                    start--;

                var asTyped = new StringBuilder();
                var meant = new StringBuilder();
                for (int i = start; i < n; i++)
                {
                    if (i > start) { asTyped.Append(' '); meant.Append(' '); }
                    foreach (KeyValuePair<string, string> key in words[i]) { asTyped.Append(key.Key); meant.Append(key.Value); }
                }
                string typedText = asTyped.ToString();
                // Backspace counts are only reliable for plain characters (no vowel marks or emoji).
                foreach (char c in typedText)
                    if (char.IsSurrogate(c) || IsCombiningMark(c)) return null;
                var fix = new AutoFix();
                fix.DeleteCount = typedText.Length;
                fix.Text = meant.ToString();
                return fix;
            }
            return null;
        }

        struct Judgement
        {
            public bool Checked;
            public string Typed;
            public bool MeantIsRight;
            public bool MeantIsCommon;
            public bool TypedLooksRight;
            public bool TypedIsCommon;
            public bool WeakProof;
            public bool Unfinished;
            public int Letters;
        }

        static bool HasLetter(string text)
        {
            foreach (char c in text) if (char.IsLetter(c) || IsCombiningMark(c)) return true;
            return false;
        }

        static Judgement Judge(List<KeyValuePair<string, string>> keys, bool currentIsArabic, Func<string, bool, bool> isCorrect)
        {
            var j = new Judgement();
            // The word without the punctuation around it: "(hello!" → "hello". A key counts as part of the
            // word if it types a letter on either keyboard, so ";glm" stays whole (it is كلمة).
            int first = -1, last = -1;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!HasLetter(keys[i].Key) && !HasLetter(keys[i].Value)) continue;
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0) return j;
            var typedBuilder = new StringBuilder();
            var meantBuilder = new StringBuilder();
            for (int i = first; i <= last; i++) { typedBuilder.Append(keys[i].Key); meantBuilder.Append(keys[i].Value); }
            string typed = typedBuilder.ToString(), meant = meantBuilder.ToString();
            j.Typed = typed;
            if (!HasLetter(meant)) return j; // 5% → ٥٪

            j.Checked = true;
            foreach (char c in meant) if (char.IsLetter(c)) j.Letters++;
            j.MeantIsCommon = IsCommon(meant, !currentIsArabic);
            j.MeantIsRight = j.MeantIsCommon || LooksRight(meant, !currentIsArabic, isCorrect);
            j.TypedIsCommon = IsCommon(typed, currentIsArabic);
            j.TypedLooksRight = j.TypedIsCommon || (!j.MeantIsCommon && LooksRight(typed, currentIsArabic, isCorrect));
            // The Arabic checker accepts so much that a real English word of 4+ letters outweighs it, but
            // only as support: real Arabic can spell English too.
            j.WeakProof = currentIsArabic && !j.TypedIsCommon && j.TypedLooksRight && j.Letters >= 4 && isCorrect(meant, false);
            return j;
        }

        /// <summary>One word in one language, by more than the spell checker alone.</summary>
        static bool LooksRight(string word, bool arabic, Func<string, bool, bool> isCorrect)
        {
            if (!HasLetter(word)) return false;
            if (arabic)
            {
                // Shift on the Arabic keyboard types vowel marks, so English capitals become marks in odd
                // places. A word doesn't start with one.
                if (IsCombiningMark(word[0])) return false;
                return isCorrect(word, true);
            }
            // Arabic letters live on English punctuation keys (ج is [, ك is ;, ز is .): "h.d;" is not English,
            // even though a spell checker sees two fine one-letter words in it.
            foreach (char c in word)
                if (!char.IsLetter(c) && c != '\'' && c != '’' && c != '-') return false;
            if (LooksLikeEnglishShortcut(word) || isCorrect(word, false)) return true;
            // Names, days and months are only right with a capital: monday, cairo.
            return word == word.ToLowerInvariant()
                && isCorrect(char.ToUpperInvariant(word[0]) + word.Substring(1), false);
        }

        static readonly HashSet<string> Prefixes = new HashSet<string>(CommonWords.EnglishPrefixes, StringComparer.Ordinal);

        /// <summary>A word that may still be in the middle of being typed: it starts the way English words start.</summary>
        public static bool CouldBeUnfinishedEnglish(string typed)
        {
            string word = typed.ToLowerInvariant().Replace("'", "");
            foreach (char c in word) if (c < 'a' || c > 'z') return false;
            return word.Length < 3 || Prefixes.Contains(word.Substring(0, 3));
        }

        static readonly HashSet<string> CommonEnglish = new HashSet<string>(CommonWords.English, StringComparer.Ordinal);
        static readonly HashSet<string> CommonArabic = BuildArabic();

        static HashSet<string> BuildArabic()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string w in CommonWords.Arabic) set.Add(NormalizeArabic(w));
            return set;
        }

        static readonly HashSet<string> Shortcuts = new HashSet<string>(CommonWords.EnglishShortcuts, StringComparer.Ordinal);

        /// <summary>
        /// Chat shortcuts (lol, brb, tmr...) and words in capitals (OK, ASAP) are English on purpose, even
        /// when the spell checker doesn't know them and their letters happen to spell Arabic.
        /// </summary>
        public static bool LooksLikeEnglishShortcut(string word)
        {
            if (Shortcuts.Contains(word.ToLowerInvariant())) return true;
            int letters = 0;
            foreach (char c in word)
            {
                if (!char.IsLetter(c)) continue;
                if (c < 'A' || c > 'Z') return false;
                letters++;
            }
            return letters >= 2;
        }

        public static bool IsCommon(string word, bool arabic)
        {
            return arabic ? CommonArabic.Contains(NormalizeArabic(word)) : CommonEnglish.Contains(word.ToLowerInvariant());
        }

        /// <summary>Spellings people use interchangeably: أ/إ/آ → ا, ى → ي, ة → ه, and no vowel marks or tatweel.</summary>
        public static string NormalizeArabic(string word)
        {
            var sb = new StringBuilder(word.Length);
            foreach (char c in word)
            {
                if (c == '\u0623' || c == '\u0625' || c == '\u0622') sb.Append('\u0627');
                else if (c == '\u0649') sb.Append('\u064A');
                else if (c == '\u0629') sb.Append('\u0647');
                else if (c == '\u0640' || (c >= '\u064B' && c <= '\u0652')) continue;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static bool IsCombiningMark(char c)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark
                || cat == UnicodeCategory.EnclosingMark;
        }
    }
}
