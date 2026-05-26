import AppKit

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let input = root.appendingPathComponent("offline-pdf-image-converter/test-samples/input")
try FileManager.default.createDirectory(at: input, withIntermediateDirectories: true)

func drawImage(size: NSSize, title: String, color: NSColor, path: URL, type: NSBitmapImageRep.FileType) throws {
    let image = NSImage(size: size)
    image.lockFocus()
    color.setFill()
    NSBezierPath(rect: NSRect(origin: .zero, size: size)).fill()
    NSColor.white.setFill()
    NSBezierPath(roundedRect: NSRect(x: 30, y: 30, width: size.width - 60, height: size.height - 60), xRadius: 18, yRadius: 18).fill()
    let attributes: [NSAttributedString.Key: Any] = [
        .font: NSFont.systemFont(ofSize: 32, weight: .semibold),
        .foregroundColor: NSColor.black
    ]
    title.draw(at: NSPoint(x: 58, y: size.height / 2 - 18), withAttributes: attributes)
    image.unlockFocus()

    guard
        let tiff = image.tiffRepresentation,
        let bitmap = NSBitmapImageRep(data: tiff),
        let data = bitmap.representation(using: type, properties: [.compressionFactor: 0.92])
    else {
        throw NSError(domain: "CreateSamples", code: 1)
    }

    try data.write(to: path)
}

try drawImage(
    size: NSSize(width: 900, height: 600),
    title: "Sample Image 1",
    color: NSColor(calibratedRed: 0.05, green: 0.42, blue: 0.74, alpha: 1),
    path: input.appendingPathComponent("sample_image_01.png"),
    type: .png)

try drawImage(
    size: NSSize(width: 900, height: 600),
    title: "Sample Image 2",
    color: NSColor(calibratedRed: 0.14, green: 0.55, blue: 0.35, alpha: 1),
    path: input.appendingPathComponent("sample_image_02.jpg"),
    type: .jpeg)

let pdfPath = input.appendingPathComponent("sample_two_pages.pdf")
let pdfData = NSMutableData()
guard let consumer = CGDataConsumer(data: pdfData as CFMutableData),
      let context = CGContext(consumer: consumer, mediaBox: nil, nil) else {
    throw NSError(domain: "CreateSamples", code: 2)
}

for page in 1...2 {
    var mediaBox = CGRect(x: 0, y: 0, width: 595, height: 842)
    context.beginPDFPage([kCGPDFContextMediaBox as String: NSData(bytes: &mediaBox, length: MemoryLayout<CGRect>.size)] as CFDictionary)
    context.setFillColor(NSColor.white.cgColor)
    context.fill(mediaBox)
    context.setFillColor(page == 1 ? NSColor.systemBlue.cgColor : NSColor.systemGreen.cgColor)
    context.fill(CGRect(x: 60, y: 560, width: 475, height: 180))
    let text = "Sample PDF Page \(page)" as NSString
    text.draw(at: CGPoint(x: 80, y: 630), withAttributes: [
        .font: NSFont.systemFont(ofSize: 34, weight: .bold),
        .foregroundColor: NSColor.white
    ])
    context.endPDFPage()
}
context.closePDF()
try pdfData.write(to: pdfPath)

print("Created samples in \(input.path)")
