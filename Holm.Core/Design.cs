using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WinNotch;

/// <summary>
/// One look for the notch and the Holm window: its surfaces, ink, accent, corners and type. Colours are ARGB.
/// Built in (<see cref="Designs.BuiltIn"/>) or imported from a small JSON file (<see cref="Designs.Parse"/>).
/// Read once at start; switching restarts Holm.
/// </summary>
internal sealed record Design
{
    public string Id { get; init; } = "classic";
    public string Name { get; init; } = "Classic";
    public string Description { get; init; } = "";
    /// <summary>The instrument layout: tools as words, small corners, plain lists, no greeting.</summary>
    public bool Instrument { get; init; }
    /// <summary>The cover's colours wash the notch and the sky glows in it; off, the notch keeps its own fill.</summary>
    public bool Wash { get; init; } = true;
    /// <summary>How dark the cover wash is (1 as the classic look, less is brighter).</summary>
    public double Shade { get; init; } = 1;

    // The notch
    public uint[] Fill { get; init; } = { 0xFF000000 };
    /// <summary>"vertical", "diagonal" or "conic" when <see cref="Fill"/> has more than one colour.</summary>
    public string FillKind { get; init; } = "vertical";
    /// <summary>A line around the notch and the window, or 0.</summary>
    public uint Edge { get; init; }
    public double EdgeWidth { get; init; } = 1.5;
    /// <summary>The edge glows.</summary>
    public bool Glow { get; init; }
    /// <summary>Text and icons.</summary>
    public uint Ink { get; init; } = 0xFFFFFFFF;

    // Both
    public uint Accent { get; init; } = 0xFF0A84FF;
    /// <summary>Switches when on, if not the accent.</summary>
    public uint? Toggle { get; init; }

    // The Holm window
    public uint Panel { get; init; } = 0xFA1E1E20;
    public uint Sidebar { get; init; } = 0xFF171719;
    public uint? Card { get; init; }
    public uint? CardStrong { get; init; }
    public uint? Hover { get; init; }
    public uint? Selected { get; init; }
    public uint? Line { get; init; }
    public uint? WindowEdge { get; init; }
    public uint? Dim { get; init; }
    public uint? Faint { get; init; }
    /// <summary>The window's corners; the rows' corners follow.</summary>
    public double Corners { get; init; } = 14;
    /// <summary>The window is frosted glass where the system can blur (Windows), see-through elsewhere.</summary>
    public bool Glass { get; init; }
    /// <summary>A hard shadow under the window, or 0.</summary>
    public uint Shadow { get; init; }
    /// <summary>"sans", "mono" or "serif".</summary>
    public string Font { get; init; } = "sans";
    /// <summary>Headings and tool words in capitals.</summary>
    public bool Caps { get; init; }
    /// <summary>Imported from a file (the file's path), or null when built in.</summary>
    public string? Source { get; init; }

    public uint CardColor => Card ?? Designs.WithAlpha(Ink, 0x14);
    public uint CardStrongColor => CardStrong ?? Designs.WithAlpha(Ink, 0x1F);
    public uint HoverColor => Hover ?? Designs.WithAlpha(Ink, 0x10);
    public uint SelectedColor => Selected ?? Designs.WithAlpha(Ink, 0x26);
    public uint LineColor => Line ?? Designs.WithAlpha(Ink, 0x14);
    public uint WindowEdgeColor => WindowEdge ?? (Edge != 0 ? Edge : Designs.WithAlpha(Ink, 0x2E));
    public uint DimColor => Dim ?? Designs.WithAlpha(Ink, 0x99);
    public uint FaintColor => Faint ?? Designs.WithAlpha(Ink, 0x66);
    public uint ToggleColor => Toggle ?? Accent;
    /// <summary>The design is dark (light ink).</summary>
    public bool IsDark => Designs.Luminance(Ink) > 0.5;
}

/// <summary>The built-in designs and the imported ones (JSON files in the designs folder).</summary>
internal static class Designs
{
    public static readonly string Folder = Path.Combine(Diag.Folder, "designs");
    const int MaxFileBytes = 64 * 1024;

