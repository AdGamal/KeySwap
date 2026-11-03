import Foundation

var failures = 0
var passes = 0

func check(_ got: String, _ want: String, _ label: String) {
    if got == want { passes += 1 } else {
        failures += 1
        print("FAIL \(label): got \"\(got)\" want \"\(want)\"")
    }
}

func check(_ cond: Bool, _ label: String) {
    if cond { passes += 1 } else { failures += 1; print("FAIL \(label)") }
}

// MARK: Real macOS layouts

let installed = Layouts.installed()
guard let us = installed.first(where: { $0.id == "com.apple.keylayout.US" }),
      let macArabic = installed.first(where: { $0.id == "com.apple.keylayout.Arabic" }) else {
    print("FAIL could not load the U.S. and Arabic layouts"); exit(1)
}
let mac = Converter(latin: us.map, arabic: macArabic.map)

check(mac.convert("hello world").text, "اثممخ صخقمي", "mac en→ar")
check(mac.convert("hello world").toArabic, "mac en→ar direction")
check(mac.convert("اثممخ صخقمي").text, "hello world", "mac ar→en")
check(mac.convert("lnpfh").text, "مرحبا", "mac typed مرحبا on English")
check(mac.convert("مرحبا").text, "lnpfh", "mac ar→en word")
check(mac.convert("Hello, World?").text, mac.convert("Hello, World?").text, "stable")
check(mac.convert(mac.convert("Hello, how are you? 123").text).text, "Hello, how are you? 123", "mac round trip sentence")
check(mac.convert("شَ").text, "aQ", "harakat are separate keys")
check(mac.convert("hi 👋\nthere").text, "اه 👋\nفاثقث", "emoji and newline kept")
check(mac.convert("").text, "", "empty")

// Every key of every Arabic layout on this Mac, against U.S.: English → Arabic → English must come back.
for arabic in installed where arabic.isArabic {
    var lost: [String] = []
    for stroke in us.map.strokes {
        let ch = us.map.output[stroke]!
        guard Script.isLatinLetter(ch.unicodeScalars.first!) else { continue }
        let there = Converter.retype(ch, from: us.map, to: arabic.map, targetIsLatin: false)
        let back = Converter.retype(there, from: arabic.map, to: us.map, targetIsLatin: true)
        if back != ch { lost.append("\(ch)→\(there)→\(back)") }
    }
    if arabic.id == "com.apple.keylayout.Arabic" {
        check(lost.isEmpty, "\(arabic.name): every English letter survives a round trip \(lost)")
    } else if !lost.isEmpty {
        // These layouts type some Arabic letters from two different keys, so the way back is ambiguous.
        print("note \(arabic.name): shared keys \(lost)")
    }
}

// MARK: Windows-style Arabic (101) layout, where B types "لا"

let qwerty = Array("qwertyuiop[]asdfghjkl;'zxcvbnm,./")
let arabic101 = ["ض", "ص", "ث", "ق", "ف", "غ", "ع", "ه", "خ", "ح", "ج", "د", "ش", "س", "ي", "ب", "ل", "ا", "ت",
                 "ن", "م", "ك", "ط", "ئ", "ء", "ؤ", "ر", "لا", "ى", "ة", "و", "ز", "ظ"]
let winLatin = KeyMap(name: "US", entries: qwerty.enumerated().map { (KeyStroke(key: $0.offset, shift: false), String($0.element)) }
    + qwerty.enumerated().map { (KeyStroke(key: $0.offset, shift: true), String($0.element).uppercased()) })
let winArabic = KeyMap(name: "Arabic 101", entries: arabic101.enumerated().map { (KeyStroke(key: $0.offset, shift: false), $0.element) })
let win = Converter(latin: winLatin, arabic: winArabic)

func typedOnArabic101(_ english: String) -> String {
    Converter.retype(english, from: winLatin, to: winArabic, targetIsLatin: false)
}

for word in ["light", "about", "bought", "enough", "job", "neighbor", "high", "night", "bob", "club",
             "web", "brightness", "through the night", "big bright lights", "thought", "laugh", "ghost", "ugh"] {
    check(win.convert(typedOnArabic101(word)).text, word, "101 \(word)")
}
check(win.convert("lvpfh").text, "مرحبا", "101 typed مرحبا on English")
check(win.convert("lv").text, "مر", "101 short")

// MARK: Double-tap Shift

func taps(_ events: [(String, Double)]) -> Int {
    let d = DoubleShiftDetector()
    var fired = 0
    d.onTrigger = { fired += 1 }
    for (e, t) in events {
        switch e {
        case "down": d.shift(down: true, otherModifiersHeld: false, at: t)
        case "up": d.shift(down: false, otherModifiersHeld: false, at: t)
        case "cmd": d.shift(down: true, otherModifiersHeld: true, at: t)
        default: d.otherInput()
        }
    }
    return fired
}

check(taps([("down", 0), ("up", 0.08), ("down", 0.2), ("up", 0.28)]) == 1, "double tap fires")
check(taps([("down", 0), ("up", 0.08), ("down", 0.7), ("up", 0.78)]) == 0, "too slow")
check(taps([("down", 0), ("key", 0.05), ("up", 0.08), ("down", 0.2), ("up", 0.28)]) == 0, "typing a capital")
check(taps([("down", 0), ("up", 0.6), ("down", 0.7), ("up", 0.78)]) == 0, "held too long")
check(taps([("down", 0), ("up", 0.08), ("key", 0.1), ("down", 0.2), ("up", 0.28)]) == 0, "key between taps")
check(taps([("down", 0), ("up", 0.08), ("cmd", 0.15), ("down", 0.2), ("up", 0.28)]) == 0, "with Cmd")
check(taps([("down", 0), ("down", 0.03), ("up", 0.08), ("down", 0.2), ("down", 0.22), ("up", 0.28)]) == 1, "key repeat ignored")
check(taps([("down", 0), ("up", 0.08), ("down", 0.2), ("up", 0.28), ("down", 0.4), ("up", 0.48)]) == 1, "triple is one")

print("\(passes) passed, \(failures) failed")
exit(failures == 0 ? 0 : 1)
