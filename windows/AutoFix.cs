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
        /// asks the spell checker about one word.
        ///
        /// Spell checkers accept many short letter mixes (about a quarter of common English words typed on
        /// the Arabic keyboard spell real Arabic), so everyday words count more than the spell checker: a
        /// common word as typed is never "wrong", and a common word on the other keyboard is strong proof.
        /// English shortcuts and capitals typed on the English keyboard are never "wrong" either.
        /// Only the trailing words are fixed, starting at the first word that's wrong as typed; there must be
        /// at least 3 letters, every fixed word must be right on the other keyboard, and at least half of
        /// them wrong as typed.
        /// </summary>
        public static AutoFix Decide(IList<Typed> typed, KeyMap current, KeyMap other, bool currentIsArabic,
                                     Func<string, bool, bool> isCorrect)
        {
            var typedWords = new List<StringBuilder> { new StringBuilder() };
            var meantWords = new List<StringBuilder> { new StringBuilder() };
            foreach (Typed item in typed)
            {
                if (item.IsSpace)
                {
                    typedWords.Add(new StringBuilder());
                    meantWords.Add(new StringBuilder());
                    continue;
                }
                string a, b;
                if (!current.Output.TryGetValue(item.Key, out a) || !other.Output.TryGetValue(item.Key, out b)) return null;
                typedWords[typedWords.Count - 1].Append(a);
                meantWords[meantWords.Count - 1].Append(b);
            }
            int n = typedWords.Count;
            var judged = new Judgement[n];
            for (int i = 0; i < n; i++)
                judged[i] = Judge(typedWords[i].ToString(), meantWords[i].ToString(), currentIsArabic, isCorrect);

            for (int start = 0; start < n; start++)
            {
                if (!judged[start].Checked || !judged[start].WrongAsTyped) continue;
                int checkedWords = 0, wrong = 0, letters = 0;
                bool allRight = true;
                for (int i = start; i < n; i++)
                {
                    if (!judged[i].Checked) continue;
                    checkedWords++;
                    letters += judged[i].Letters;
                    if (judged[i].WrongAsTyped) wrong++;
                    if (!judged[i].MeantIsRight) allRight = false;
                }
                if (!allRight || letters < 3 || wrong * 2 < checkedWords) continue;

                var asTyped = new StringBuilder();
                var meant = new StringBuilder();
                for (int i = start; i < n; i++)
                {
                    if (i > start) { asTyped.Append(' '); meant.Append(' '); }
                    asTyped.Append(typedWords[i]);
                    meant.Append(meantWords[i]);
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
            public bool MeantIsRight;
            public bool WrongAsTyped;
            public int Letters;
        }

        static Judgement Judge(string typed, string meant, bool currentIsArabic, Func<string, bool, bool> isCorrect)
        {
            var j = new Judgement();
            string meantCore = LettersCore(meant);
            if (meantCore.Length == 0) return j; // punctuation or digits only
            j.Checked = true;
            j.Letters = meantCore.Length;
            bool meantCommon = IsCommon(meantCore, !currentIsArabic);
            j.MeantIsRight = meantCommon || isCorrect(meantCore, !currentIsArabic);
            string typedCore = LettersCore(typed);
            bool typedLooksRight = typedCore.Length > 0
                && (IsCommon(typedCore, currentIsArabic)
                    || (!currentIsArabic && LooksLikeEnglishShortcut(typedCore))
                    || (!meantCommon && isCorrect(typedCore, currentIsArabic)));
            j.WrongAsTyped = !typedLooksRight;
            return j;
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

        /// <summary>The word without punctuation around it: "(hello!" → "hello".</summary>
        public static string LettersCore(string word)
        {
            int first = 0, last = word.Length - 1;
            while (first <= last && !char.IsLetter(word[first])) first++;
            while (last >= first && !char.IsLetter(word[last])) last--;
            return first > last ? "" : word.Substring(first, last - first + 1);
        }

        static bool IsCombiningMark(char c)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark
                || cat == UnicodeCategory.EnclosingMark;
        }
    }
}
