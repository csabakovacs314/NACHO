namespace Nacho.Core;

/// <summary>
/// Linux wording for the texts that name Windows things (Explorer, Win+Shift+S, Send to, the File name box).
/// Each Linux list replaces some keys of the same language and has exactly the same keys as the others.
/// </summary>
public static partial class Strings
{
    public static readonly IReadOnlyDictionary<string, string> LinuxEn = new Dictionary<string, string>
    {
        ["Drop.Sub"] = "Or right-click a file in your file manager and choose Open With > NACHO.",
        ["Status.Copied"] = "Copied {0} as a file. Paste it in your file manager, or drag the thumbnail into your Doc.",
        ["Status.CopiedPath"] = "Copied the path of {0}. In Google Docs: Replace image > Upload from computer, then press Ctrl+L, Ctrl+V and Enter.",
        ["Status.Opened"] = "Opened the folder in your file manager.",
        ["Status.ShellFailed"] = "Couldn't update the file manager menus or the app menu: {0}",
        ["Opt.Shell"] = "Desktop integration",
        ["Opt.ContextMenu"] = "Show Make Docs-ready in the file manager's right-click menu",
        ["Opt.ContextMenuHint"] = "For Files, Nemo, Caja and Dolphin. In Files it sits under Scripts. It points at this copy of NACHO.",
        ["Opt.SendTo"] = "Add NACHO to the app menu and to Open With",
        ["About.Way2"] = "Copy an image or a screenshot, then press Ctrl+V in NACHO.",
        ["About.Way3"] = "Right-click an image in your file manager and choose Open With > NACHO, or Make Docs-ready (turn it on in More options).",
        ["About.Way4"] = "Then drag the thumbnail into your Google Doc.",
        ["About.ReplaceBody"] = "Click the image in the Doc, choose Replace image > Upload from computer, and pick the copy in Docs-Ready. Quicker: right-click its tile in NACHO, choose Copy path, then press Ctrl+L, Ctrl+V and Enter in the file dialog. The copy has the same shape as the original, so it takes the old image's place without stretching.",
        ["Wd.PcTitle"] = "On this computer",
        ["Wd.WherePc"] = "this computer",
        ["Wd.FileCon1"] = "Close it in Word or LibreOffice first",
        ["Wd.ErrInUse"] = "\"{0}\" is open in Word or LibreOffice. Close it there and try again, or choose Into a new copy.",
        ["Wd.OpenWord"] = "Open the file",
        ["Home.Step1"] = "Choose a Google Doc, or a Word file on this computer or in Google Drive",
    };

    public static readonly IReadOnlyDictionary<string, string> LinuxEs = new Dictionary<string, string>
    {
        ["Drop.Sub"] = "O haz clic derecho en un archivo de tu gestor de archivos y elige Abrir con > NACHO.",
        ["Status.Copied"] = "Se copió {0} como archivo. Pégalo en tu gestor de archivos o arrastra la miniatura a tu Doc.",
        ["Status.CopiedPath"] = "Se copió la ruta de {0}. En Google Docs: Reemplazar imagen > Subir desde el ordenador y luego pulsa Ctrl+L, Ctrl+V e Intro.",
        ["Status.Opened"] = "Se abrió la carpeta en tu gestor de archivos.",
        ["Status.ShellFailed"] = "No se pudieron actualizar los menús del gestor de archivos ni el menú de aplicaciones: {0}",
        ["Opt.Shell"] = "Integración con el escritorio",
        ["Opt.ContextMenu"] = "Mostrar Preparar para Docs en el menú del clic derecho del gestor de archivos",
        ["Opt.ContextMenuHint"] = "Para Archivos, Nemo, Caja y Dolphin. En Archivos aparece en Scripts. Apunta a esta copia de NACHO.",
        ["Opt.SendTo"] = "Añadir NACHO al menú de aplicaciones y a Abrir con",
        ["About.Way2"] = "Copia una imagen o una captura de pantalla y pulsa Ctrl+V en NACHO.",
        ["About.Way3"] = "Haz clic derecho en una imagen de tu gestor de archivos y elige Abrir con > NACHO, o Preparar para Docs (actívalo en Más opciones).",
        ["About.Way4"] = "Después arrastra la miniatura a tu Google Doc.",
        ["About.ReplaceBody"] = "Haz clic en la imagen del Doc, elige Reemplazar imagen > Subir desde el ordenador y selecciona la copia en Docs-Ready. Más rápido: haz clic derecho en su miniatura en NACHO, elige Copiar ruta y, en el diálogo de archivos, pulsa Ctrl+L, Ctrl+V e Intro. La copia tiene la misma proporción que el original, así que ocupa el lugar de la imagen anterior sin deformarse.",
        ["Wd.PcTitle"] = "En este ordenador",
        ["Wd.WherePc"] = "este ordenador",
        ["Wd.FileCon1"] = "Ciérralo antes en Word o LibreOffice",
        ["Wd.ErrInUse"] = "\"{0}\" está abierto en Word o LibreOffice. Ciérralo allí y vuelve a intentarlo, o elige En una copia nueva.",
        ["Wd.OpenWord"] = "Abrir el archivo",
        ["Home.Step1"] = "Elige un Google Doc, o un archivo de Word de este ordenador o de Google Drive",
    };

