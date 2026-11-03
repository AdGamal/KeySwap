// Pure conversion logic: no macOS APIs, so it can be unit-tested with hand-made layouts.

/// One physical key press: which key, and whether Shift was held.
struct KeyStroke: Hashable {
    let key: Int
    let shift: Bool
}

/// What every key of one keyboard layout types.
struct KeyMap {
    let name: String
    /// Keys in priority order (plain keys before shifted ones) so lookups prefer the simplest key.
    private(set) var strokes: [KeyStroke] = []
    private(set) var output: [KeyStroke: String] = [:]
    private(set) var reverse: [String: KeyStroke] = [:]
    private(set) var maxScalars = 1

    init(name: String, entries: [(KeyStroke, String)]) {
        self.name = name
        let ordered = entries.filter { !$0.0.shift } + entries.filter { $0.0.shift }
        for (stroke, text) in ordered where KeyMap.isTypeable(text) && output[stroke] == nil {
            strokes.append(stroke)
            output[stroke] = text
            if reverse[text] == nil { reverse[text] = stroke }
            maxScalars = max(maxScalars, text.unicodeScalars.count)
        }
    }

    /// Keeps printable characters only (no control codes or whitespace, which are the same on every layout).
    static func isTypeable(_ text: String) -> Bool {
        guard !text.isEmpty else { return false }
        return text.unicodeScalars.allSatisfy { s in
            s.value >= 0x20 && s.value != 0x7F && !(0x80...0x9F).contains(s.value)
                && !s.properties.isWhitespace
        }
    }
}

enum Script {
    static func isArabic(_ s: Unicode.Scalar) -> Bool {
        switch s.value {
        case 0x0600...0x06FF, 0x0750...0x077F, 0x08A0...0x08FF, 0xFB50...0xFDFF, 0xFE70...0xFEFF: return true
        default: return false
        }
    }

    static func isLatinLetter(_ s: Unicode.Scalar) -> Bool {
        switch s.value {
        case 0x41...0x5A, 0x61...0x7A, 0xC0...0x24F: return s.properties.isAlphabetic
        default: return false
        }
    }
}

struct ConversionResult {
    let text: String
    /// true when the text was turned into Arabic, false when it was turned into the Latin layout.
    let toArabic: Bool
}

struct Converter {
    let latin: KeyMap
    let arabic: KeyMap

    /// Detects which layout the text was typed with and retypes it on the other one.
    func convert(_ text: String) -> ConversionResult {
        var arabicCount = 0, latinCount = 0
        for s in text.unicodeScalars {
            if Script.isArabic(s) { arabicCount += 1 } else if Script.isLatinLetter(s) { latinCount += 1 }
        }
        let toArabic = latinCount >= arabicCount
        let out = toArabic
            ? Converter.retype(text, from: latin, to: arabic, targetIsLatin: false)
            : Converter.retype(text, from: arabic, to: latin, targetIsLatin: true)
        return ConversionResult(text: out, toArabic: toArabic)
    }

    /// Finds the keys that would type `text` on `source`, then types those keys on `target`.
    /// Characters the source layout can't type (emoji, newlines, other scripts) are kept as they are.
    static func retype(_ text: String, from source: KeyMap, to target: KeyMap, targetIsLatin: Bool) -> String {
        let scalars = Array(text.unicodeScalars)
        var pieces: [Piece] = []
        var i = 0
        while i < scalars.count {
            var matched = false
            for len in stride(from: min(source.maxScalars, scalars.count - i), through: 1, by: -1) {
                let chunk = String(String.UnicodeScalarView(scalars[i..<(i + len)]))
                guard let stroke = source.reverse[chunk] else { continue }
                let primary = target.output[stroke] ?? chunk
                // A single key that types several characters (e.g. لا) could also have been typed as
                // separate keys. Keep that reading too so the word check below can pick between them.
                var alternative: String?
                if len > 1 {
                    let parts = scalars[i..<(i + len)].map { source.reverse[String($0)] }
                    if parts.allSatisfy({ $0 != nil }) {
                        alternative = parts.map { target.output[$0!] ?? "" }.joined()
                    }
                }
                pieces.append(Piece(primary: primary, alternative: alternative))
                i += len
                matched = true
                break
            }
            if !matched {
                pieces.append(Piece(primary: String(scalars[i]), alternative: nil))
                i += 1
            }
        }
        if targetIsLatin { resolveEnglishAmbiguity(&pieces) }
        return pieces.map { $0.chosen }.joined()
    }

