using System.Runtime.InteropServices;

namespace Nacho.Core.Pdf;

/// <summary>The few PDFium functions NACHO uses (PDFium: BSD-3-Clause; prebuilt by bblanchon, Apache-2.0).</summary>
internal static unsafe class Pdfium
{
    private const string Lib = "pdfium";

    public const int PAGEOBJ_IMAGE = 3;
    public const int PAGEOBJ_FORM = 5;
    public const int ERR_PASSWORD = 4;
    public const int ERR_SECURITY = 5;
    public const int NO_INCREMENTAL = 2;
    public const int FPDF_ANNOT = 0x01;
    public const int FPDF_PRINTING = 0x800;
    public const int BitmapGray = 1, BitmapBGR = 2, BitmapBGRx = 3, BitmapBGRA = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct ImageMetadata
    {
        public uint Width;
        public uint Height;
        public float HorizontalDpi;
        public float VerticalDpi;
        public uint BitsPerPixel;
        public int ColorSpace;
        public int MarkedContentId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileWrite
    {
        public int Version;
        public delegate* unmanaged[Cdecl]<FileWrite*, void*, CULong, int> WriteBlock;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileRead
    {
        public CULong FileLen;
        public delegate* unmanaged[Cdecl]<void*, CULong, byte*, CULong, int> GetBlock;
        public void* Param;
    }

    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadMemDocument64(byte* data, nuint size, string? password);
    [DllImport(Lib)] public static extern uint FPDF_GetLastError();
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Lib)] public static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Lib)] public static extern int FPDF_GetSignatureCount(IntPtr doc);
    [DllImport(Lib)] public static extern int FPDF_GetSecurityHandlerRevision(IntPtr doc);
    [DllImport(Lib)] public static extern int FPDF_SaveAsCopy(IntPtr doc, FileWrite* writer, uint flags);

    [DllImport(Lib)] public static extern int FPDFPage_GenerateContent(IntPtr page);
    [DllImport(Lib)] public static extern int FPDFPage_CountObjects(IntPtr page);
    [DllImport(Lib)] public static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetType(IntPtr obj);
    [DllImport(Lib)] public static extern int FPDFFormObj_CountObjects(IntPtr form);
    [DllImport(Lib)] public static extern IntPtr FPDFFormObj_GetObject(IntPtr form, uint index);

    [DllImport(Lib)] public static extern int FPDFImageObj_GetImageMetadata(IntPtr obj, IntPtr page, out ImageMetadata meta);
    [DllImport(Lib)] public static extern int FPDFImageObj_GetImageFilterCount(IntPtr obj);
    [DllImport(Lib)] public static extern nuint FPDFImageObj_GetImageFilter(IntPtr obj, int index, byte* buffer, nuint length);
    [DllImport(Lib)] public static extern nuint FPDFImageObj_GetImageDataRaw(IntPtr obj, byte* buffer, nuint length);
    [DllImport(Lib)] public static extern IntPtr FPDFImageObj_GetBitmap(IntPtr obj);
    [DllImport(Lib)] public static extern IntPtr FPDFImageObj_GetRenderedBitmap(IntPtr doc, IntPtr page, IntPtr obj);
    [DllImport(Lib)] public static extern int FPDFImageObj_SetBitmap(IntPtr* pages, int count, IntPtr obj, IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFImageObj_LoadJpegFileInline(IntPtr* pages, int count, IntPtr obj, FileRead* file);

    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Lib)] public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, void* firstScan, int stride);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_GetFormat(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_GetWidth(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_GetHeight(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Lib)] public static extern byte* FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Lib)] public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int w, int h, int rotate, int flags);

    [DllImport(Lib)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Lib)] public static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Lib)] public static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Lib)] public static extern int FPDFText_GetText(IntPtr textPage, int start, int count, ushort* result);
}