    public static readonly IReadOnlyDictionary<string, string> LinuxHu = new Dictionary<string, string>
    {
        ["Drop.Sub"] = "Vagy kattints jobb gombbal egy fájlra a fájlkezelőben, és válaszd a Megnyitás ezzel > NACHO lehetőséget.",
        ["Status.Copied"] = "{0} fájlként a vágólapra került. Illeszd be a fájlkezelőbe, vagy húzd a bélyegképet a dokumentumba.",
        ["Status.CopiedPath"] = "{0} elérési útja a vágólapon. A Google Docsban: Kép cseréje > Feltöltés a számítógépről, majd Ctrl+L, Ctrl+V és Enter.",
        ["Status.Opened"] = "A mappa megnyílt a fájlkezelőben.",
        ["Status.ShellFailed"] = "Nem sikerült frissíteni a fájlkezelő menüit vagy az alkalmazásmenüt: {0}",
        ["Opt.Shell"] = "Asztali integráció",
        ["Opt.ContextMenu"] = "Docs-kész másolat készítése a fájlkezelő jobb gombos menüjében",
        ["Opt.ContextMenuHint"] = "A Fájlok, a Nemo, a Caja és a Dolphin számára. A Fájlokban a Parancsfájlok alatt jelenik meg. Ennek a NACHO-nak a helyére mutat.",
        ["Opt.SendTo"] = "A NACHO hozzáadása az alkalmazásmenühöz és a Megnyitás ezzel listához",
        ["About.Way2"] = "Másolj egy képet vagy képernyőképet, majd nyomj Ctrl+V-t a NACHO-ban.",
        ["About.Way3"] = "Kattints jobb gombbal egy képre a fájlkezelőben, és válaszd a Megnyitás ezzel > NACHO vagy a Docs-kész másolat készítése parancsot (a Beállításokban kapcsolhatod be).",
        ["About.Way4"] = "Ezután húzd a bélyegképet a Google Docs-dokumentumodba.",
        ["About.ReplaceBody"] = "Kattints a képre a dokumentumban, válaszd a Kép cseréje > Feltöltés a számítógépről lehetőséget, és válaszd ki a másolatot a Docs-Ready mappából. Gyorsabban: kattints jobb gombbal a csempéjére a NACHO-ban, válaszd az Elérési út másolása parancsot, majd a fájlválasztóban nyomj Ctrl+L-t, Ctrl+V-t és Entert. A másolat aránya megegyezik az eredetiével, ezért torzítás nélkül kerül a régi kép helyére.",
        ["Wd.PcTitle"] = "Ezen a számítógépen",
        ["Wd.WherePc"] = "ez a számítógép",
        ["Wd.FileCon1"] = "Előbb zárd be a Wordben vagy a LibreOffice-ban",
        ["Wd.ErrInUse"] = "A(z) \"{0}\" meg van nyitva a Wordben vagy a LibreOffice-ban. Zárd be ott, és próbáld újra, vagy válaszd az Egy új másolatba lehetőséget.",
        ["Wd.OpenWord"] = "A fájl megnyitása",
        ["Home.Step1"] = "Válassz egy Google-dokumentumot, vagy egy Word-fájlt erről a számítógépről vagy a Google Drive-ból",
    };

    public static IReadOnlyDictionary<string, string> LinuxFor(string language) => Normalise(language) switch
    {
        "es" => LinuxEs,
        "hu" => LinuxHu,
        _ => LinuxEn,
    };

    /// <summary>The texts for one language on one platform: the Linux list wins over the shared one.</summary>
    public static IReadOnlyDictionary<string, string> ForPlatform(string language, bool linux)
    {
        var shared = For(language);
        if (!linux) return shared;
        var merged = new Dictionary<string, string>(shared);
        foreach (var (key, text) in LinuxFor(language)) merged[key] = text;
        return merged;
    }
}
