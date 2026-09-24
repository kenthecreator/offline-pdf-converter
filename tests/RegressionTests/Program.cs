using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using Avalonia;
using Avalonia.Input;

var root = Path.Combine(Path.GetTempPath(), "pdf-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failed = 0;
var passed = 0;
var progress = new ImmediateProgress(_ => { });
var passwords = new Dictionary<string,string>();
GlobalFontSettings.FontResolver = new AppFontResolver();
string Folder(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Test(string name, Func<Task> test) { try { await test(); passed++; Console.WriteLine("PASS " + name); } catch(Exception ex) { failed++; Console.WriteLine("FAIL " + name + "\n" + ex); } }
void Expect<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
string Pdf(string name, int pages, string? password = null, bool text = false) {
 var file = Path.Combine(root,name+".pdf"); using var doc = new PdfDocument();
 for(var i=1;i<=pages;i++) { var page=doc.AddPage(); page.Width=XUnit.FromPoint(400+i); page.Height=XUnit.FromPoint(600);
 if(text) { using var graphics=XGraphics.FromPdfPage(page); var font=new XFont("OfflinePDFConverterGothic",20); graphics.DrawString("日本語の書類確認 ABC 12345",font,XBrushes.Black,new XPoint(30,80)); } }
 if(password!=null) { doc.SecuritySettings.UserPassword=password; doc.SecuritySettings.OwnerPassword="owner"; }
 doc.Save(file); return file;
}
if(args.Length==2 && args[0]=="--fixture") {
 Directory.CreateDirectory(args[1]); File.Copy(Pdf("sample",2,text:true),Path.Combine(args[1],"sample.pdf"),true);
 File.WriteAllText(Path.Combine(args[1],"broken.pdf"),"not a pdf"); Directory.Delete(root,true); return 0;
}
try {
var gestureCharacters = new List<PdfSelectableWord>();
var nextIndex = 0;
void AddWord(string value, int wordIndex, double left, double top) {
 for (var i = 0; i < value.Length; i++)
  gestureCharacters.Add(new PdfSelectableWord(nextIndex++, wordIndex, value[i].ToString(), left + i * 10, top, 10, 12));
}
AddWord("Hello", 0, 0, 0);
AddWord("world", 1, 60, 0);
AddWord("Second", 2, 0, 24);
AddWord("line", 3, 70, 24);
int Hit(double x, double y) => PdfTextSelectionLogic.FindCharacter(gestureCharacters, new Point(x,y)) ?? throw new Exception("No text hit");
await Test("text click and short movement never create a selection",()=>{
 var hit=Hit(22,6);
 Check(PdfTextSelectionLogic.ApplyGesture(gestureCharacters,Array.Empty<int>(),hit,hit,null,false,false,false).Count==0,"plain click selected text");
 Check(!PdfTextSelectionLogic.IsDrag(new Point(20,6),new Point(24.9,6),1),"short movement became drag");
 Check(PdfTextSelectionLogic.ApplyGesture(gestureCharacters,Enumerable.Range(0,5).ToArray(),hit,hit,0,false,false,false).Count==0,"short movement did not act as a plain click");
 Check(PdfTextSelectionLogic.IsDrag(new Point(20,6),new Point(25,6),1),"five pixel drag was ignored");
 Check(!PdfTextSelectionLogic.IsDrag(new Point(20,6),new Point(22,6),2),"zoomed short movement became drag");
 Check(PdfTextSelectionLogic.IsDrag(new Point(20,6),new Point(22.5,6),2),"zoomed five pixel drag was ignored");
 return Task.CompletedTask;
});
await Test("text selection drag follows same-line endpoints",()=>{
 var selected=PdfTextSelectionLogic.SelectRange(gestureCharacters,Hit(22,6),Hit(102,6));
 Check(selected.SequenceEqual(Enumerable.Range(2,8)),"same-line range has gaps or extra characters");
 return Task.CompletedTask;
});
await Test("text selection drag spans lines and works backwards",()=>{
 var forward=PdfTextSelectionLogic.SelectRange(gestureCharacters,Hit(82,6),Hit(32,30));
 var backward=PdfTextSelectionLogic.SelectRange(gestureCharacters,Hit(32,30),Hit(82,6));
 Check(forward.SequenceEqual(Enumerable.Range(7,7)) && backward.SequenceEqual(forward),"multi-line selection differs by direction");
 return Task.CompletedTask;
});
await Test("blank click clears selection but modified blank click preserves it",()=>{
 var blank=PdfTextSelectionLogic.HitCharacter(gestureCharacters,new Point(300,200));
 Check(blank==null,"page whitespace hit a character");
 var old=Enumerable.Range(0,5).ToArray();
 Check(PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,blank,blank,0,false,false,false).Count==0,"plain blank click did not clear");
 Check(PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,blank,blank,0,false,true,false).SequenceEqual(old),"modified blank click changed selection");
 return Task.CompletedTask;
});
await Test("plain text click clears selection while modified clicks preserve it",()=>{
 var old=Enumerable.Range(0,5).ToArray();
 var plain=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(72,6),Hit(72,6),0,false,false,false);
 var onSelected=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(22,6),Hit(22,6),0,false,false,false);
 var added=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(72,6),Hit(72,6),0,false,true,false);
 var shifted=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(72,6),Hit(72,6),0,false,false,true);
 var combined=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(72,6),Hit(72,6),0,false,true,true);
 Check(plain.Count==0&&onSelected.Count==0,"plain text click did not clear selection");
 Check(added.SequenceEqual(old)&&shifted.SequenceEqual(old)&&combined.SequenceEqual(old),"modified click changed selection");
 Check(PdfTextSelectionLogic.IsAdditiveModifier(KeyModifiers.Meta,true),"Mac Command is not additive");
 Check(!PdfTextSelectionLogic.IsAdditiveModifier(KeyModifiers.Control,true),"Mac Control incorrectly adds");
 Check(PdfTextSelectionLogic.IsAdditiveModifier(KeyModifiers.Control,false),"Windows Ctrl is not additive");
 Check(!PdfTextSelectionLogic.IsAdditiveModifier(KeyModifiers.Meta,false),"Windows Meta incorrectly adds");
 return Task.CompletedTask;
});
await Test("normal drag replaces an earlier selection",()=>{
 var old=Enumerable.Range(0,5).ToArray();
 var selection=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(72,6),Hit(102,6),0,true,false,false);
 Check(selection.SequenceEqual(Enumerable.Range(6,4)),"normal drag did not replace");
 return Task.CompletedTask;
});
await Test("modified drag adds a distant range without filling the gap",()=>{
 var old=Enumerable.Range(0,5).ToArray();
 var selection=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(12,30),Hit(32,30),0,true,true,false);
 Check(selection.SequenceEqual(old.Concat(Enumerable.Range(11,3))),"modified drag selected gap or lost old selection");
 return Task.CompletedTask;
});
await Test("Shift click preserves selection and Shift drag extends from previous anchor",()=>{
 var old=Enumerable.Range(0,5).ToArray();
 var click=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(12,30),Hit(12,30),0,false,false,true);
 var drag=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(72,30),Hit(102,30),0,true,false,true);
 Check(click.SequenceEqual(old),"Shift click unexpectedly selected text");
 Check(drag.SequenceEqual(Enumerable.Range(0,20)),"Shift drag did not follow endpoint");
 return Task.CompletedTask;
});
await Test("Command or Ctrl plus Shift adds an extended range",()=>{
 var old=new[]{0,1,2,18,19};
 var selection=PdfTextSelectionLogic.ApplyGesture(gestureCharacters,old,Hit(32,30),Hit(52,30),10,true,true,true);
 Check(selection.SequenceEqual(new[]{0,1,2}.Concat(Enumerable.Range(10,6)).Concat(new[]{18,19})),"combined modifiers lost selection or gap");
 return Task.CompletedTask;
});
await Test("embedded OCR extracts without installed software",()=>{using var runtime=BundledOcrRuntime.ExtractEmbedded();Check(File.Exists(runtime.EnginePath),"engine missing");foreach(var lang in new[]{"jpn","jpn_vert","eng"})Check(File.Exists(Path.Combine(runtime.DataDirectory,lang+".traineddata")),"model missing");return Task.CompletedTask;});
await Test("OCR extraction cleans its private directory",()=>{var runtime=BundledOcrRuntime.ExtractEmbedded();var path=runtime.RootDirectory;runtime.Dispose();Check(!Directory.Exists(path),"extraction survived dispose");return Task.CompletedTask;});
await Test("corrupted OCR bundle is rejected",()=>{using var stream=new MemoryStream(new byte[]{1,2,3});Expect<InvalidDataException>(()=>BundledOcrRuntime.Extract(stream,new string('0',64)));return Task.CompletedTask;});
await Test("OCR extraction respects cancellation",()=>{using var cts=new CancellationTokenSource();cts.Cancel();Expect<OperationCanceledException>(()=>BundledOcrRuntime.ExtractEmbedded(cts.Token));return Task.CompletedTask;});
await Test("application version is 3.2.0.0",()=>{Check(typeof(ConversionResult).Assembly.GetName().Version?.ToString()=="3.2.0.0","wrong version");return Task.CompletedTask;});
await Test("atomic write cleans incomplete output", () => { var dir=Folder("failure"); var path=Path.Combine(dir,"out.txt"); Expect<IOException>(()=>AtomicFile.Write(path,temp=>{File.WriteAllText(temp,"partial");throw new IOException("disk full");})); Check(Directory.GetFiles(dir).Length==0,"partial survived"); return Task.CompletedTask; });
await Test("atomic write preserves an existing file", () => { var dir=Folder("collision");var path=Path.Combine(dir,"out.txt");File.WriteAllText(path,"original");var actual=AtomicFile.Write(path,temp=>File.WriteAllText(temp,"complete"));Check(File.ReadAllText(path)=="original"&&File.ReadAllText(actual)=="complete"&&actual!=path,"overwritten");return Task.CompletedTask; });
await Test("atomic cancellation before publication", () => { var dir=Folder("cancel-save");using var cts=new CancellationTokenSource();Expect<OperationCanceledException>(()=>AtomicFile.Write(Path.Combine(dir,"out.txt"),temp=>{File.WriteAllText(temp,"complete");cts.Cancel();},cts.Token));Check(Directory.GetFiles(dir).Length==0,"cancelled output survived");return Task.CompletedTask; });
await Test("concurrent writers preserve every output", async () => {var dir=Folder("concurrent");var results=await Task.WhenAll(Enumerable.Range(0,12).Select(i=>Task.Run(()=>AtomicFile.Write(Path.Combine(dir,"out.txt"),temp=>File.WriteAllText(temp,i.ToString())))));Check(results.Distinct().Count()==12&&Directory.GetFiles(dir).Length==12,"lost concurrent output");});
await Test("page ranges normalize and deduplicate",()=>{Check(PageRangeParser.Parse("5-3、1,3",5).SequenceEqual(new[]{1,3,4,5}),"wrong pages");Expect<ArgumentException>(()=>PageRangeParser.Parse("0",5));Expect<ArgumentException>(()=>PageRangeParser.Parse("1-9",5));return Task.CompletedTask;});
await Test("batch continues after failure and selects retry inputs",async()=>{var calls=new List<string>();var result=await BatchRunner.RunAsync(new[]{"a","b","c"},(f,p,t)=>{calls.Add(f);if(f=="b")throw new IOException("broken");return Task.FromResult(new ConversionResult(1,Array.Empty<string>()));},progress,default);Check(calls.Count==3&&result.CreatedFiles==2&&result.Items[1].Status==FileConversionStatus.Failed,"batch status");Check(result.Items.Where(x=>x.Status==FileConversionStatus.Failed).Select(x=>x.SourcePath).SequenceEqual(new[]{"b"}),"retry selection");});
await Test("batch cancellation separates current and unstarted",async()=>{using var cts=new CancellationTokenSource();var result=await BatchRunner.RunAsync(new[]{"a","b","c"},(f,p,t)=>{if(f=="b"){cts.Cancel();t.ThrowIfCancellationRequested();}return Task.FromResult(new ConversionResult(1,Array.Empty<string>()));},progress,cts.Token);Check(result.Items.Select(x=>x.Status).SequenceEqual(new[]{FileConversionStatus.Succeeded,FileConversionStatus.Cancelled,FileConversionStatus.NotStarted}),"cancel states");});
var source=Pdf("source",4);var other=Pdf("other",2);var service=new PdfDocumentService();
await Test("merge preserves page count and source",async()=>{var dir=Folder("merge");var before=File.ReadAllBytes(source);await service.MergeAsync(new(new[]{source,other},Path.Combine(dir,"merged.pdf"),passwords),progress,default);using var doc=PdfReader.Open(Directory.GetFiles(dir).Single(),PdfDocumentOpenMode.Import);Check(doc.PageCount==6&&File.ReadAllBytes(source).SequenceEqual(before),"merge changed source or count");});
await Test("delete removes only requested pages",async()=>{var dir=Folder("delete");await service.DeletePagesAsync(new(new[]{source},"2,4",Path.Combine(dir,"deleted.pdf"),passwords),progress,default);using var doc=PdfReader.Open(Directory.GetFiles(dir).Single(),PdfDocumentOpenMode.Import);Check(doc.PageCount==2&&doc.Pages[0].Width.Point==401&&doc.Pages[1].Width.Point==403,"wrong remaining pages");});
await Test("extract orders noncontiguous pages",async()=>{var dir=Folder("extract");await service.ExtractPagesAsync(new(new[]{source},"4,1",Path.Combine(dir,"extracted.pdf"),passwords),progress,default);using var doc=PdfReader.Open(Directory.GetFiles(dir).Single(),PdfDocumentOpenMode.Import);Check(doc.PageCount==2&&doc.Pages[0].Width.Point==401&&doc.Pages[1].Width.Point==404,"wrong selected order");});
await Test("password protected split",async()=>{var input=Pdf("protected",2,"secret");var dir=Folder("split");await service.SplitAsync(new(new[]{input},dir,"split",new Dictionary<string,string>{{input,"secret"}}),progress,default);Check(Directory.GetFiles(dir).Length==2,"split count");foreach(var f in Directory.GetFiles(dir)){using var doc=PdfReader.Open(f,PdfDocumentOpenMode.Import);Check(doc.PageCount==1,"split pages");}});
await Test("Japanese text extraction round trip",async()=>{var input=Path.Combine(AppContext.BaseDirectory,"Fixtures","Japanese.pdf");var dir=Folder("text");await new PdfTextExtractionService().ExtractAsync(new(new[]{input},Path.Combine(dir,"text.txt"),passwords,new Dictionary<string,IReadOnlyList<int>>(),Array.Empty<PdfTextSelectionItem>()),progress,default);var text=File.ReadAllText(Directory.GetFiles(dir).Single());Check(text.Contains("日本語")&&text.Contains("12345"),"Japanese text lost");});
await Test("selected page rendering yields readable image",async()=>{var dir=Folder("images");var result=await new PdfToImageService().ConvertAsync(new(new[]{source},dir,"test",PdfImageFormat.Jpeg,200,"2",passwords,80),progress,default);Check(!result.HasErrors&&result.CreatedFiles==1,"render error: "+string.Join(",",result.Errors));using var bitmap=SkiaSharp.SKBitmap.Decode(Directory.GetFiles(dir).Single());Check(bitmap!=null&&bitmap.Width>100,"invalid image");});
await Test("OCR rejects missing local engine",()=>{Expect<ArgumentException>(()=>OfflineOcrService.Validate(new(source,Path.Combine(root,"ocr.txt"),Path.Combine(root,"missing-engine"),root,"jpn+eng","")));return Task.CompletedTask;});
await Test("wrong password creates no split outputs",async()=>{var input=Pdf("wrong-password",2,"secret");var dir=Folder("wrong-password-out");var result=await BatchRunner.RunAsync(new[]{input},(file,p,t)=>service.SplitAsync(new(new[]{file},dir,"split",passwords),p,t),progress,default);Check(result.HasErrors&&Directory.GetFiles(dir).Length==0,"invalid password produced output");});
await Test("broken PDF creates no image outputs",async()=>{var input=Path.Combine(root,"broken.pdf");File.WriteAllText(input,"broken");var dir=Folder("broken-out");var result=await new PdfToImageService().ConvertAsync(new(new[]{input},dir,"test",PdfImageFormat.Png,200,"",passwords),progress,default);Check(result.HasErrors&&Directory.GetFiles(dir).Length==0,"broken PDF produced output");});
await Test("image conversion creates a readable PDF",async()=>{var dir=Folder("image-pdf");var image=Directory.GetFiles(Path.Combine(root,"images")).Single();await new ImageToPdfService().ConvertAsync(new(new[]{image},Path.Combine(dir,"image.pdf"),ImagePageMode.A4Portrait,true),progress,default);using var doc=PdfReader.Open(Directory.GetFiles(dir).Single(),PdfDocumentOpenMode.Import);Check(doc.PageCount==1,"image PDF page count");});
await Test("failed image does not leave a blank page",async()=>{var dir=Folder("image-partial");var bad=Path.Combine(root,"bad.png");File.WriteAllText(bad,"broken image");var good=Directory.GetFiles(Path.Combine(root,"images")).Single();var result=await new ImageToPdfService().ConvertAsync(new(new[]{good,bad},Path.Combine(dir,"image.pdf"),ImagePageMode.A4Portrait,true),progress,default);using var doc=PdfReader.Open(Directory.GetFiles(dir).Single(),PdfDocumentOpenMode.Import);Check(result.HasErrors&&doc.PageCount==1,"blank page from failed input");});
if(!OperatingSystem.IsWindows()) {
 await Test("OCR child process stops on cancellation",async()=>{var script=Path.Combine(root,"slow.sh");File.WriteAllText(script,"sleep 30\n");using var cts=new CancellationTokenSource(200);var watch=System.Diagnostics.Stopwatch.StartNew();try{await OfflineOcrService.RecognizeImageAsync("/bin/sh",root,"eng",script,cts.Token);throw new Exception("expected cancellation");}catch(OperationCanceledException){Check(watch.Elapsed<TimeSpan.FromSeconds(5),"process did not stop promptly");}});
 await Test("OCR nonzero process exit is an error",async()=>{var script=Path.Combine(root,"error.sh");File.WriteAllText(script,"echo 'invalid data' >&2\nexit 7\n");try{await OfflineOcrService.RecognizeImageAsync("/bin/sh",root,"eng",script,default);throw new Exception("expected error");}catch(IOException ex){Check(ex.Message.Contains("7"),"exit code lost");}});
}
if(OperatingSystem.IsMacOS() && File.Exists("/opt/homebrew/bin/tesseract")) {
 await Test("Mac OCR button path recognizes Japanese and English",async()=>{
  var input=Path.Combine(AppContext.BaseDirectory,"Fixtures","Japanese.pdf");
  var dir=Folder("mac-ocr");var output=Path.Combine(dir,"mac-ocr.txt");
  await new OfflineOcrService().ExtractBundledAsync(input,output,"jpn+eng","1","",progress,default);
  var text=File.ReadAllText(output);
  Check(text.Contains("日本語")&&text.Contains("ABC 12345"),"Mac OCR unexpected: "+text);
  var evidence=Environment.GetEnvironmentVariable("OCR_EVIDENCE_DIR");
  if(!string.IsNullOrWhiteSpace(evidence)){Directory.CreateDirectory(evidence);File.Copy(output,Path.Combine(evidence,"ocr-mac-app-path.txt"),true);}
 });
}
var engine=Environment.GetEnvironmentVariable("OCR_TEST_ENGINE");var data=Environment.GetEnvironmentVariable("OCR_TEST_DATA");
if(!string.IsNullOrEmpty(engine)&&!string.IsNullOrEmpty(data)) {
 await Test("real offline OCR from PDF to TXT",async()=>{
  var input=Path.Combine(AppContext.BaseDirectory,"Fixtures","Japanese.pdf");var dir=Folder("ocr");
  var language=File.Exists(Path.Combine(data,"jpn.traineddata"))?"jpn+eng":"eng";
  var output=Path.Combine(dir,"ocr.txt");
  await new OfflineOcrService().ExtractAsync(new(input,output,engine,data,language,"1"),progress,default);
  var text=File.ReadAllText(output);
  Check(text.Contains("12345")&&(language=="eng"||text.Contains("日本語")),"unexpected OCR: "+text);
  var evidence=Environment.GetEnvironmentVariable("OCR_EVIDENCE_DIR");
  if(!string.IsNullOrWhiteSpace(evidence)){
   Directory.CreateDirectory(evidence);
   File.Copy(output,Path.Combine(evidence,language=="eng"?"ocr-english.txt":"ocr-japanese-english.txt"),true);
  }
  Console.WriteLine("OCR language="+language+"; text="+text.Trim().Replace('\n',' '));
 });
} else Console.WriteLine("SKIP real OCR: set OCR_TEST_ENGINE and OCR_TEST_DATA");
} finally { Directory.Delete(root,true); }
Console.WriteLine($"RESULT {passed} passed, {failed} failed");return failed==0?0:1;
sealed class ImmediateProgress(Action<ConversionProgress> action):IProgress<ConversionProgress>{public void Report(ConversionProgress value)=>action(value);}
