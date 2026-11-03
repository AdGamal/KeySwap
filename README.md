<p align="center"><img src="docs/icon.png" width="128" alt="KeySwap icon"></p>

# KeySwap

Typed in the wrong language? Select the text and **double-tap Shift**. KeySwap retypes it with the other keyboard.

| You typed | You meant | After double-tap Shift |
|---|---|---|
| `lnpfh` (Mac) / `lvpfh` (Windows) | مرحبا | `مرحبا` |
| `اثممخ صخقمي` | hello world | `hello world` |

- Works in every app: browsers, Word, WhatsApp, Slack, Notes, anything you can type in.
- Detects the direction automatically: Arabic → English or English → Arabic.
- Switches your keyboard to the right language afterwards, so you can keep typing (you can turn this off).
- Reads your real keyboard layouts from the system, so it matches your exact Arabic keyboard (Mac Arabic, Arabic – PC, Arabic 101, 102…).
- Keeps your clipboard: whatever you had copied is put back.
- Free and open source. Runs offline and sends nothing anywhere.

## Download

Get the latest version from the **Releases** page:

- **Mac:** `KeySwap-mac.zip` (Apple Silicon and Intel, macOS 11 or later)
- **Windows:** `KeySwap.exe` (Windows 10 and 11)

## Install on Mac

1. Unzip and drag **KeySwap** into your **Applications** folder.
2. Open it. macOS will say it can't verify the developer, because the app isn't sold through Apple. Click **Done**, then open **System Settings → Privacy & Security**, scroll down, and click **Open Anyway** next to KeySwap.
3. When asked, allow **Accessibility** access (System Settings → Privacy & Security → Accessibility → turn on KeySwap). KeySwap needs it to copy and replace the selected text.
4. The `A⇄ع` icon appears in the menu bar. Turn on **Open at Login** from its menu if you like.

## Install on Windows

1. Download `KeySwap.exe` and double-click it.
2. Windows SmartScreen may say "Windows protected your PC", because the app isn't signed with a paid certificate. Click **More info → Run anyway**.
3. The KeySwap icon appears in the system tray (near the clock; it may be under the `^` arrow). Right-click it and turn on **Start with Windows** if you like.

## Use

1. Select the text you typed in the wrong language (Cmd+A / Ctrl+A to select everything).
2. Press and release **Shift** twice, quickly.

To undo, press Cmd+Z / Ctrl+Z as usual.

You need both an Arabic and an English keyboard added on your computer:
**Mac:** System Settings → Keyboard → Text Input → Edit.
**Windows:** Settings → Time & language → Language & region.

## Good to know

- Double-tapping Shift only counts when you press nothing else in between, so typing capital letters never triggers it.
- On Windows, KeySwap can't fix text in programs running as administrator unless KeySwap also runs as administrator.
- If nothing is selected, you'll hear a beep and nothing changes. (A few code editors copy the whole line when nothing is selected; select the text first.)
- On the Windows Arabic keyboard, B types `لا`, the same as G then H. KeySwap picks `b` unless `gh` spells a common English word (light, through, enough…).

---

## بالعربي

كتبت بلغة غلط؟ حدّد النص واضغط **Shift مرتين بسرعة**، وKeySwap يعيد كتابته بالكيبورد الصح.

**على الماك:** فك الضغط عن `KeySwap-mac.zip` وانقل KeySwap لمجلد Applications. افتحه، ولو ظهرت رسالة إن الماك مش قادر يتحقق من المطوّر، روح **System Settings ← Privacy & Security** واضغط **Open Anyway**. بعدها فعّل صلاحية **Accessibility** لـ KeySwap.

**على الويندوز:** شغّل `KeySwap.exe`. لو ظهرت رسالة "Windows protected your PC" اضغط **More info ← Run anyway**. هتلاقي الأيقونة جنب الساعة.

**الاستخدام:** حدّد النص (Cmd+A أو Ctrl+A) واضغط Shift مرتين. KeySwap يعرف لوحده إذا كان النص عربي أو إنجليزي، ويغيّر الكيبورد للغة الصح بعدها. للتراجع: Cmd+Z أو Ctrl+Z.

---

## Build it yourself

- **Mac:** `./mac/build.sh` (needs Xcode Command Line Tools). Runs the tests, then makes `mac/build/KeySwap.app` and `KeySwap-mac.zip`.
- **Windows:** double-click `windows\build.bat`. It uses the C# compiler built into Windows, so there's nothing to install. Runs the tests, then makes `windows\build\KeySwap.exe`.
- **Automatic:** every push to GitHub builds and tests both. Pushing a tag like `v1.0.0` publishes a release with both downloads.
