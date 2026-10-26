using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Nacho.Core.Pdf.Pdfium;

namespace Nacho.Core.Pdf;

/// <summary>
/// One open PDF in PDFium. PDFium is not thread-safe, so every use goes through <see cref="Gate"/>.
/// The file's bytes stay pinned while the document is open, as PDFium reads from them on demand.
/// </summary>
internal sealed unsafe class PdfDoc : IDisposable
{
    public static readonly object Gate = new();
    private static bool _ready;

    private GCHandle _pin;
    public IntPtr Handle { get; private set; }
    public int PageCount { get; }

    private PdfDoc(GCHandle pin, IntPtr handle)
    {
        _pin = pin;
        Handle = handle;
        PageCount = FPDF_GetPageCount(handle);
    }

    /// <summary>Starts PDFium once. False when the PDFium library is missing (a broken install).</summary>
    public static bool EnsureLibrary()
    {
        lock (Gate)
        {
            if (_ready) return true;
            try
            {
                FPDF_InitLibrary();
                _ready = true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return false;
            }
            return true;
        }
    }

    /// <summary>Opens a PDF from memory; on failure returns null and PDFium's error code (4 = needs a password).</summary>
    public static PdfDoc? Open(byte[] data, out uint error)
    {
        // PDFium keeps reading from the buffer, so it stays pinned for the document's life.
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr h = FPDF_LoadMemDocument64((byte*)pin.AddrOfPinnedObject(), (nuint)data.Length, null);
        if (h == IntPtr.Zero)
        {
            error = FPDF_GetLastError();
            pin.Free();
            return null;
        }
        error = 0;
        return new PdfDoc(pin, h);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) FPDF_CloseDocument(Handle);
        Handle = IntPtr.Zero;
        if (_pin.IsAllocated) _pin.Free();
    }

    /// <summary>The whole document written out again (not an incremental update), or null if PDFium failed.</summary>
    public byte[]? Save()
    {
        using var ms = new MemoryStream();
        var state = new WriterState { Handle = GCHandle.Alloc(ms) };
        try
        {
            state.W.Version = 1;
            state.W.WriteBlock = &WriteBlock;
            int ok = FPDF_SaveAsCopy(Handle, (FileWrite*)&state, NO_INCREMENTAL);
            return ok != 0 ? ms.ToArray() : null;
        }
        finally
        {
            state.Handle.Free();
        }
    }

    /// <summary>Text of a page, for checking that nothing but pictures changed.</summary>
    public static string PageText(IntPtr page)
    {
        IntPtr tp = FPDFText_LoadPage(page);
        if (tp == IntPtr.Zero) return "";
        try
        {
            int n = FPDFText_CountChars(tp);
            if (n <= 0) return "";
            var buf = new ushort[n + 1];
            fixed (ushort* b = buf) FPDFText_GetText(tp, 0, n, b);
            fixed (ushort* b = buf) return new string((char*)b, 0, n);
        }
        finally
        {
            FPDFText_ClosePage(tp);
        }
    }

    /// <summary>A page drawn on white at the given size, as BGRA bytes (4 per pixel).</summary>
    public static byte[] RenderPage(IntPtr page, int width, int height)
    {
        IntPtr bmp = FPDFBitmap_Create(width, height, 1);
        try
        {
            FPDFBitmap_FillRect(bmp, 0, 0, width, height, 0xFFFFFFFF);
            FPDF_RenderPageBitmap(bmp, page, 0, 0, width, height, 0, FPDF_ANNOT);
            int stride = FPDFBitmap_GetStride(bmp);
            byte* src = FPDFBitmap_GetBuffer(bmp);
            var px = new byte[width * height * 4];
            for (int y = 0; y < height; y++) Marshal.Copy((IntPtr)(src + (long)y * stride), px, y * width * 4, width * 4);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            return px;
        }
        finally
        {
            FPDFBitmap_Destroy(bmp);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int WriteBlock(FileWrite* self, void* data, CULong size)
    {
        var st = (WriterState*)self;
        var s = (Stream)st->Handle.Target!;
        s.Write(new ReadOnlySpan<byte>(data, checked((int)size.Value)));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static int ReadBlock(void* param, CULong position, byte* buf, CULong size)
    {
        var bytes = (byte[])GCHandle.FromIntPtr((IntPtr)param).Target!;
        long pos = (long)position.Value, len = (long)size.Value;
        if (pos < 0 || pos + len > bytes.Length) return 0;
        new ReadOnlySpan<byte>(bytes, (int)pos, (int)len).CopyTo(new Span<byte>(buf, (int)len));
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WriterState
    {
        public FileWrite W;
        public GCHandle Handle;
    }
}
