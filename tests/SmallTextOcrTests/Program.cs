using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PDFtoImage;
using SkiaSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System.Text.Json;
using System.Diagnostics;
#pragma warning disable CA1416
var evidence = Path.GetFullPath(args[1]); Directory.CreateDirectory(evidence);
GlobalFontSettings.FontResolver ??= new AppFontResolver();
var checks = new List<string>();
void Check(bool value, string message) { if (!value) throw new Exception(message); }
byte[] Render(string path) {
 using var stream=File.OpenRead(path);
 using var bitmap=Conversion.ToImages(stream,new[]{0},options:new RenderOptions(Dpi:100,WithAnnotations:true,BackgroundColor:SKColors.White)).First();return bitmap.Bytes;
}
var phrases=new[]{"建築物の色彩の変更","工作物の撤去","土地の開墾","木竹の伐採","通常の管理行為を除く","基本的には不要","個別の状況により判断","国の機関、地方公共団体"};
var vector=Path.Combine(evidence,"small-vector.pdf");
using(var document=new PdfDocument()) {
 var page=document.AddPage();page.Size=PdfSharp.PageSize.A4;
 using(var g=XGraphics.FromPdfPage(page)) {
  var title=new XFont("OfflinePDFConverterOcr",12);var small=new XFont("OfflinePDFConverterOcr",6);
  g.DrawString("小さい日本語の認識テスト",title,XBrushes.Black,new XPoint(50,65));
  for(var i=0;i<phrases.Length;i++) {
   var y=110+i*42;
   g.DrawRectangle(new XPen(XColors.Black,0.5),45,y-15,505,42);
   g.DrawLine(new XPen(XColors.Black,0.5),280,y-15,280,y+27);
   g.DrawString(phrases[i],small,XBrushes.Black,new XPoint(50,y));
   g.DrawString("手続きの要否を確認してください",small,XBrushes.Blue,new XPoint(290,y));
  }
 }
 document.Save(vector);
}
var raster=Path.Combine(evidence,"small-body.png");
using(var stream=File.OpenRead(vector)) using(var bitmap=Conversion.ToImages(stream,new[]{0},options:new RenderOptions(Dpi:300,BackgroundColor:SKColors.White)).First())
 using(var image=File.Create(raster)) bitmap.Encode(image,SKEncodedImageFormat.Png,100);
var input=Path.Combine(evidence,"small-with-page-number.pdf");
using(var document=new PdfDocument()) {
 var page=document.AddPage();page.Size=PdfSharp.PageSize.A4;
 using(var g=XGraphics.FromPdfPage(page)) using(var image=XImage.FromFile(raster)) {
  g.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
  g.DrawString("2",new XFont("OfflinePDFConverterOcr",10),XBrushes.Black,new XPoint(295,825));
 }
 document.Save(input);
}
using(var before=UglyToad.PdfPig.PdfDocument.Open(input)) Check(before.GetPage(1).Text=="2","fixture must have only a digital page number");
var output=Path.Combine(evidence,"small-searchable.pdf");
var service=new OfflineOcrService();var progress=new SilentProgress();var watch=Stopwatch.StartNew();
await service.CreateSearchablePdfBundledAsync(input,output,"jpn","","",progress,default);
string text;
using(var result=UglyToad.PdfPig.PdfDocument.Open(output)) {
 text=result.GetPage(1).Text;
 foreach(var phrase in phrases) Check(text.Contains(phrase),"6 pt Japanese phrase missing: "+phrase+" / "+text);
 Check(text.Contains("手続きの要否を確認してください"),"blue text missing");
 Check(result.GetPage(1).Letters.Count(l=>l.Value=="2")==1,"page number duplicated");
 Check(result.GetPage(1).Letters.Any(l=>l.FontName?.Replace(" ","").Contains("NotoSansJP",StringComparison.OrdinalIgnoreCase)==true),"OCR text layer must use Noto Sans Japanese");
}
checks.Add("6 pt Japanese table cells, blue text and digital footer recognized");
Check(Render(input).SequenceEqual(Render(output)),"page appearance changed");checks.Add("pixel-identical appearance at 100 dpi");
var repeated=Path.Combine(evidence,"repeated.pdf");var called=false;
await SearchablePdfService.CreateAsync(output,repeated,"jpn","","",progress,default,(_,_)=>{called=true;throw new Exception("complete text page was OCRed again");});
Check(!called,"existing complete text layer not preserved");
using(var result=UglyToad.PdfPig.PdfDocument.Open(repeated)) Check(result.GetPage(1).Text==text,"repeated OCR duplicated text");
checks.Add("complete text layer retained without repeating OCR or duplicating text");
var digital=Path.Combine(evidence,"digital-body.pdf");
using(var doc=new PdfDocument()) {var p=doc.AddPage();using(var g=XGraphics.FromPdfPage(p))g.DrawString("日本語",new XFont("OfflinePDFConverterOcr",10),XBrushes.Black,new XPoint(50,300));doc.Save(digital);}
await SearchablePdfService.CreateAsync(digital,Path.Combine(evidence,"digital-preserved.pdf"),"jpn","","",progress,default,(_,_)=>throw new Exception("digital body should be preserved"));
checks.Add("short existing digital body preserved");
if(args.Length>2) {
 var sample=Path.GetFullPath(args[2]);var resultPath=Path.Combine(evidence,"uploaded-sample-searchable.pdf");
 await service.CreateSearchablePdfBundledAsync(sample,resultPath,"jpn","","",progress,default);
 using var result=UglyToad.PdfPig.PdfDocument.Open(resultPath);var actual=result.GetPage(1).Text;
 foreach(var phrase in phrases) Check(actual.Contains(phrase),"uploaded sample phrase missing: "+phrase);
 File.WriteAllText(Path.Combine(evidence,"uploaded-sample-text.txt"),actual);
 Check(Render(sample).SequenceEqual(Render(resultPath)),"uploaded sample appearance changed");
 Check(result.GetPage(1).Letters.Count(l=>l.Value=="2" && l.BoundingBox.Top<80)==1,"uploaded page number duplicated");
 checks.Add("uploaded sample: eight Japanese phrases, footer not duplicated, pixel-identical appearance");
}
File.WriteAllText(Path.Combine(evidence,"checks.json"),JsonSerializer.Serialize(new{passed=true,checks,seconds=watch.Elapsed.TotalSeconds},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("PASS "+string.Join("\nPASS ",checks));
PaddleOcrService.Cleanup();
sealed class SilentProgress:IProgress<ConversionProgress>{public void Report(ConversionProgress value){}}
