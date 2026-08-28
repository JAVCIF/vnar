using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LocaleGameHub.Services;

public static class LocalizationService
{
    private static readonly Dictionary<string, string> EsToEn = new(StringComparer.Ordinal)
    {
        ["Visual Novels, un clic y listo."] = "Visual Novels, one click and you're ready.",
        ["Añadir EXE"] = "Add EXE",
        ["Escanear carpeta"] = "Scan folder",
        ["Ajustes"] = "Settings",
        ["Configurar"] = "Configure",
        ["Ejecutar como administrador"] = "Run as administrator",
        ["▶ Jugar"] = "▶ Play",
        ["Tu biblioteca está vacía"] = "Your library is empty",
        ["Arrastra aquí un .exe o una carpeta de juegos, añade un ejecutable o escanea una biblioteca completa."] = "Drop an .exe or a game folder here, add an executable, or scan a complete library.",
        ["Tip: también puedes arrastrar EXE o carpetas directamente sobre VNAR."] = "Tip: you can also drop EXEs or folders directly onto VNAR.",
        ["Buscar por nombre o ruta"] = "Search by name or path",
        ["Editar juego"] = "Edit game",
        ["Anterior"] = "Previous",
        ["Siguiente"] = "Next",
        ["Todos"] = "All games",
        ["Favoritos"] = "Favorites",
        ["No se encontraron juegos"] = "No games found",
        ["Prueba con otro nombre o ruta."] = "Try another name or path.",
        ["Aún no tienes favoritos"] = "No favorites yet",
        ["Marca ★ Favorito en la configuración de un juego y aparecerá aquí."] = "Mark ★ Favorite in a game's settings and it will appear here.",

        ["Locale Emulator"] = "Locale Emulator",
        ["El Hub usa los perfiles reales de LEConfig.xml para reproducir el mismo comportamiento de Run in Japanese."] = "VNAR uses the real LEConfig.xml profiles to reproduce the same behavior as Run in Japanese.",
        ["Primer arranque"] = "First launch",
        ["Antes de jugar, indica dónde tienes LEProc.exe o deja que VNAR descargue y prepare Locale Emulator por ti."] = "Before playing, select where LEProc.exe is located or let VNAR download and prepare Locale Emulator for you.",
        ["La descarga usa la última release publicada en github.com/xupefei/Locale-Emulator y la descomprime en la carpeta que elijas."] = "The download uses the latest release published at github.com/xupefei/Locale-Emulator and extracts it to the folder you choose.",
        ["Perfil japonés normal"] = "Normal Japanese profile",
        ["Perfil japonés administrador"] = "Administrator Japanese profile",
        ["Búsqueda de portadas"] = "Cover search",
        ["VNDB funciona sin configuración. Si quieres ver resultados de Google Images dentro del Hub, puedes añadir una API key opcional de SerpApi."] = "VNDB works without configuration. To see Google Images results inside VNAR, you can add an optional SerpApi API key.",
        ["Opcional. La clave se guarda localmente en settings.json. Si la dejas vacía, el buscador interno seguirá usando VNDB y el drag desde el navegador."] = "Optional. The key is stored locally in settings.json. If left blank, the built-in search will keep using VNDB and browser drag-and-drop.",
        ["Estado"] = "Status",
        ["Examinar…"] = "Browse…",
        ["↓ Descargar / configurar LE"] = "↓ Download / configure LE",
        ["↻ Leer perfiles de LEConfig.xml"] = "↻ Read LEConfig.xml profiles",
        ["GitHub de Locale Emulator ↗"] = "Locale Emulator GitHub ↗",
        ["Cancelar"] = "Cancel",
        ["Guardar"] = "Save",
        ["Ajustes · VNAR"] = "Settings · VNAR",
        ["Configurar Locale Emulator · VNAR"] = "Configure Locale Emulator · VNAR",
        ["Interfaz"] = "Interface",
        ["Idioma"] = "Language",
        ["Juegos por página"] = "Games per page",
        ["Puedes elegir entre 10 y 50 juegos por página."] = "You can choose between 10 and 50 games per page.",

        ["Portada"] = "Cover",
        ["Arrastra aquí una portada"] = "Drop a cover here",
        ["También puedes arrastrar una imagen desde una carpeta o directamente desde el navegador."] = "You can also drag an image from a folder or directly from your browser.",
        ["Juego"] = "Game",
        ["Nombre"] = "Name",
        ["Ejecutable"] = "Executable",
        ["Argumentos opcionales"] = "Optional arguments",
        ["Desmarcado usa Run in Japanese; marcado usa Run in Japanese (Admin)."] = "Unchecked uses Run in Japanese; checked uses Run in Japanese (Admin).",
        ["Pega el código de la novela (por ejemplo v17 o simplemente 17) para descargar su portada."] = "Paste the VN code (for example v17 or simply 17) to download its cover.",
        ["Elegir imagen"] = "Choose image",
        ["Editar portada…"] = "Edit cover…",
        ["Buscar portada…"] = "Search cover…",
        ["Cambiar…"] = "Change…",
        ["Ejecutar con el perfil japonés de administrador"] = "Run with the administrator Japanese profile",
        ["Traer de VNDB"] = "Fetch from VNDB",
        ["★ Favorito"] = "★ Favorite",
        ["Eliminar de la biblioteca"] = "Remove from library",
        ["Configurar juego · VNAR"] = "Configure game · VNAR",

        ["Vista previa"] = "Preview",
        ["La imagen inicia completa dentro del marco, sin recortes automáticos. Luego tú decides si acercarla, alejarla o moverla."] = "The image starts fully visible inside the frame with no automatic cropping. You then decide whether to zoom in, zoom out, or move it.",
        ["Ajustes de portada"] = "Cover adjustments",
        ["Zoom"] = "Zoom",
        ["1.00× muestra la imagen completa ajustada al marco. Usa la rueda del mouse para cambiar el zoom más rápido."] = "1.00× shows the full image fitted to the frame. Use the mouse wheel to change zoom faster.",
        ["Relleno del fondo"] = "Background fill",
        ["Útil para imágenes rectangulares o pequeñas cuando no quieres recortar agresivamente."] = "Useful for rectangular or small images when you do not want aggressive cropping.",
        ["Posición"] = "Position",
        ["Puedes arrastrar con el mouse o usar pequeños ajustes finos."] = "You can drag with the mouse or use small fine adjustments.",
        ["Resultado"] = "Output",
        ["Al aplicar, se generará una nueva imagen PNG desde la fuente original, conservando estos ajustes para futuras ediciones."] = "When applied, a new PNG will be generated from the original source while preserving these settings for future edits.",
        ["Negro"] = "Black",
        ["Blanco"] = "White",
        ["Transparente"] = "Transparent",
        ["Desenfoque con la misma imagen"] = "Blur using the same image",
        ["Centrar"] = "Center",
        ["Mejorar calidad antes de guardar (exportar a mayor resolución para reducir pixelado)"] = "Improve quality before saving (export at a higher resolution to reduce pixelation)",
        ["Restablecer"] = "Reset",
        ["Aplicar"] = "Apply",
        ["Editar portada · VNAR"] = "Edit cover · VNAR",

        ["Ejecutables encontrados"] = "Executables found",
        ["Marca solo los ejecutables principales. El escáner evita instaladores y utilidades comunes, pero tú tienes la última palabra."] = "Select only the main executables. The scanner avoids common installers and utilities, but you have the final say.",
        ["Seleccionar todos"] = "Select all",
        ["Ninguno"] = "None",
        ["Añadir seleccionados"] = "Add selected",
        ["Resultados del escaneo"] = "Scan results",

        ["Buscar portada"] = "Search cover",
        ["Busca por el nombre del juego y elige una de las primeras coincidencias sin salir del Hub."] = "Search by game name and choose one of the first matches without leaving VNAR.",
        ["VNDB no necesita API key y suele ser la mejor fuente para novelas visuales."] = "VNDB does not require an API key and is usually the best source for visual novels.",
        ["Tip: también puedes arrastrar una imagen directamente desde Chrome/Edge hasta la portada del editor."] = "Tip: you can also drag an image directly from Chrome/Edge onto the editor cover.",
        ["VNDB · automático"] = "VNDB · automatic",
        ["Google Images · SerpApi"] = "Google Images · SerpApi",
        ["Buscar"] = "Search",
        ["Abrir Google Images ↗"] = "Open Google Images ↗",
        ["Cerrar"] = "Close",
        ["Buscar portada · VNAR"] = "Search cover · VNAR",

        ["Jugar"] = "Play",
        ["Crear acceso directo…"] = "Create shortcut…",
        ["Crear acceso directo · VNAR"] = "Create shortcut · VNAR",
        ["Crear acceso directo"] = "Create shortcut",
        ["El acceso directo abre el juego mediante VNAR y conserva la configuración actual de Locale Emulator, administrador y argumentos."] = "The shortcut launches the game through VNAR and keeps the current Locale Emulator, administrator, and argument settings.",
        ["Nombre del acceso directo"] = "Shortcut name",
        ["Carpeta de destino"] = "Destination folder",
        ["Elegir…"] = "Choose…",
        ["Icono"] = "Icon",
        ["VNAR busca iconos en los ejecutables de la misma carpeta del juego. Si ninguno sirve, puedes usar el icono genérico de VNAR."] = "VNAR looks for icons in executables located in the same game folder. If none are suitable, you can use the generic VNAR icon.",
        ["Crear"] = "Create",

        ["Developers"] = "Developers",
        ["Selecciona un developer para ver sus juegos."] = "Select a developer to see its games.",
        ["Mostrando los juegos asociados a este developer en VNDB."] = "Showing games associated with this developer on VNDB.",
        ["↻ Actualizar desde VNDB"] = "↻ Refresh from VNDB",
        ["Ajustar portada…"] = "Adjust cover…",
        ["Aún no hay developers detectados"] = "No developers detected yet",
        ["Configura el ID de VNDB en tus juegos y VNAR agrupará automáticamente sus developers aquí."] = "Configure VNDB IDs for your games and VNAR will automatically group their developers here.",
        ["No se encontraron developers"] = "No developers found",
        ["Portada del developer"] = "Developer cover",
        ["Portada del developer · VNAR"] = "Developer cover · VNAR",
        ["Arrastra una imagen aquí"] = "Drop an image here",
        ["Imagen"] = "Image",
        ["La API actual de VNDB no expone una imagen para producers/developers. Puedes elegir una imagen propia y ajustarla con el mismo editor de portadas de los juegos."] = "The current VNDB API does not expose an image for producers/developers. You can choose your own image and adjust it with the same cover editor used for games.",
        ["Elegir / reemplazar imagen…"] = "Choose / replace image…",
        ["Editar encuadre…"] = "Edit framing…",
        ["Quitar portada"] = "Remove cover",
    };

