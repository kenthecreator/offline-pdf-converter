using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;
using PDFtoImage;
using SkiaSharp;
using System.Text.Json;

#pragma warning disable CA1416
var root = Path.GetFullPath(args[0]);
var evidence = Path.GetFullPath(args[1]); Directory.CreateDirectory(evidence);
GlobalFontSettings.FontResolver ??= new AppFontResolver();
var input = Path.Combine(root, "docs/paddle-edition-results/evaluation-app.pdf");
var output = Path.Combine(evidence, "searchable.pdf");
var service = new OfflineOcrService(); var progress = new SilentProgress();
var checks = new List<string>();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
byte[] Render(string file, int page) {
 using var stream = File.OpenRead(file);
 using var bitmap = Conversion.ToImages(stream, new[] { page - 1 }, options: new RenderOptions(Dpi:100, WithAnnotations:true, BackgroundColor:SKColors.White)).First();
 return bitmap.Bytes;
}
var result = await service.CreateSearchablePdfBundledAsync(input, output, "jpn+eng", "1-5,7-9", "", progress, default);
Check(!result.HasErrors && result.CreatedFiles == 1, "failed PDF output");
using (var text = UglyToad.PdfPig.PdfDocument.Open(output)) {
 Check(text.NumberOfPages == 9, "page count changed");
 Check(text.GetPage(1).Text.Contains("日本語"), "Japanese text missing: " + text.GetPage(1).Text);
 Check(text.GetPage(8).Text.Contains("Invoice"), "English text missing");
 Check(text.GetPage(6).Letters.Count == 0, "unselected page modified");
 Check(text.GetPage(9).Letters.Count == 0, "blank page contains invented text");
 Check(text.GetPage(1).Letters.All(l => l.BoundingBox.Left >= 0 && l.BoundingBox.Top <= text.GetPage(1).Height+1), "text outside page");
}
checks.Add("Japanese/English searchable text, all pages retained, selection and blank page");
for(var page=1;page<=9;page++) Check(Render(input,page).SequenceEqual(Render(output,page)), "page appearance changed: " + page);
checks.Add("all nine pages render pixel-identically at 100 dpi");
var vertical = Path.Combine(evidence,"vertical.pdf");
await service.CreateSearchablePdfBundledAsync(input,vertical,"jpn_vert","6","",progress,default);
using(var text=UglyToad.PdfPig.PdfDocument.Open(vertical)) Check(text.GetPage(6).Text.Contains("日本語"),"vertical text missing");
Check(Render(input,6).SequenceEqual(Render(vertical,6)),"vertical appearance changed");
checks.Add("vertical Japanese searchable and visually unchanged");
var repeated=Path.Combine(evidence,"repeated.pdf");
await service.CreateSearchablePdfBundledAsync(output,repeated,"jpn+eng","1","",progress,default);
using(var a=UglyToad.PdfPig.PdfDocument.Open(output)) using(var b=UglyToad.PdfPig.PdfDocument.Open(repeated))
 Check(a.GetPage(1).Text==b.GetPage(1).Text,"existing text duplicated");
checks.Add("existing text layer preserved without duplication");
foreach (var (profile, number, expected) in new[] { ("jpn", 1, "日本語"), ("eng", 8, "Invoice"), ("jpn+eng", 5, "PDF") }) {
 var file=Path.Combine(evidence,"profile-"+profile.Replace("+","-")+".pdf");
 await service.CreateSearchablePdfBundledAsync(input,file,profile,number.ToString(),"",progress,default);
 using var doc=UglyToad.PdfPig.PdfDocument.Open(file);
 Check(doc.GetPage(number).Text.Contains(expected),"language profile failed: "+profile+" / "+doc.GetPage(number).Text);
 Check(Render(input,number).SequenceEqual(Render(file,number)),"language profile altered appearance");
}
using(var mixed=UglyToad.PdfPig.PdfDocument.Open(Path.Combine(evidence,"profile-jpn-eng.pdf")))
 Check(mixed.GetPage(5).Text.Contains("Invoice No."),"spaces between English words lost");
checks.Add("Japanese, English and mixed profiles switch recognizers and create searchable PDFs");
var original=File.ReadAllBytes(output);
var numbered = await service.CreateSearchablePdfBundledAsync(input,output,"jpn+eng","1","",progress,default);
Check(numbered.OutputPaths.Count == 1 && numbered.OutputPaths[0] != output && File.Exists(numbered.OutputPaths[0]), "numbered output path not returned to preview");
Check(original.SequenceEqual(File.ReadAllBytes(output)),"existing output overwritten");
checks.Add("existing output not overwritten");
var cancelled=Path.Combine(evidence,"cancelled.pdf");
using(var cts=new CancellationTokenSource()) {
 try {
  await SearchablePdfService.CreateAsync(input,cancelled,"jpn+eng","1","",progress,cts.Token,
   (_,_)=> { cts.Cancel(); return Task.FromResult<IReadOnlyList<OcrTextBlock>>(Array.Empty<OcrTextBlock>()); });
  throw new Exception("cancellation ignored");
 } catch(OperationCanceledException) {}
 Check(!File.Exists(cancelled),"partial output left after cancellation");
}
checks.Add("in-flight cancellation leaves no output");
var encrypted=Path.Combine(evidence,"encrypted.pdf");
using(var doc=PdfReader.Open(input,PdfDocumentOpenMode.Modify)) {doc.SecuritySettings.UserPassword="test";doc.SecuritySettings.OwnerPassword="owner";doc.Save(encrypted);}
var decrypted=Path.Combine(evidence,"encrypted-ocr.pdf");
await service.CreateSearchablePdfBundledAsync(encrypted,decrypted,"jpn+eng","1","test",progress,default);
using(var doc=UglyToad.PdfPig.PdfDocument.Open(decrypted,new UglyToad.PdfPig.ParsingOptions {Password="test"}))
 Check(doc.GetPage(1).Text.Contains("日本語"),"encrypted PDF OCR failed");
checks.Add("password-protected input processed");
File.WriteAllText(Path.Combine(evidence,"checks.json"),JsonSerializer.Serialize(new {passed=true,checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PASS " + string.Join("\nPASS ",checks));
PaddleOcrService.Cleanup();
sealed class SilentProgress : IProgress<ConversionProgress> {public void Report(ConversionProgress value) {}}
