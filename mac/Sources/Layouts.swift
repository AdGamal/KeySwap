import Carbon

/// A keyboard layout installed on this Mac, read straight from the system so the mapping is exact.
struct SystemLayout {
    let source: TISInputSource
    let id: String
    let name: String
    let isArabic: Bool
    let isASCIICapable: Bool
    /// The layout's main language, like "en" or "ar".
    let language: String?
    let map: KeyMap

    func select() { TISSelectInputSource(source) }
}

enum Layouts {
    /// Main typing keys only (letters, digits, punctuation); keypad and function keys are skipped.
    static let typingKeyCodes: [UInt16] = Array(0...50) + [93, 94, 95]

    static func enabled() -> [SystemLayout] { load(includeAllInstalled: false) }
    static func installed() -> [SystemLayout] { load(includeAllInstalled: true) }

    /// Picks the Arabic layout and the Latin layout to convert between, from the enabled keyboards.
    static func pair() -> (latin: SystemLayout, arabic: SystemLayout)? {
        let all = enabled()
        guard let arabic = all.first(where: { $0.isArabic }) else { return nil }
        let currentASCII = TISCopyCurrentASCIICapableKeyboardLayoutInputSource()?.takeRetainedValue()
        let currentID = currentASCII.flatMap { string($0, kTISPropertyInputSourceID) }
        let latins = all.filter { !$0.isArabic && $0.isASCIICapable }
        guard let latin = latins.first(where: { $0.id == currentID }) ?? latins.first else { return nil }
        return (latin, arabic)
    }

    static func currentLayoutID() -> String? {
        let current = TISCopyCurrentKeyboardLayoutInputSource()?.takeRetainedValue()
        return current.flatMap { string($0, kTISPropertyInputSourceID) }
    }

    private static func load(includeAllInstalled: Bool) -> [SystemLayout] {
        let filter = [kTISPropertyInputSourceType as String: kTISTypeKeyboardLayout as String] as CFDictionary
        guard let list = TISCreateInputSourceList(filter, includeAllInstalled)?.takeRetainedValue()
                as? [TISInputSource] else { return [] }
        return list.compactMap(makeLayout)
    }

    private static func makeLayout(_ source: TISInputSource) -> SystemLayout? {
        guard let id = string(source, kTISPropertyInputSourceID),
              let ptr = TISGetInputSourceProperty(source, kTISPropertyUnicodeKeyLayoutData) else { return nil }
        let name = string(source, kTISPropertyLocalizedName) ?? id
        let languages = (TISGetInputSourceProperty(source, kTISPropertyInputSourceLanguages))
            .map { Unmanaged<CFArray>.fromOpaque($0).takeUnretainedValue() as? [String] ?? [] } ?? []
        let ascii = TISGetInputSourceProperty(source, kTISPropertyInputSourceIsASCIICapable)
            .map { CFBooleanGetValue(Unmanaged<CFBoolean>.fromOpaque($0).takeUnretainedValue()) } ?? false
        let data = Unmanaged<CFData>.fromOpaque(ptr).takeUnretainedValue() as Data

        var entries: [(KeyStroke, String)] = []
        data.withUnsafeBytes { raw in
            guard let base = raw.baseAddress else { return }
            let layout = base.assumingMemoryBound(to: UCKeyboardLayout.self)
            for code in typingKeyCodes {
                for shift in [false, true] {
                    if let text = translate(layout, code: code, shift: shift) {
                        entries.append((KeyStroke(key: Int(code), shift: shift), text))
                    }
                }
            }
        }
        return SystemLayout(source: source, id: id, name: name,
                            isArabic: languages.first == "ar", isASCIICapable: ascii, language: languages.first,
                            map: KeyMap(name: name, entries: entries))
    }

    private static func translate(_ layout: UnsafePointer<UCKeyboardLayout>, code: UInt16, shift: Bool) -> String? {
        var deadKeyState: UInt32 = 0
        var length = 0
        var chars = [UniChar](repeating: 0, count: 8)
        let modifiers: UInt32 = shift ? UInt32(shiftKey >> 8) & 0xFF : 0
        let status = UCKeyTranslate(layout, code, UInt16(kUCKeyActionDown), modifiers, UInt32(LMGetKbdType()),
                                    0, &deadKeyState, chars.count, &length, &chars)
        // Dead keys (accents waiting for the next key) don't type a character on their own.
        guard status == noErr, deadKeyState == 0, length > 0 else { return nil }
        return String(utf16CodeUnits: chars, count: length)
    }

    private static func string(_ source: TISInputSource, _ key: CFString) -> String? {
        guard let ptr = TISGetInputSourceProperty(source, key) else { return nil }
        return Unmanaged<CFString>.fromOpaque(ptr).takeUnretainedValue() as String
    }
}