    private static readonly Dictionary<string, string> EnToEs = EsToEn.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    public static string CurrentLanguage { get; private set; } = "en";
    public static bool IsSpanish => CurrentLanguage == "es";

    public static void SetLanguage(string? language)
        => CurrentLanguage = string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";

    public static string Bi(string es, string en) => IsSpanish ? es : en;

    public static string T(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (IsSpanish)
            return EnToEs.TryGetValue(text, out var es) ? es : text;
        return EsToEn.TryGetValue(text, out var en) ? en : text;
    }

    public static void Apply(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        Visit(root, seen);
    }

    private static void Visit(DependencyObject obj, HashSet<DependencyObject> seen)
    {
        if (!seen.Add(obj)) return;

        if (obj is Window window)
            window.Title = T(window.Title);
        if (obj is TextBlock textBlock)
            textBlock.Text = T(textBlock.Text);
        if (obj is ContentControl contentControl && contentControl.Content is string content)
            contentControl.Content = T(content);
        if (obj is HeaderedContentControl headered && headered.Header is string header)
            headered.Header = T(header);
        if (obj is FrameworkElement element && element.ToolTip is string tip)
            element.ToolTip = T(tip);

        foreach (var child in LogicalTreeHelper.GetChildren(obj).OfType<DependencyObject>())
            Visit(child, seen);

        var visualCount = 0;
        try { visualCount = VisualTreeHelper.GetChildrenCount(obj); } catch { }
        for (var i = 0; i < visualCount; i++)
        {
            try { Visit(VisualTreeHelper.GetChild(obj, i), seen); } catch { }
        }
    }
}
