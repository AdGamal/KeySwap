# What's new

## 1.2.0
Fewer wrong swaps, measured on 241 real English and Arabic sentences at every point you could pause (5,670 in all): correct text is now never changed.
- **Pausing in the middle of an English word** no longer turns it into Arabic ("hel" → اثم). A half-typed word that starts like an English word is left alone until you finish it.
- **Arabic words with ك ج د ز ط** typed on the English keyboard are now caught. Those letters sit on punctuation keys (`;` `[` `]` `.` `'`), which used to hide the word from the check.
- **Sentences are fixed whole**, not just the ending, when they start with a name, a typo or a shortcut ("ahmed is meeting…", "pls send me…").
- Days, months and names typed without a capital (monday, cairo) count as English.
- More everyday contractions (he's, didn't, we'll…) and app names are recognised.

## 1.1.4
- English chat shortcuts and names stay English even when their letters spell Arabic on the other keyboard (tmr, hmu, gps, ios, lol, brb, idk, asap…). Words in capitals (OK, ASAP) stay English too.
- Inside an Arabic sentence typed on the wrong keyboard, a shortcut is still fixed together with the sentence.

## 1.1.3
- KeySwap never switches the keyboard unless it's allowed to retype the text. If Accessibility access is missing, the menu shows a warning instead.
- Clearer log of what was fixed, to make problems easier to track down.

## 1.1.2
- **Fix while typing:** pause for a moment and words typed with the wrong keyboard are replaced, then the keyboard switches. Never in password fields.
- **Starts by itself** when you turn on your computer (turn it off from the menu).
- Everyday words count more than the spell checker, so short English words typed on the Arabic keyboard (how, and, that…) are still caught, and Arabic typed right is left alone.
- Only one KeySwap runs at a time.

## 1.0.1
- First release: select text and double-tap Shift to retype it on the other keyboard, for Mac and Windows.
