using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace OfflinePDFConverter.Services;

internal sealed class OfficeKitRuntime : IDisposable
{
    private readonly IntPtr _office;
    private readonly DirectoryInfo _work;
    private readonly SetKitActive? _resetMacKitMode;
    private string? _windowsFont;

    public OfficeKitRuntime()
    {
        _work = Directory.CreateTempSubdirectory("opc-office-");
        try
        {
            Program.TraceGuiVerification("Preparing bundled Office engine");
            var (library, bootstrap) = GetRuntime();
            var profile = Directory.CreateDirectory(Path.Combine(_work.FullName, "profile", "user"));
            File.WriteAllText(Path.Combine(profile.FullName, "registrymodifications.xcu"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <oor:items xmlns:oor="http://openoffice.org/2001/registry">
                  <item oor:path="/org.openoffice.Office.Common/Security/Scripting">
                    <prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop>
                    <prop oor:name="DisableMacrosExecution" oor:op="fuse"><value>true</value></prop>
                  </item>
                </oor:items>
                """);

            ConfigureFonts(bootstrap);
            var module = LoadRuntimeLibrary(library);
            if (OperatingSystem.IsMacOS())
            {
                // The native Writer shutdown needs Kit mode cleared after the public destroy hook.
                var helperPath = Path.Combine(Path.GetDirectoryName(library)!, "libcomphelper.dylib");
                var helper = File.Exists(helperPath) ? NativeLibrary.Load(helperPath) : module;
                _resetMacKitMode = Marshal.GetDelegateForFunctionPointer<SetKitActive>(
                    NativeLibrary.GetExport(helper, "_ZN10comphelper14LibreOfficeKit9setActiveEb"));
            }

            var initialize = Marshal.GetDelegateForFunctionPointer<Initialize>(
                NativeLibrary.GetExport(module, "libreofficekit_hook_2"));
            Program.TraceGuiVerification("Calling the native Office initialization hook");
            _office = initialize(bootstrap, FileUri(profile.Parent!.FullName));
            Program.TraceGuiVerification("Native Office initialization returned");
            if (_office == IntPtr.Zero)
                throw new InvalidOperationException("Office変換エンジンを初期化できませんでした。");
        }
        catch
        {
            UnregisterWindowsFont();
            DeleteWorkDirectory();
            throw;
        }
    }

    public void SavePdf(string source, string destination, CancellationToken token)
    {
        if (!source.Contains('%') && !destination.Contains('%'))
        {
            SaveNativePdf(source, destination, token);
            return;
        }

        // Some LibreOffice URL loaders interpret a literal %xx in a filesystem path as an escape.
        var staging = Directory.CreateTempSubdirectory("opc-office-conversion-");
        try
        {
            var input = Path.Combine(staging.FullName, "source" + Path.GetExtension(source).ToLowerInvariant());
            var output = Path.Combine(staging.FullName, "output.pdf");
            File.Copy(source, input);
            token.ThrowIfCancellationRequested();
            SaveNativePdf(input, output, token);
            token.ThrowIfCancellationRequested();
            File.Copy(output, destination, overwrite: true);
        }
        finally { staging.Delete(recursive: true); }
    }

    private void SaveNativePdf(string source, string destination, CancellationToken token)
    {
        IntPtr document;
        var extension = Path.GetExtension(source);
        Program.TraceGuiVerification($"Loading an Office document ({extension})");
        using (OperatingSystem.IsWindows() && extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? WakeWindowsSpreadsheetImport() : null)
        {
            document = GetMethod<LoadDocument>(_office, 4)(_office, FileUri(source),
                "Batch=true,MacroSecurityLevel=3,EnableMacrosExecution=false");
        }
        Program.TraceGuiVerification("Native document load returned");
        if (document == IntPtr.Zero)
            throw new InvalidDataException($"Officeファイルを開けませんでした。{LastError()}");

        try
        {
            token.ThrowIfCancellationRequested();
            if (GetMethod<SaveDocument>(document, 2)(document, FileUri(destination), "pdf", IntPtr.Zero) == 0)
                throw new IOException($"PDFに変換できませんでした。{LastError()}");
            Program.TraceGuiVerification("Native PDF export returned");
        }
        finally { GetMethod<Destroy>(document, 1)(document); }
    }

    public void Dispose()
    {
        GetMethod<Destroy>(_office, 1)(_office);
        _resetMacKitMode?.Invoke(false);
        UnregisterWindowsFont();
        DeleteWorkDirectory();
    }

    private (string Library, string Bootstrap) GetRuntime()
    {
        if (OperatingSystem.IsMacOS())
        {
            var contents = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources",
                "OfficeEngine", "LibreOffice.app", "Contents"));
            return (FindLibrary(Path.Combine(contents, "Frameworks"), "libsofficeapp.dylib", "libmergedlo.dylib"),
                Path.Combine(contents, "Resources"));
        }

        if (OperatingSystem.IsWindows())
        {
            using var archive = typeof(OfficeKitRuntime).Assembly.GetManifestResourceStream("OfflinePDFConverter.OfficeEngine.zip")
                ?? throw new FileNotFoundException("Windows用のOffice変換エンジンがアプリに含まれていません。");
            var directory = Path.Combine(_work.FullName, "engine");
            using (var zip = new ZipArchive(archive, ZipArchiveMode.Read))
                zip.ExtractToDirectory(directory);
            var program = Path.Combine(directory, "program");
            return (FindLibrary(program, "sofficeapp.dll", "mergedlo.dll"), program);
        }

        throw new PlatformNotSupportedException("Office変換はMac版とWindows版で利用できます。");
    }

    private void ConfigureFonts(string bootstrap)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_work.FullName, "fonts"));
        var fallback = Path.Combine(directory.FullName, "NotoSansJP-Regular.ttf");
        using (var font = typeof(OfficeKitRuntime).Assembly.GetManifestResourceStream("OfflinePDFConverter.Fonts.NotoSansJP-Regular.ttf")
            ?? throw new InvalidDataException("Office変換用の日本語フォントが含まれていません。"))
        using (var file = File.Create(fallback))
            font.CopyTo(file);

        if (OperatingSystem.IsWindows())
        {
            if (AddPrivateFont(fallback, PrivateFont, IntPtr.Zero) == 0)
                throw new InvalidOperationException("Office変換用の日本語フォントを読み込めませんでした。");
            _windowsFont = fallback;
            return;
        }

        var config = Path.Combine(_work.FullName, "fonts.conf");
        var cache = Directory.CreateDirectory(Path.Combine(_work.FullName, "font-cache"));
        new XDocument(new XElement("fontconfig",
            new XElement("dir", directory.FullName),
            new XElement("dir", Path.Combine(bootstrap, "fonts", "truetype")),
            new XElement("dir", "/System/Library/Fonts"),
            new XElement("dir", "/Library/Fonts"),
            new XElement("dir", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts")),
            new XElement("cachedir", cache.FullName))).Save(config);
        if (SetNativeEnvironment("FONTCONFIG_FILE", config, 1) != 0
            || SetNativeEnvironment("SAL_USE_VCLPLUGIN", "svp", 1) != 0)
            throw new InvalidOperationException("Office変換のフォント設定を初期化できませんでした。");
    }

    private const uint PrivateFont = 0x10;

    private static Timer? WakeWindowsSpreadsheetImport()
    {
        // Calc's parallel worksheet import waits for the WinSal event loop while called
        // from a worker. Wake that loop if its last notification arrived before the wait.
        var window = IntPtr.Zero;
        while ((window = FindWindowEx(IntPtr.Zero, window, "SALCOMWND", null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(window, out var processId);
            if (processId != (uint)Environment.ProcessId)
                continue;

            Program.TraceGuiVerification("Keeping the Windows spreadsheet import event loop awake");
            var officeWindow = window;
            return new Timer(_ => PostMessage(officeWindow, 0, IntPtr.Zero, IntPtr.Zero),
                null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
        }
        return null;
    }

    private void UnregisterWindowsFont()
    {
        if (_windowsFont is not null)
            RemovePrivateFont(_windowsFont, PrivateFont, IntPtr.Zero);
    }

    private static string FindLibrary(string directory, params string[] candidates) =>
        candidates.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists)
        ?? throw new FileNotFoundException("Office変換エンジンがアプリに含まれていません。");

    private static IntPtr LoadRuntimeLibrary(string path)
    {
        if (!OperatingSystem.IsWindows())
            return NativeLibrary.Load(path);

        const uint searchDllDirectoryAndDefaultPaths = 0x00001100;
        var module = LoadLibraryEx(path, IntPtr.Zero, searchDllDirectoryAndDefaultPaths);
        return module != IntPtr.Zero
            ? module
            : throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Office変換エンジンを読み込めませんでした。");
    }

    private string LastError()
    {
        var message = GetMethod<GetError>(_office, 3)(_office);
        if (message == IntPtr.Zero)
            return "";
        try { return Marshal.PtrToStringUTF8(message) ?? ""; }
        finally { GetMethod<Destroy>(_office, 5)(message); }
    }

    private void DeleteWorkDirectory()
    {
        try { _work.Delete(recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string FileUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static T GetMethod<T>(IntPtr owner, int index) where T : Delegate
    {
        var table = Marshal.ReadIntPtr(owner);
        var offset = index * IntPtr.Size;
        if (Marshal.ReadIntPtr(table).ToInt64() < offset + IntPtr.Size)
            throw new InvalidOperationException("Office変換エンジンのバージョンが適合しません。");
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, offset));
    }

    [DllImport("libc", EntryPoint = "setenv", SetLastError = true)]
    private static extern int SetNativeEnvironment(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int overwrite);

    [DllImport("gdi32", EntryPoint = "AddFontResourceExW", CharSet = CharSet.Unicode)]
    private static extern int AddPrivateFont(string fileName, uint flags, IntPtr reserved);
    [DllImport("gdi32", EntryPoint = "RemoveFontResourceExW", CharSet = CharSet.Unicode)]
    private static extern bool RemovePrivateFont(string fileName, uint flags, IntPtr reserved);

    [DllImport("kernel32", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("user32", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr previous, string className, string? windowName);
    [DllImport("user32", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32", EntryPoint = "PostMessageW", ExactSpelling = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Initialize(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string installPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string profileUri);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LoadDocument(IntPtr office, [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string options);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SaveDocument(IntPtr document, [MarshalAs(UnmanagedType.LPUTF8Str)] string destination,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string format, IntPtr filterOptions);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Destroy(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetError(IntPtr office);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetKitActive([MarshalAs(UnmanagedType.I1)] bool active);
}