    public static readonly IReadOnlyList<Design> BuiltIn = new Design[]
    {
        new() { Id = "classic", Name = "Classic", Description = "Holm as it always was: black, round, the cover's colours", Toggle = 0xFF30D158 },
        new()
        {
            Id = "instrument", Name = "Instrument", Description = "Black with white ink, small corners, tools as words", Instrument = true, Wash = false,
            Ink = 0xFFF2F2F2, Accent = 0xFFF2F2F2, Panel = 0xFF0B0B0C, Sidebar = 0xFF000000, WindowEdge = 0xFF26272A, Card = 0xFF141416,
            CardStrong = 0xFF1C1D20, Hover = 0xFF121214, Selected = 0xFF1C1D20, Line = 0xFF26272A, Dim = 0xFF8A8C91, Faint = 0xFF5C5E63, Corners = 4,
        },
        new()
        {
            Id = "frosted", Name = "Frosted Glass", Description = "Milky glass the desktop shimmers through", Wash = false,
            Fill = new[] { 0xC0343A48u, 0xA8202430u }, Edge = 0x59FFFFFF, EdgeWidth = 1, Accent = 0xFF7FB2FF,
            Panel = 0xB8262A34, Sidebar = 0x40FFFFFF, Card = 0x1FFFFFFF, Corners = 18, Glass = true,
        },
        new()
        {
            Id = "neon", Name = "Neon", Description = "Glowing lines instead of surfaces", Instrument = true, Wash = false,
            Fill = new[] { 0xFF07040Du }, Edge = 0xFFFF2BD6, EdgeWidth = 1.5, Glow = true, Ink = 0xFFE9E4FF, Accent = 0xFF22F0FF,
            Panel = 0xFF07040D, Sidebar = 0xFF0B0614, Card = 0x00000000, Line = 0x5522F0FF, Dim = 0xFFA99BD6, Corners = 10, Caps = true,
        },
        new()
        {
            Id = "paper", Name = "Paper", Description = "Light and warm like paper, serif headings", Wash = false,
            Fill = new[] { 0xFFFBF8F2u }, Ink = 0xFF2B2724, Accent = 0xFFC2410C, Panel = 0xFFFBF8F2, Sidebar = 0xFFF3EEE5,
            Card = 0xFFFFFFFF, Line = 0xFFECE5D8, WindowEdge = 0xFFE3DBCC, Dim = 0xFF7A6F63, Font = "serif",
        },
        new()
        {
            Id = "terminal", Name = "Terminal", Description = "Everything in monospace, green on black", Instrument = true, Wash = false,
            Fill = new[] { 0xFF020402u }, Edge = 0xFF1D5A2E, EdgeWidth = 1, Ink = 0xFF39FF7A, Accent = 0xFF39FF7A,
            Panel = 0xFF020402, Sidebar = 0xFF030803, Card = 0x00000000, Line = 0xFF1D5A2E, Dim = 0xFF1FAE4F, Corners = 0, Font = "mono",
        },
        new()
        {
            Id = "brutalist", Name = "Brutalist", Description = "Thick black lines, hard shadows, loud", Instrument = true, Wash = false,
            Fill = new[] { 0xFFFFFFFFu }, Edge = 0xFF000000, EdgeWidth = 3, Ink = 0xFF000000, Accent = 0xFFFF4D00,
            Panel = 0xFFFFFFFF, Sidebar = 0xFFF4E04D, Card = 0xFFFFFFFF, Line = 0xFF000000, Dim = 0xFF333333, Faint = 0xFF555555,
            Selected = 0xFFFF4D00, Corners = 0, Shadow = 0xFF000000, Caps = true,
        },
        new()
        {
            Id = "aurora", Name = "Aurora", Description = "Northern lights in the notch, very round", Wash = false,
            Fill = new[] { 0xFF00785Au, 0xFF036A9Au, 0xFF6A1590u, 0xFF9A4410u, 0xFF00785Au }, FillKind = "conic",
            Accent = 0xFF03E1FF, Panel = 0xFF0B0C16, Sidebar = 0xFF080912, Card = 0x0DFFFFFF, Corners = 22,
        },
        new()
        {
            Id = "liquid", Name = "Prism Glass", Description = "Translucent glass with a curved, light-catching rim", Wash = false,
            Fill = new[] { 0xBF2A303Du, 0xB8181C26u, 0xC8202334u }, WindowEdge = 0x50DFE8FF,
            Ink = 0xFFF7FAFF, Accent = 0xFFB4E6FF, Toggle = 0xFF59DDAD, Dim = 0xFFE0E7F5, Faint = 0xFFC0CADC,
            Panel = 0xC41C202B, Sidebar = 0x18232E45, Card = 0x0EF7FAFF, CardStrong = 0x1EF7FAFF,
            Hover = 0x18F7FAFF, Selected = 0x354E98CA, Line = 0x24E3EDFF, Corners = 24, Glass = true,
        },
        new()
        {
            Id = "material", Name = "Material", Description = "Soft pastel surfaces, thick bars, pill buttons", Wash = false,
            Fill = new[] { 0xFFE8DEF8u }, Ink = 0xFF1D192B, Accent = 0xFF6750A4, Panel = 0xFFF7F2FA, Sidebar = 0xFFF3EDF7,
            Card = 0xFFECE6F0, Selected = 0xFFE8DEF8, Line = 0x00000000, Dim = 0xFF49454F, Corners = 28,
        },
        new()
        {
            Id = "mica", Name = "Mica", Description = "Tinted like Windows 11, small corners, a line for the accent", Wash = false,
            Fill = new[] { 0xE6202834u }, Edge = 0x14FFFFFF, EdgeWidth = 1, Accent = 0xFF60CDFF,
            Panel = 0xD8202834, Sidebar = 0x14FFFFFF, Card = 0x0DFFFFFF, Corners = 8, Glass = true,
        },
        new()
        {
            Id = "dotmatrix", Name = "Dot Matrix", Description = "Black and white, wide capitals, one red dot", Instrument = true, Wash = false,
            Fill = new[] { 0xFF000000u }, Edge = 0xFF2A2A2A, EdgeWidth = 1, Ink = 0xFFFFFFFF, Accent = 0xFFD71921, Toggle = 0xFFFFFFFF,
            Panel = 0xFF000000, Sidebar = 0xFF000000, Card = 0x00000000, Line = 0xFF2A2A2A, Dim = 0xFF8A8A8A, Corners = 16, Font = "mono", Caps = true,
        },
        new()
        {
            Id = "hardware", Name = "Hardware", Description = "Industrial grey, orange keys, tiny labels", Instrument = true, Wash = false,
            Fill = new[] { 0xFFE4E4DFu }, Ink = 0xFF1A1A1A, Accent = 0xFFFF5A00, Panel = 0xFFE4E4DF, Sidebar = 0xFFD6D6D0,
            Card = 0x00000000, Line = 0xFFC5C5BF, Selected = 0xFFFF5A00, Dim = 0xFF5A5A55, Corners = 6, Font = "mono", Caps = true,
        },
        new()
        {
            Id = "aero", Name = "Frutiger Aero", Description = "Blue skies, green hills and glossy water bubbles", Wash = false,
            Fill = new[] { 0xFF74CEFFu, 0xFFCEF8FFu, 0xFF83E6FAu, 0xFF36B8EAu }, Edge = 0xFFE2FCFF, EdgeWidth = 1,
            Ink = 0xFF0A2B43, Accent = 0xFF075C9D, Toggle = 0xFF437D14,
            Panel = 0xFFDFF6FF, Sidebar = 0xFF9DE4FA, Card = 0xFFF4FCFF, CardStrong = 0xFFC0EBFF,
            Hover = 0xFFD6F6FF, Selected = 0xFFC7F48A, Line = 0xFF8EC6D9, WindowEdge = 0xFF4D9AC2,
            Dim = 0xFF214A63, Faint = 0xFF315870, Corners = 16,
        },
        new()
        {
            Id = "retro98", Name = "Retro 98", Description = "Grey 3D edges and a blue title bar", Wash = false,
            Fill = new[] { 0xFFC0C0C0u }, Edge = 0xFF808080, EdgeWidth = 2, Ink = 0xFF000000, Accent = 0xFF000080,
            Panel = 0xFFC0C0C0, Sidebar = 0xFFFFFFFF, Card = 0xFFC0C0C0, Selected = 0xFFA6CAF0, Line = 0xFF808080,
            WindowEdge = 0xFFFFFFFF, Dim = 0xFF404040, Faint = 0xFF707070, Corners = 0, Font = "sans",
        },
        new()
        {
            Id = "pocket", Name = "Pocket", Description = "Four greens and a pixel feel", Instrument = true, Wash = false,
            Fill = new[] { 0xFF9BBC0Fu }, Edge = 0xFF306230, EdgeWidth = 3, Ink = 0xFF0F380F, Accent = 0xFF0F380F,
            Panel = 0xFF9BBC0F, Sidebar = 0xFF8BAC0F, Card = 0x00000000, Line = 0xFF306230, Selected = 0xFF8BAC0F, Dim = 0xFF306230,
            Faint = 0xFF4E7A2A, Corners = 0, Shadow = 0xFF0F380F, Font = "mono", Caps = true,
        },
        new()
        {
            Id = "album", Name = "Album", Description = "The cover's colours flow strongly into the notch", Wash = true, Shade = 0.55,
            Fill = new[] { 0xFF121212u }, Accent = 0xFF1ED760, Panel = 0xFF181818, Sidebar = 0xFF000000, Card = 0xFF242424,
            Selected = 0xFF282828, Dim = 0xFFB3B3B3, Corners = 10,
        },
    };

