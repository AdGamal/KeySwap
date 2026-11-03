// Draws the app icon: two flat keyboard keys, "A" and "ع", on a solid tile. Saves PNGs for iconutil.
import AppKit
import CoreText

let outDir = CommandLine.arguments[1]

func color(_ hex: UInt32) -> NSColor {
    NSColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
}

let tile = color(0x111827)       // near-black ink
let capLight = color(0xFFFFFF)
let capLightEdge = color(0xCBD5E1)
let capAccent = color(0x10B981)  // emerald
let capAccentEdge = color(0x047857)
let inkOnLight = color(0x111827)

/// Draws text centred on its visible glyph bounds (not the font's line box), so both scripts sit level.
func drawCentered(_ string: String, font: CTFont, color: NSColor, in rect: NSRect, context: CGContext) {
    let attributes: [NSAttributedString.Key: Any] = [kCTFontAttributeName as NSAttributedString.Key: font,
                      kCTForegroundColorAttributeName as NSAttributedString.Key: color.cgColor]
    let line = CTLineCreateWithAttributedString(NSAttributedString(string: string, attributes: attributes))
    context.textMatrix = .identity
    let bounds = CTLineGetImageBounds(line, context)
    context.textPosition = CGPoint(x: rect.midX - bounds.midX, y: rect.midY - bounds.midY)
    CTLineDraw(line, context)
}

/// A flat keycap: a darker lip underneath, the face on top.
func drawKey(_ face: NSRect, faceColor: NSColor, edgeColor: NSColor, radius: CGFloat, lip: CGFloat) {
    edgeColor.setFill()
    NSBezierPath(roundedRect: face.offsetBy(dx: 0, dy: -lip), xRadius: radius, yRadius: radius).fill()
    faceColor.setFill()
    NSBezierPath(roundedRect: face, xRadius: radius, yRadius: radius).fill()
}

for (size, scale) in [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)] {
    let px = CGFloat(size * scale)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(px), pixelsHigh: Int(px), bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    let ns = NSGraphicsContext(bitmapImageRep: rep)!
    NSGraphicsContext.current = ns
    let ctx = ns.cgContext
    let u = px / 100 // design on a 100-unit grid

    // Tile, on Apple's icon grid (about 10% margin).
    tile.setFill()
    NSBezierPath(roundedRect: NSRect(x: 10 * u, y: 10 * u, width: 80 * u, height: 80 * u),
                 xRadius: 18 * u, yRadius: 18 * u).fill()

    // Back key "A" (top left), front key "ع" (bottom right), overlapping like a swap.
    let keySize = 36 * u, radius = 8 * u, lip = 3.5 * u
    let back = NSRect(x: 19 * u, y: 46 * u, width: keySize, height: keySize)
    let front = NSRect(x: 45 * u, y: 22 * u, width: keySize, height: keySize)

    drawKey(back, faceColor: capLight, edgeColor: capLightEdge, radius: radius, lip: lip)
    drawCentered("A", font: NSFont.systemFont(ofSize: 22 * u, weight: .semibold).rounded as CTFont,
                 color: inkOnLight, in: back, context: ctx)

    // A ring in the tile colour separates the front key from the one behind it.
    tile.setFill()
    NSBezierPath(roundedRect: front.insetBy(dx: -2.5 * u, dy: -2.5 * u).offsetBy(dx: 0, dy: -lip / 2)
                    .insetBy(dx: 0, dy: -lip / 2),
                 xRadius: radius + 2.5 * u, yRadius: radius + 2.5 * u).fill()
    drawKey(front, faceColor: capAccent, edgeColor: capAccentEdge, radius: radius, lip: lip)
    // The system UI font for Arabic (SF Arabic), at a semibold weight to match the "A".
    let arabicBase = CTFontCreateUIFontForLanguage(.system, 26 * u, "ar" as CFString)!
    let semibold = [kCTFontTraitsAttribute: [kCTFontWeightTrait: NSFont.Weight.semibold.rawValue]] as CFDictionary
    let arabicFont = CTFontCreateCopyWithAttributes(arabicBase, 26 * u, nil,
                                                    CTFontDescriptorCreateWithAttributes(semibold))
    drawCentered("ع", font: arabicFont, color: capLight, in: front, context: ctx)

    NSGraphicsContext.restoreGraphicsState()
    let name = scale == 1 ? "icon_\(size)x\(size).png" : "icon_\(size)x\(size)@2x.png"
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(outDir)/\(name)"))
}

extension NSFont {
    var rounded: NSFont {
        guard let descriptor = fontDescriptor.withDesign(.rounded) else { return self }
        return NSFont(descriptor: descriptor, size: pointSize) ?? self
    }
}
