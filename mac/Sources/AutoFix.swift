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
    /// Spell checkers accept many short letter mixes (about a quarter of common English words typed on
    /// the Arabic keyboard spell real Arabic), so everyday words count more than the spell checker:
    /// a common word as typed is never "wrong", and a common word on the other keyboard is strong proof.
    /// English shortcuts and capitals typed on the English keyboard are never "wrong" either.
    /// Only the trailing words are fixed, starting at the first word that's wrong as typed; there must be
    /// at least 3 letters, every fixed word must be right on the other keyboard, and at least half of them
    /// wrong as typed.
    static func decide(_ typed: [Typed], current: KeyMap, other: KeyMap, currentIsArabic: Bool,
                       isCorrect: (String, Bool) -> Bool) -> AutoFix? {
        var words: [(typed: String, meant: String)] = [("", "")]
        for item in typed {
            switch item {
            case .space:
                words.append(("", ""))
            case .key(let stroke):
                guard let a = current.output[stroke], let b = other.output[stroke] else { return nil }
                words[words.count - 1].typed += a
                words[words.count - 1].meant += b
            }
        }
        let judged = words.map { judge(typed: $0.typed, meant: $0.meant, currentIsArabic: currentIsArabic, isCorrect: isCorrect) }

        for start in words.indices {
            guard judged[start].checked && judged[start].wrongAsTyped else { continue }
            let tail = judged[start...]
            let checked = tail.filter { $0.checked }
            guard checked.allSatisfy({ $0.meantIsRight }) else { continue }
            let letters = checked.reduce(0) { $0 + $1.letters }
            let wrong = checked.filter { $0.wrongAsTyped }.count
            guard letters >= 3, wrong * 2 >= checked.count else { continue }
            let asTyped = words[start...].map { $0.typed }.joined(separator: " ")
            // Backspace counts are only reliable for plain characters (no vowel marks or emoji).
            guard asTyped.unicodeScalars.allSatisfy({ $0.value <= 0xFFFF && !isCombiningMark($0) }) else { return nil }
            return AutoFix(deleteCount: asTyped.utf16.count, text: words[start...].map { $0.meant }.joined(separator: " "))
        }
        return nil
    }

    struct Judgement {
        var checked = false
        var meantIsRight = false
        var wrongAsTyped = false
        var letters = 0
    }

    static func judge(typed: String, meant: String, currentIsArabic: Bool, isCorrect: (String, Bool) -> Bool) -> Judgement {
        var j = Judgement()
        let meantCore = lettersCore(meant)
        guard !meantCore.isEmpty else { return j } // punctuation or digits only
        j.checked = true
        j.letters = meantCore.unicodeScalars.count
        let meantCommon = isCommon(meantCore, arabic: !currentIsArabic)
        j.meantIsRight = meantCommon || isCorrect(meantCore, !currentIsArabic)
        let typedCore = lettersCore(typed)
        let typedLooksRight = !typedCore.isEmpty
            && (isCommon(typedCore, arabic: currentIsArabic)
                || (!currentIsArabic && looksLikeEnglishShortcut(typedCore))
                || (!meantCommon && isCorrect(typedCore, currentIsArabic)))
        j.wrongAsTyped = !typedLooksRight
        return j
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

    /// The word without punctuation around it: "(hello!" → "hello".
    static func lettersCore(_ word: String) -> String {
        let scalars = Array(word.unicodeScalars)
        guard let first = scalars.firstIndex(where: { $0.properties.isAlphabetic }),
              let last = scalars.lastIndex(where: { $0.properties.isAlphabetic }) else { return "" }
        return String(String.UnicodeScalarView(scalars[first...last]))
    }

    static func isCombiningMark(_ s: Unicode.Scalar) -> Bool {
        switch s.properties.generalCategory {
        case .nonspacingMark, .spacingMark, .enclosingMark: return true
        default: return false
        }
    }
}
