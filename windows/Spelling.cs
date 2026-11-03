// Uses the spell checker built into Windows (8 and later) to tell real words from wrong-keyboard text.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace KeySwap
{
    public class Spelling
    {
        readonly ISpellChecker latin;
        readonly ISpellChecker arabic;

        public string LatinLanguage { get; private set; }
        public string ArabicLanguage { get; private set; }

        /// <summary>True when Windows can check both languages; otherwise fixing while typing stays off.</summary>
        public bool Available { get { return latin != null && arabic != null; } }

        /// <param name="latinCulture">The Latin keyboard's language, like "en-US" or "fr-FR".</param>
        public Spelling(string latinCulture)
        {
            ISpellCheckerFactory factory;
            try { factory = (ISpellCheckerFactory)new SpellCheckerFactoryClass(); }
            catch (Exception) { return; } // Windows 7 has no spell checker API

            var supported = SupportedLanguages(factory);
            string latinPrefix = latinCulture.Split('-')[0] + "-";
            string latinTag = Pick(supported, latinCulture, latinPrefix) ?? Pick(supported, "en-US", "en-");
            string arabicTag = Pick(supported, "ar-SA", "ar-");
            latin = Create(factory, latinTag);
            arabic = Create(factory, arabicTag);
            if (latin != null) LatinLanguage = latinTag;
            if (arabic != null) ArabicLanguage = arabicTag;
        }

        public bool IsCorrect(string word, bool isArabic)
        {
            ISpellChecker checker = isArabic ? arabic : latin;
            if (checker == null) return false;
            try
            {
                IEnumSpellingError errors;
                if (checker.Check(word, out errors) != 0 || errors == null) return false;
                object error;
                bool hasError = errors.Next(out error) == 0; // S_OK means there's an error; S_FALSE means none
                if (error != null) Marshal.ReleaseComObject(error);
                Marshal.ReleaseComObject(errors);
                return !hasError;
            }
            catch (Exception) { return false; }
        }

        static List<string> SupportedLanguages(ISpellCheckerFactory factory)
        {
            var result = new List<string>();
            try
            {
                IEnumString languages;
                if (factory.get_SupportedLanguages(out languages) != 0 || languages == null) return result;
                var one = new string[1];
                while (languages.Next(1, one, IntPtr.Zero) == 0) result.Add(one[0]);
            }
            catch (Exception) { }
            return result;
        }

        static string Pick(List<string> supported, string exact, string prefix)
        {
            foreach (string tag in supported) if (string.Equals(tag, exact, StringComparison.OrdinalIgnoreCase)) return tag;
            foreach (string tag in supported) if (tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return tag;
            return null;
        }

        static ISpellChecker Create(ISpellCheckerFactory factory, string tag)
        {
            if (tag == null) return null;
            try
            {
                ISpellChecker checker;
                return factory.CreateSpellChecker(tag, out checker) == 0 ? checker : null;
            }
            catch (Exception) { return null; }
        }

        [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
        class SpellCheckerFactoryClass { }

        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISpellCheckerFactory
        {
            [PreserveSig] int get_SupportedLanguages(out IEnumString value);
            [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);
            [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISpellChecker
        {
            [PreserveSig] int get_LanguageTag(out IntPtr value);
            [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IEnumSpellingError
        {
            [PreserveSig] int Next([MarshalAs(UnmanagedType.IUnknown)] out object value);
        }
    }
}
