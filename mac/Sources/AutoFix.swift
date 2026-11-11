// Fixing while typing: remembers the keys pressed since the last pause and decides whether they were
// meant for the other keyboard. Pure logic, so it can be tested with hand-made layouts and word lists.

enum Typed: Equatable {
    case key(KeyStroke)
    case space
}

struct AutoFix: Equatable {
    /// How many characters to delete with Backspace.
    let deleteCount: Int
    /// What to type instead.
    let text: String
}

/// The keys typed since the last click, Enter, shortcut or app switch, all on one keyboard layout.
final class TypingBuffer {
    static let maxItems = 80

    private(set) var items: [Typed] = []
    private(set) var layoutID: String?

    func add(_ item: Typed, layoutID: String) {
        if layoutID != self.layoutID {
            items = []
            self.layoutID = layoutID
        }
        items.append(item)
        if items.count > TypingBuffer.maxItems {
            // Drop the oldest word so we only ever look at the most recent text.
            if let space = items.firstIndex(of: .space) { items.removeFirst(space + 1) } else { items = [] }
        }
    }

    /// Backspace removes the last key, unless that key typed several characters (like لا): then one
    /// Backspace only removes part of it, so we can no longer tell what's on screen and start over.
    func backspace(charactersTypedBy count: (KeyStroke) -> Int) {
        guard let last = items.last else { return }
        if case .key(let stroke) = last, count(stroke) != 1 { reset(); return }
        items.removeLast()
    }

    func reset() {
        items = []
        layoutID = nil
    }
}

enum AutoFixer {
    /// Decides whether the last words typed were meant for the other keyboard.
    /// - `isCorrect(word, arabic)` asks the system spell checker about one word.
    ///
    /// Each word is judged twice: does it look right as typed, and does it look right on the other
    /// keyboard? Spell checkers, the Arabic ones most of all, accept many letter mixes (about a quarter
    /// of common English words typed on the Arabic keyboard "spell" Arabic), so:
    /// - a common word as typed always looks right, and a common word on the other keyboard is proof;
    /// - an English word of 4 letters or more against Arabic that is merely accepted is "weak proof":
    ///   it counts only next to real proof ("turn off the lights": "the" is common, so "turn" counts);
    /// - English shortcuts and capitals always look right as English;
    /// - anything with punctuation inside is not English ("h.d;" is ازيك: ز and ك are on those keys).
    ///
    /// Only the trailing words are fixed, starting at the first word that looks wrong as typed. In that
    /// run there must be:
    /// - no word that is right as typed and wrong on the other keyboard,
    /// - at least 3 letters of "proof" words (wrong as typed, right on the other keyboard),
    /// - at least as many proof and weak-proof words as other words that are right both ways, and as
    ///   words that are wrong both ways (names, typos), which are then fixed along with the rest.
    /// The run then grows backwards over words that are right both ways but not common as typed
    /// ("pls" before "send me the file").
    ///
    /// A last word with no space after it may still be half-typed: on the English keyboard, if it starts
    /// like an English word ("hel", "tomo") it proves nothing and is never fixed on its own, unless the
    /// other keyboard gives a common Arabic word.
    static func decide(_ typed: [Typed], current: KeyMap, other: KeyMap, currentIsArabic: Bool,
                       isCorrect: (String, Bool) -> Bool) -> AutoFix? {
        // Per word, what each key types on both keyboards.
        var words: [[(typed: String, meant: String)]] = [[]]
        for item in typed {
            switch item {
            case .space:
                words.append([])
            case .key(let stroke):
                guard let a = current.output[stroke], let b = other.output[stroke] else { return nil }
                words[words.count - 1].append((a, b))
            }
        }
        var judged = words.map { judge($0, currentIsArabic: currentIsArabic, isCorrect: isCorrect) }
        if !currentIsArabic, let last = typed.last, last != .space, !judged[judged.count - 1].meantIsCommon,
           couldBeUnfinishedEnglish(judged[judged.count - 1].typed) {
            judged[judged.count - 1].unfinished = true
        }

        for first in words.indices {
            guard judged[first].checked, !judged[first].typedLooksRight, !judged[first].unfinished else { continue }
            var proof = 0, proofLetters = 0, rightBothWays = 0, wrongBothWays = 0, contradicted = false
            for j in judged[first...] where j.checked && !j.unfinished {
                switch (j.typedLooksRight, j.meantIsRight) {
                case (false, true): proof += 1; proofLetters += j.letters
                case (true, true): if j.weakProof { proof += 1 } else { rightBothWays += 1 }
                case (false, false): wrongBothWays += 1
                case (true, false): contradicted = true
                }
            }
            guard !contradicted, proofLetters >= 3, proof >= rightBothWays, proof >= wrongBothWays else { continue }
            var start = first
            while start > 0, judged[start - 1].checked, judged[start - 1].meantIsRight, !judged[start - 1].typedIsCommon {
                start -= 1
            }
            let asTyped = words[start...].map { $0.map { $0.typed }.joined() }.joined(separator: " ")
            // Backspace counts are only reliable for plain characters (no vowel marks or emoji).
            guard asTyped.unicodeScalars.allSatisfy({ $0.value <= 0xFFFF && !isCombiningMark($0) }) else { return nil }
            return AutoFix(deleteCount: asTyped.utf16.count,
                           text: words[start...].map { $0.map { $0.meant }.joined() }.joined(separator: " "))
        }
        return nil
    }