    struct Piece {
        let primary: String
        let alternative: String?
        var useAlternative = false
        var chosen: String { useAlternative ? alternative! : primary }
    }

    /// On the Windows-style Arabic keyboard the B key types "لا", which is also what G then H type.
    /// "b" is the default; "gh" wins only when it forms a known English spelling (light, through...).
    static let ghSpellings = ["ght", "high", "sigh", "thigh", "nigh", "through", "though", "thorough",
                              "borough", "enough", "laugh", "tough", "rough", "cough", "trough", "dough",
                              "plough", "weigh", "neigh", "sleigh", "ghost", "ghast", "ghetto", "ghoul",
                              "spaghetti", "afghan", "yoghurt", "dinghy", "sorghum", "burgh"]
    static let ghWords: Set<String> = ["ugh", "hugh"]

    static func resolveEnglishAmbiguity(_ pieces: inout [Piece]) {
        for idx in pieces.indices where pieces[idx].alternative != nil {
            // Find the word around this piece: neighbouring pieces made only of letters.
            func isWordPiece(_ p: Piece) -> Bool {
                !p.chosen.isEmpty && p.chosen.unicodeScalars.allSatisfy { Script.isLatinLetter($0) }
            }
            var start = idx, end = idx
            while start > 0 && isWordPiece(pieces[start - 1]) { start -= 1 }
            while end < pieces.count - 1 && isWordPiece(pieces[end + 1]) { end += 1 }

            var trial = pieces
            trial[idx].useAlternative = true
            let before = trial[start..<idx].map { $0.chosen }.joined().lowercased()
            let alt = trial[idx].chosen.lowercased()
            let after = trial[(idx + 1)..<(end + 1)].map { $0.chosen }.joined().lowercased()
            let word = before + alt + after
            let altStart = before.count, altEnd = before.count + alt.count

            var keep = ghWords.contains(word)
            if !keep {
                let letters = Array(word)
                outer: for spelling in ghSpellings {
                    let s = Array(spelling)
                    guard s.count <= letters.count else { continue }
                    for pos in 0...(letters.count - s.count) where Array(letters[pos..<(pos + s.count)]) == s {
                        if pos <= altStart && pos + s.count >= altEnd { keep = true; break outer }
                    }
                }
            }
            if keep { pieces[idx].useAlternative = true }
        }
    }
}

/// Recognises Shift pressed and released twice quickly, with no other key in between.
final class DoubleShiftDetector {
    var maxPress: Double = 0.35
    var maxGap: Double = 0.40
    var onTrigger: () -> Void = {}

    private var shiftDownAt: Double?
    private var taps = 0
    private var lastTapAt: Double = 0

    func shift(down: Bool, otherModifiersHeld: Bool, at time: Double) {
        if otherModifiersHeld { reset(); return }
        if down {
            if shiftDownAt != nil { return } // key repeat or the second Shift key
            if taps > 0 && time - lastTapAt > maxGap { taps = 0 }
            shiftDownAt = time
        } else {
            guard let downAt = shiftDownAt else { return }
            shiftDownAt = nil
            guard time - downAt <= maxPress else { taps = 0; return }
            taps += 1
            lastTapAt = time
            if taps == 2 {
                taps = 0
                onTrigger()
            }
        }
    }

    /// Any other key, modifier or click cancels a tap in progress.
    func otherInput() { reset() }

    private func reset() {
        shiftDownAt = nil
        taps = 0
    }
}