    /// <summary>The design with this id among the built-in and imported ones, or Classic.</summary>
    public static Design Find(string? id, IEnumerable<Design>? imported = null) =>
        BuiltIn.Concat(imported ?? Array.Empty<Design>()).FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) ?? BuiltIn[0];

    /// <summary>The design to run: the one in the settings (the instrument switch of 1.12.11 counts), with the chosen accent.</summary>
    public static Design Current(AppSettings settings)
    {
        string id = string.IsNullOrEmpty(settings.Design) ? settings.InstrumentLook ? "instrument" : "classic" : settings.Design;
        var design = Find(id, id.StartsWith("custom-", StringComparison.Ordinal) ? LoadImported() : null);
        return TryColor(settings.AccentColor, out uint accent) ? design with { Accent = accent | 0xFF000000, Toggle = null } : design;
    }

    /// <summary>The designs imported into the designs folder; a broken file is skipped.</summary>
    public static IReadOnlyList<Design> LoadImported()
    {
        var list = new List<Design>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (string file in Directory.EnumerateFiles(Folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (new FileInfo(file).Length > MaxFileBytes) continue;
                if (Parse(File.ReadAllText(file), out var design, out _) && design != null) list.Add(design with { Source = file });
            }
        }
        catch (Exception ex) { Diag.Error(ex); }
        return list;
    }

    /// <summary>Copies a design file into the designs folder once it reads. Returns the design, or why not.</summary>
    public static (Design? Design, string? Error) Import(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return (null, "The file is too big for a design (64 KB at most).");
            string json = File.ReadAllText(path);
            if (!Parse(json, out var design, out string? error) || design == null) return (null, error);
            Directory.CreateDirectory(Folder);
            string target = Path.Combine(Folder, design.Id + ".json");
            File.WriteAllText(target, json);
            return (design with { Source = target }, null);
        }
        catch (Exception ex) { return (null, "Holm couldn't read the file: " + ex.Message); }
    }

    /// <summary>
    /// Reads a design from JSON: <c>name</c> is needed, everything else is optional and starts from <c>base</c>
    /// (a built-in design's id, Classic if left out). Colours are "#RGB", "#RRGGBB" or "#AARRGGBB"; numbers are
    /// kept within sensible limits; unknown keys are ignored.
    /// </summary>
    public static bool Parse(string json, out Design? design, out string? error)
    {
        design = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { error = "A design is a JSON object: { \"name\": \"…\", … }."; return false; }
            string? name = Str(root, "name");
            if (string.IsNullOrWhiteSpace(name)) { error = "The design needs a \"name\"."; return false; }
            name = name.Trim();
            if (name.Length > 40) name = name[..40];
            string? baseId = Str(root, "base");
            if (baseId != null && !BuiltIn.Any(d => d.Id == baseId)) { error = $"\"base\" must be one of: {string.Join(", ", BuiltIn.Select(d => d.Id))}."; return false; }
            var d = Find(baseId ?? "classic");
            string slug = new string(name.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-').ToArray()).Trim('-');
            d = d with { Id = "custom-" + (slug.Length > 0 ? slug : "design"), Name = name, Description = Clip(Str(root, "description"), 80) ?? "Imported" };

            string? bad = null;
            uint C(string key, uint fallback)
            {
                if (!root.TryGetProperty(key, out var v)) return fallback;
                if (v.ValueKind == JsonValueKind.String && TryColor(v.GetString(), out uint c)) return c;
                bad ??= key;
                return fallback;
            }
            uint? N(string key, uint? fallback) => root.TryGetProperty(key, out _) ? C(key, fallback ?? 0) : fallback;
            double Num(string key, double fallback, double min, double max) =>
                root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? Math.Clamp(v.GetDouble(), min, max) : fallback;
            bool Flag(string key, bool fallback) =>
                root.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
            string Pick(string key, string fallback, params string[] allowed)
            {
                string? s = Str(root, key);
                if (s == null) return fallback;
                if (allowed.Contains(s)) return s;
                bad ??= key;
                return fallback;
            }

            uint[] fill = d.Fill;
            if (root.TryGetProperty("fill", out var f))
            {
                var colors = f.ValueKind == JsonValueKind.Array ? f.EnumerateArray().Take(8).ToList() : new List<JsonElement> { f };
                var parsed = new List<uint>();
                foreach (var c in colors)
                    if (c.ValueKind == JsonValueKind.String && TryColor(c.GetString(), out uint v)) parsed.Add(v); else bad ??= "fill";
                if (parsed.Count > 0) fill = parsed.ToArray();
            }
            d = d with
            {
                Instrument = Pick("layout", d.Instrument ? "instrument" : "classic", "classic", "instrument") == "instrument",
                Wash = Flag("coverColors", d.Wash),
                Fill = fill,
                FillKind = Pick("fillKind", d.FillKind, "vertical", "diagonal", "conic"),
                Edge = C("edge", d.Edge),
                EdgeWidth = Num("edgeWidth", d.EdgeWidth, 0, 4),
                Glow = Flag("glow", d.Glow),
                Ink = C("ink", d.Ink),
                Accent = C("accent", d.Accent) | 0xFF000000,
                Toggle = N("toggle", d.Toggle),
                Panel = C("panel", d.Panel),
                Sidebar = C("sidebar", d.Sidebar),
                Card = N("card", d.Card),
                CardStrong = N("cardStrong", d.CardStrong),
                Hover = N("hover", d.Hover),
                Selected = N("selected", d.Selected),
                Line = N("line", d.Line),
                WindowEdge = N("windowEdge", d.WindowEdge),
                Dim = N("dim", d.Dim),
                Faint = N("faint", d.Faint),
                Corners = Num("corners", d.Corners, 0, 30),
                Glass = Flag("glass", d.Glass),
                Shadow = C("shadow", d.Shadow),
                Font = Pick("font", d.Font, "sans", "mono", "serif"),
                Caps = Flag("caps", d.Caps),
                Shade = Num("coverShade", d.Shade, 0.2, 1.5),
            };
            if (bad != null) { error = $"\"{bad}\" isn't valid. Colours look like \"#1E1E20\" or \"#CC1E1E20\"."; return false; }
            if (Contrast(d.Ink, d.Panel) < 2.5) { error = "\"ink\" and \"panel\" are too alike to read."; return false; }
            design = d;
            error = null;
            return true;
        }
        catch (JsonException ex) { error = "This isn't valid JSON: " + ex.Message; return false; }
    }

    /// <summary>Reads "#RGB", "#RRGGBB" or "#AARRGGBB" as ARGB.</summary>
    public static bool TryColor(string? text, out uint argb)
    {
        argb = 0;
        if (text is not { Length: 4 or 7 or 9 } || text[0] != '#') return false;
        string hex = text[1..];
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        argb = hex.Length switch
        {
            3 => 0xFF000000 | ((v >> 8 & 0xF) * 0x11 << 16) | ((v >> 4 & 0xF) * 0x11 << 8) | (v & 0xF) * 0x11,
            6 => 0xFF000000 | v,
            _ => v,
        };
        return true;
    }

    public static uint WithAlpha(uint argb, byte alpha) => (uint)alpha << 24 | argb & 0xFFFFFF;

    /// <summary>Relative luminance (0 black … 1 white), alpha ignored.</summary>
    public static double Luminance(uint argb)
    {
        static double L(uint c) { double s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(argb >> 16 & 0xFF) + 0.7152 * L(argb >> 8 & 0xFF) + 0.0722 * L(argb & 0xFF);
    }

    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Black or white, whichever reads better on this colour.</summary>
    public static uint OnColor(uint argb) => Contrast(argb, 0xFFFFFFFF) >= 3 ? 0xFFFFFFFF : 0xFF0B0B0C; // white unless it can't be read, as on Classic's blue

    static string? Str(JsonElement root, string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static string? Clip(string? s, int max) => s == null ? null : s.Length > max ? s[..max] : s;
}
