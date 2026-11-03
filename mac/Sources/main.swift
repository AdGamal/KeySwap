import Cocoa
import Carbon
import ServiceManagement

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private let detector = DoubleShiftDetector()
    private var monitors: [Any] = []
    private var trustTimer: Timer?
    private var busy = false
    private let defaults = UserDefaults.standard

    private var enabled: Bool {
        get { defaults.object(forKey: "enabled") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "enabled") }
    }
    private var switchKeyboard: Bool {
        get { defaults.object(forKey: "switchKeyboard") as? Bool ?? true }
        set { defaults.set(newValue, forKey: "switchKeyboard") }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
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

        if AXIsProcessTrusted() {
            installMonitors()
        } else {
            let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
            AXIsProcessTrustedWithOptions(options)
            // Start listening as soon as the user grants access, without needing a restart.
            trustTimer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] timer in
                guard AXIsProcessTrusted() else { return }
                timer.invalidate()
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
        guard event.type == .flagsChanged else { detector.otherInput(); return }
        let leftShift: UInt16 = 56, rightShift: UInt16 = 60
        guard event.keyCode == leftShift || event.keyCode == rightShift else { detector.otherInput(); return }
        let flags = event.modifierFlags
        detector.shift(down: flags.contains(.shift),
                       otherModifiersHeld: !flags.intersection([.command, .option, .control]).isEmpty,
                       at: event.timestamp)
    }

    // MARK: Fixing the selected text

    @objc private func fixSelection() {
        guard !busy else { return }
        guard AXIsProcessTrusted() else { showPermissionHelp(); return }
        guard let pair = Layouts.pair() else { showLayoutHelp(); return }
        busy = true

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

    private func postShortcut(key: CGKeyCode) {
        let source = CGEventSource(stateID: .hidSystemState)
        for isDown in [true, false] {
            let event = CGEvent(keyboardEventSource: source, virtualKey: key, keyDown: isDown)
            event?.flags = .maskCommand
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
        if !AXIsProcessTrusted() {
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