    struct Judgement {
        var checked = false
        var typed = ""
        var meantIsRight = false
        var meantIsCommon = false
        var typedLooksRight = false
        var typedIsCommon = false
        var weakProof = false
        var unfinished = false
        var letters = 0
    }

    static func judge(_ keys: [(typed: String, meant: String)], currentIsArabic: Bool,
                      isCorrect: (String, Bool) -> Bool) -> Judgement {
        var j = Judgement()
        // The word without the punctuation around it: "(hello!" → "hello". A key counts as part of the
        // word if it types a letter on either keyboard, so ";glm" stays whole (it is كلمة).
        func hasLetter(_ key: (typed: String, meant: String)) -> Bool {
            (key.typed + key.meant).unicodeScalars.contains { $0.properties.isAlphabetic }
        }
        guard let first = keys.firstIndex(where: hasLetter), let last = keys.lastIndex(where: hasLetter) else { return j }
        let typed = keys[first...last].map { $0.typed }.joined()
        j.typed = typed
        let meant = keys[first...last].map { $0.meant }.joined()
        guard meant.unicodeScalars.contains(where: { $0.properties.isAlphabetic }) else { return j } // 5% → ٥٪

        j.checked = true
        j.letters = meant.unicodeScalars.filter { $0.properties.isAlphabetic && !isCombiningMark($0) }.count
        j.meantIsCommon = isCommon(meant, arabic: !currentIsArabic)
        j.meantIsRight = j.meantIsCommon || looksRight(meant, arabic: !currentIsArabic, isCorrect: isCorrect)
        j.typedIsCommon = isCommon(typed, arabic: currentIsArabic)
        j.typedLooksRight = j.typedIsCommon
            || (!j.meantIsCommon && looksRight(typed, arabic: currentIsArabic, isCorrect: isCorrect))
        // The Arabic checker accepts so much that a real English word of 4+ letters outweighs it, but
        // only as support: real Arabic can spell English too (بعته is "fuji").
        j.weakProof = currentIsArabic && !j.typedIsCommon && j.typedLooksRight && j.letters >= 4
            && isCorrect(meant, false)
        return j
    }

    /// One word in one language, by more than the spell checker alone.
    private static func looksRight(_ word: String, arabic: Bool, isCorrect: (String, Bool) -> Bool) -> Bool {
        guard word.unicodeScalars.contains(where: { $0.properties.isAlphabetic }) else { return false }
        if arabic {
            // Shift on the Arabic keyboard types vowel marks, so English capitals become marks in odd
            // places ("Tik" → ُهن). A word doesn't start with one.
            if let first = word.unicodeScalars.first, isCombiningMark(first) { return false }
            return isCorrect(word, true)
        }
        // Arabic letters live on English punctuation keys (ج is [, ك is ;, ز is .): "h.d;" is not English,
        // even though a spell checker sees two fine one-letter words in it.
        let allowedInside: Set<Unicode.Scalar> = ["'", "\u{2019}", "-"]
        if word.unicodeScalars.contains(where: { !$0.properties.isAlphabetic && !allowedInside.contains($0) }) { return false }
        if looksLikeEnglishShortcut(word) || isCorrect(word, false) { return true }
        // Names, days and months are only right with a capital: monday, cairo.
        return word == word.lowercased() && isCorrect(word.prefix(1).uppercased() + word.dropFirst(), false)
    }

    /// A word that may still be in the middle of being typed: it starts the way English words start.
    static func couldBeUnfinishedEnglish(_ typed: String) -> Bool {
        let scalars = Array(typed.lowercased().unicodeScalars.filter { $0 != "'" })
        guard scalars.allSatisfy({ ("a"..."z").contains($0) }) else { return false }
        return scalars.count < 3 || CommonWords.englishPrefixes.contains(String(String.UnicodeScalarView(scalars.prefix(3))))
    }

    static let commonArabic = Set(CommonWords.arabic.map(normalizeArabic))

    /// Chat shortcuts (lol, brb, tmr...) and words in capitals (OK, ASAP) are English on purpose, even
    /// when the spell checker doesn't know them and their letters happen to spell Arabic.
    static func looksLikeEnglishShortcut(_ word: String) -> Bool {
        if CommonWords.englishShortcuts.contains(word.lowercased()) { return true }
        let letters = word.unicodeScalars.filter { $0.properties.isAlphabetic }
        return letters.count >= 2 && letters.allSatisfy { ("A"..."Z").contains($0) }
    }

    static func isCommon(_ word: String, arabic: Bool) -> Bool {
        arabic ? commonArabic.contains(normalizeArabic(word)) : CommonWords.english.contains(word.lowercased())
    }

    /// Spellings people use interchangeably: أ/إ/آ → ا, ى → ي, ة → ه, and no vowel marks or tatweel.
    static func normalizeArabic(_ word: String) -> String {
        var out = String.UnicodeScalarView()
        for s in word.unicodeScalars {
            switch s.value {
            case 0x0623, 0x0625, 0x0622: out.append("\u{0627}")
            case 0x0649: out.append("\u{064A}")
            case 0x0629: out.append("\u{0647}")
            case 0x0640, 0x064B...0x0652: continue
            default: out.append(s)
            }
        }
        return String(out)
    }

    static func isCombiningMark(_ s: Unicode.Scalar) -> Bool {
        switch s.properties.generalCategory {
        case .nonspacingMark, .spacingMark, .enclosingMark: return true
        default: return false
        }
    }
}
