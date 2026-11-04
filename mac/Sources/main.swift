import Cocoa
import Carbon
import ServiceManagement
import os

private let log = Logger(subsystem: "app.keyswap.mac", category: "general")

/// Marks the key presses KeySwap sends itself, so it doesn't mistake them for the user's typing.
let ownEventMarker: Int64 = 0x4B53_5750

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private let detector = DoubleShiftDetector()
    private let typing = TypingBuffer()
    private var pauseTimer: Timer?
    private var cachedPair: (latin: SystemLayout, arabic: SystemLayout)?
    private var monitors: [Any] = []
    private var trustTimer: Timer?
    private var busy = false
    private let defaults = UserDefaults.standard
    private let pauseBeforeAutoFix = 0.6

    private var autoFix: Bool {
        get { defaults.object(forKey: "autoFix") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "autoFix") }
    }

    private var enabled: Bool {
        get { defaults.object(forKey: "enabled") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "enabled") }
    }
    private var switchKeyboard: Bool {
        get { defaults.object(forKey: "switchKeyboard") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "switchKeyboard") }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // Only one KeySwap at a time (opening it again, or Open at Login, must not start a second one
        // that would fix the same words twice).
        let running = NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "")
        if running.contains(where: { $0 != NSRunningApplication.current }) {
            log.notice("already running, quitting this copy")
            NSApp.terminate(nil)
            return
        }

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.title = "ع⇄A"
        statusItem.button?.toolTip = "KeySwap: double-tap Shift to fix text typed in the wrong language"
        let menu = NSMenu()
        menu.delegate = self
        statusItem.menu = menu

        detector.onTrigger = { [weak self] in
            guard let self, self.enabled else { return }
            self.fixSelection()
        }

        // Start at login by default, once; the user can turn it off from the menu.
        if #available(macOS 13.0, *), defaults.object(forKey: "loginItemConfigured") == nil {
            defaults.set(true, forKey: "loginItemConfigured")
            try? SMAppService.mainApp.register()
        }

        // Typing in another app, or adding/switching keyboards, starts fresh.
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didActivateApplicationNotification,
                                                          object: nil, queue: .main) { [weak self] _ in self?.resetTyping("app switch") }
        DistributedNotificationCenter.default().addObserver(
            forName: NSNotification.Name(kTISNotifyEnabledKeyboardInputSourcesChanged as String),
            object: nil, queue: .main) { [weak self] _ in self?.cachedPair = nil }

        log.notice("started, accessibility trusted: \(AXIsProcessTrusted(), privacy: .public)")
        if AXIsProcessTrusted() {
            installMonitors()
        } else {
            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
            AXIsProcessTrustedWithOptions(options)
            // Start listening as soon as the user grants access, without needing a restart.
            trustTimer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] timer in
                guard AXIsProcessTrusted() else { return }
                timer.invalidate()
                log.notice("accessibility granted, listening now")
                self?.installMonitors()
            }
        }
    }

    // MARK: Listening for the shortcut

    private func installMonitors() {
        monitors.forEach { NSEvent.removeMonitor($0) }
        monitors = []
        let mask: NSEvent.EventTypeMask = [.flagsChanged, .keyDown, .leftMouseDown, .rightMouseDown, .otherMouseDown]
        if let global = NSEvent.addGlobalMonitorForEvents(matching: mask, handler: { [weak self] in self?.handle($0) }) {
            monitors.append(global)
        }
        if let local = NSEvent.addLocalMonitorForEvents(matching: mask, handler: { [weak self] in self?.handle($0); return $0 }) {
            monitors.append(local)
        }
    }

    private func handle(_ event: NSEvent) {
        if event.cgEvent?.getIntegerValueField(.eventSourceUserData) == ownEventMarker { return }
        if event.type == .keyDown { trackTyping(event) }
        if event.type == .leftMouseDown || event.type == .rightMouseDown || event.type == .otherMouseDown {
            resetTyping("click") // a click may move the cursor
        }
        guard event.type == .flagsChanged else { detector.otherInput(); return }
        let leftShift: UInt16 = 56, rightShift: UInt16 = 60
        guard event.keyCode == leftShift || event.keyCode == rightShift else { detector.otherInput(); return }
        let flags = event.modifierFlags
        detector.shift(down: flags.contains(.shift),
                       otherModifiersHeld: !flags.intersection([.command, .option, .control]).isEmpty,
                       at: event.timestamp)
    }

    // MARK: Fixing while typing

    private func pair() -> (latin: SystemLayout, arabic: SystemLayout)? {
        if cachedPair == nil { cachedPair = Layouts.pair() }
        return cachedPair
    }

    private func resetTyping(_ reason: String) {
        if !typing.items.isEmpty { log.notice("start over (\(reason, privacy: .public)) after \(self.typing.items.count, privacy: .public) keys") }
        typing.reset()
    }

    private func trackTyping(_ event: NSEvent) {
        pauseTimer?.invalidate()
        guard enabled, autoFix else { typing.reset(); return }
        let flags = event.modifierFlags
        let code = event.keyCode
        let space: UInt16 = 49, backspace: UInt16 = 51
        guard flags.intersection([.command, .control, .option, .capsLock, .function]).isEmpty,
              let layoutID = Layouts.currentLayoutID(), let pair = pair(),
              layoutID == pair.latin.id || layoutID == pair.arabic.id else {
            log.debug("key ignored: flags \(flags.rawValue, privacy: .public) layout \(Layouts.currentLayoutID() ?? "-", privacy: .public) pair \(self.pair() != nil, privacy: .public)")
            resetTyping("shortcut or other keyboard") // shortcuts, arrows and other keyboards end the run
            return
        }
        let map = layoutID == pair.arabic.id ? pair.arabic.map : pair.latin.map
        let stroke = KeyStroke(key: Int(code), shift: flags.contains(.shift))
        if code == backspace {
            typing.backspace { map.output[$0]?.utf16.count ?? 0 }
        } else if code == space {
            typing.add(.space, layoutID: layoutID)
        } else if map.output[stroke] != nil {
            typing.add(.key(stroke), layoutID: layoutID)
        } else {
            resetTyping("key \(code)") // Return, Tab, arrows, Escape...
            return
        }
        pauseTimer = Timer.scheduledTimer(withTimeInterval: pauseBeforeAutoFix, repeats: false) { [weak self] _ in
            self?.fixWhileTyping()
        }
    }

    private func fixWhileTyping() {
        guard !busy, enabled, autoFix, !IsSecureEventInputEnabled(), // never in password fields
              let pair = pair(), let layoutID = typing.layoutID else { return }
        let onArabic = layoutID == pair.arabic.id
        let current = onArabic ? pair.arabic : pair.latin
        let other = onArabic ? pair.latin : pair.arabic
        guard let languages = spellLanguages(latin: pair.latin) else {
            log.notice("no spell checker for both languages")
            return
        }
        let spell = NSSpellChecker.shared
        let fix = AutoFixer.decide(typing.items, current: current.map, other: other.map, currentIsArabic: onArabic) { word, arabic in
            spell.checkSpelling(of: word, startingAt: 0, language: arabic ? languages.arabic : languages.latin,
                                wrap: false, inSpellDocumentWithTag: 0, wordCount: nil).location == NSNotFound
        }
        let words = typing.items.filter { $0 == .space }.count + 1
        let app = NSWorkspace.shared.frontmostApplication?.bundleIdentifier ?? "-"
        log.notice("pause: \(self.typing.items.count, privacy: .public) keys, \(words, privacy: .public) words on \(onArabic ? "Arabic" : "Latin", privacy: .public) in \(app, privacy: .public), fix: \(fix.map { "delete \($0.deleteCount), type \($0.text.count)" } ?? "none", privacy: .public)")
        guard let fix else { return }
        typing.reset()
        // Without permission to send keys the letters can't be retyped; switching the keyboard alone
        // would only make things worse.
        guard CGPreflightPostEventAccess() else {
            log.error("can't send keys: this copy of KeySwap isn't allowed in Accessibility")
            return
        }
        for _ in 0..<fix.deleteCount { postKey(CGKeyCode(kVK_Delete), flags: []) }
        typeText(fix.text)
        other.select()
    }

    /// The spell checker languages for both keyboards, or nil if the Mac can't check one of them
    /// (then auto-fix stays off rather than guessing).
    private func spellLanguages(latin: SystemLayout) -> (latin: String, arabic: String)? {
        let available = NSSpellChecker.shared.availableLanguages
        func pick(_ language: String) -> String? {
            available.first { $0 == language } ?? available.first { $0.hasPrefix(language) }
        }
        guard let arabic = pick("ar"), let latinLanguage = pick(latin.language ?? "en") ?? pick("en") else { return nil }
        return (latinLanguage, arabic)
    }

    private func typeText(_ text: String) {
        let source = CGEventSource(stateID: .hidSystemState)
        for character in text {
            let units = Array(String(character).utf16)
            for isDown in [true, false] {
                guard let event = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: isDown) else { continue }
                event.flags = []
                event.keyboardSetUnicodeString(stringLength: units.count, unicodeString: units)
                event.setIntegerValueField(.eventSourceUserData, value: ownEventMarker)
                event.post(tap: .cghidEventTap)
            }
            usleep(1000) // keep the letters in order in slower apps
        }
    }

    // MARK: Fixing the selected text

    @objc private func fixSelection() {
        guard !busy else { return }
        guard AXIsProcessTrusted(), CGPreflightPostEventAccess() else { showPermissionHelp(); return }
        guard let pair = Layouts.pair() else { showLayoutHelp(); return }
        busy = true
        typing.reset()
        pauseTimer?.invalidate()

        let pasteboard = NSPasteboard.general
        let saved = savePasteboard(pasteboard)
        let countBefore = pasteboard.changeCount
        postShortcut(key: CGKeyCode(kVK_ANSI_C))

        waitUntil({ pasteboard.changeCount != countBefore }, timeout: 0.8) { [weak self] copied in
            guard let self else { return }
            guard copied, let text = pasteboard.string(forType: .string), !text.isEmpty else {
                if copied { self.restorePasteboard(pasteboard, saved) }
                self.busy = false
                NSSound.beep() // nothing was selected
                return
            }
            let converter = Converter(latin: pair.latin.map, arabic: pair.arabic.map)
            let result = converter.convert(text)
            pasteboard.clearContents()
            pasteboard.setString(result.text, forType: .string)
            postShortcut(key: CGKeyCode(kVK_ANSI_V))
            if self.switchKeyboard {
                (result.toArabic ? pair.arabic : pair.latin).select()
            }
            // Give the app time to read the pasted text before putting the user's clipboard back.
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                self.restorePasteboard(pasteboard, saved)
                self.busy = false
            }
        }
    }

    private func postShortcut(key: CGKeyCode) { postKey(key, flags: .maskCommand) }

    private func postKey(_ key: CGKeyCode, flags: CGEventFlags) {
        let source = CGEventSource(stateID: .hidSystemState)
        for isDown in [true, false] {
            let event = CGEvent(keyboardEventSource: source, virtualKey: key, keyDown: isDown)
            event?.flags = flags
            event?.setIntegerValueField(.eventSourceUserData, value: ownEventMarker)
            event?.post(tap: .cghidEventTap)
        }
    }

    private func waitUntil(_ condition: @escaping () -> Bool, timeout: Double, done: @escaping (Bool) -> Void) {
        let deadline = Date().addingTimeInterval(timeout)
        func poll() {
            if condition() { done(true); return }
            if Date() > deadline { done(false); return }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.02, execute: poll)
        }
        poll()
    }

    private func savePasteboard(_ pasteboard: NSPasteboard) -> [[NSPasteboard.PasteboardType: Data]] {
        (pasteboard.pasteboardItems ?? []).map { item in
            var copy: [NSPasteboard.PasteboardType: Data] = [:]
            for type in item.types { copy[type] = item.data(forType: type) }
            return copy
        }
    }

    private func restorePasteboard(_ pasteboard: NSPasteboard, _ items: [[NSPasteboard.PasteboardType: Data]]) {
        pasteboard.clearContents()
        let restored: [NSPasteboardItem] = items.map { dict in
            let item = NSPasteboardItem()
            for (type, data) in dict { item.setData(data, forType: type) }
            return item
        }
        if !restored.isEmpty { pasteboard.writeObjects(restored) }
    }

    // MARK: Menu

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let header = NSMenuItem(title: "Double-tap ⇧ Shift to fix selected text", action: nil, keyEquivalent: "")
        header.isEnabled = false
        menu.addItem(header)
        menu.addItem(item("Fix Selected Text Now", #selector(fixSelection)))
        menu.addItem(.separator())
        menu.addItem(item("Enabled", #selector(toggleEnabled), on: enabled))
        menu.addItem(item("Fix While Typing (after a short pause)", #selector(toggleAutoFix), on: autoFix))
        menu.addItem(item("Switch Keyboard After Fixing", #selector(toggleSwitch), on: switchKeyboard))
        if #available(macOS 13.0, *) {
            menu.addItem(item("Open at Login", #selector(toggleLogin), on: SMAppService.mainApp.status == .enabled))
        }
        menu.addItem(.separator())
        if let pair = Layouts.pair() {
            let info = NSMenuItem(title: "Keyboards: \(pair.latin.name) ⇄ \(pair.arabic.name)", action: nil, keyEquivalent: "")
            info.isEnabled = false
            menu.addItem(info)
        } else {
            menu.addItem(item("⚠︎ Add an Arabic and an English keyboard…", #selector(showLayoutHelp)))
        }
        if !AXIsProcessTrusted() || !CGPreflightPostEventAccess() {
            menu.addItem(item("⚠︎ Allow Accessibility Access…", #selector(showPermissionHelp)))
        }
        menu.addItem(.separator())
        menu.addItem(item("Quit KeySwap", #selector(quit), key: "q"))
    }

    private func item(_ title: String, _ action: Selector, on: Bool? = nil, key: String = "") -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self
        if let on { item.state = on ? .on : .off }
        return item
    }

    @objc private func toggleEnabled() { enabled.toggle() }
    @objc private func toggleSwitch() { switchKeyboard.toggle() }
    @objc private func toggleAutoFix() { autoFix.toggle(); typing.reset() }
    @objc private func quit() { NSApp.terminate(nil) }

    @objc private func toggleLogin() {
        guard #available(macOS 13.0, *) else { return }
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            alert("Couldn't change Open at Login",
                  "Move KeySwap to your Applications folder and try again.\n\n\(error.localizedDescription)")
        }
    }

    @objc private func showPermissionHelp() {
        alert("KeySwap needs Accessibility access",
              "To copy and replace the selected text, KeySwap needs permission.\n\nOpen System Settings → Privacy & Security → Accessibility, then turn on KeySwap.")
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
    }

    @objc private func showLayoutHelp() {
        alert("Add both keyboards",
              "KeySwap converts between your Arabic keyboard and your English keyboard.\n\nOpen System Settings → Keyboard → Text Input → Edit, and make sure both an Arabic and an English keyboard are listed.")
    }

    private func alert(_ title: String, _ text: String) {
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = text
        alert.runModal()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
