using SkiaSharp;

namespace Valentinos.Api.Kpi;

// Genera el PDF del reporte (según el periodo seleccionado) con SkiaSharp.
// Incluye TODO lo que muestra la página: distribución (donut), tendencia por día,
// top de más reportadas (barras), tabla de detalle, stats y KPIs.
public static class KpiPdf
{
    private const float W = 595f, Hgt = 842f, M = 44f;
    private static readonly SKColor CGreen = new(0x16, 0xa3, 0x4a);
    private static readonly SKColor CAmber = new(0xf5, 0x9e, 0x0b);
    private static readonly SKColor CRed = new(0xef, 0x44, 0x44);
    private static readonly SKColor CGray = new(0x94, 0xa3, 0xb8);

    private static string StLabel(string k) => k switch
    {
        "operational" => "Operational",
        "AMedias" => "Working with faults",
        "NoFunciona" => "Out of service",
        _ => "No vacuum available"
    };

    public static byte[] Render(PeriodKpi m) => RenderMulti(new[] { m });

    // Un solo PDF con el reporte de VARIOS sites, uno tras otro; cada site empieza
    // en una página nueva (cada DrawOne hace su propio BeginPage inicial).
    public static byte[] RenderMulti(IReadOnlyList<PeriodKpi> models)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            using var fTitle = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 20);
            using var fH2 = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 14);
            using var fBold = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 11);
            using var f = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 11);
            using var fSmall = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 9);
            using var ink = new SKPaint { Color = new SKColor(0x0f, 0x17, 0x2a), IsAntialias = true };
            using var blue = new SKPaint { Color = new SKColor(0x15, 0x60, 0xA8), IsAntialias = true };
            using var gray = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8b), IsAntialias = true };
            using var line = new SKPaint { Color = new SKColor(0xe5, 0xe9, 0xf0), IsAntialias = true, StrokeWidth = 1 };
            using var track = new SKPaint { Color = new SKColor(0xee, 0xf2, 0xf6), IsAntialias = true };
            using var fill = new SKPaint { IsAntialias = true };
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };

            // Dibuja el reporte de UN site, empezando en una página nueva.
            void DrawOne(PeriodKpi m)
            {
            var pName = m.Period switch { "weekly" => "Weekly", "monthly" => "Monthly", _ => "Daily" };
            var canvas = doc.BeginPage(W, Hgt);
            float y = M;

            void NewPageIfNeeded(float need)
            {
                if (y + need <= Hgt - M) return;
                doc.EndPage(); canvas = doc.BeginPage(W, Hgt); y = M;
            }
            void Text(string s, SKFont fnt, SKPaint p, float x) => canvas.DrawText(s ?? "", x, y, SKTextAlign.Left, fnt, p);
            void TextAt(string s, SKFont fnt, SKPaint p, float x, float yy, SKTextAlign a = SKTextAlign.Left)
                => canvas.DrawText(s ?? "", x, yy, a, fnt, p);
            void H2(string s) { NewPageIfNeeded(40); y += 8; Text(s, fH2, blue, M); y += 8; canvas.DrawLine(M, y, W - M, y, line); y += 18; }
            void Kv(string k, string v) { NewPageIfNeeded(20); Text(k, f, gray, M); Text(v, fBold, ink, M + 240); y += 18; }
            // Igual que Kv pero el valor hace wrap dentro del ancho disponible (evita desborde).
            void KvWrap(string k, string v)
            {
                float vx = M + 240, maxW = W - M - vx;
                var words = (string.IsNullOrWhiteSpace(v) ? "—" : v).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var lines = new List<string>();
                var cur = "";
                foreach (var w in words)
                {
                    var trial = cur.Length == 0 ? w : cur + " " + w;
                    if (fBold.MeasureText(trial) > maxW && cur.Length > 0) { lines.Add(cur); cur = w; }
                    else cur = trial;
                }
                if (cur.Length > 0) lines.Add(cur);
                if (lines.Count == 0) lines.Add("—");
                NewPageIfNeeded(lines.Count * 15 + 6);
                Text(k, f, gray, M);
                for (var i = 0; i < lines.Count; i++)
                    TextAt(lines[i], fBold, ink, vx, y + i * 15);
                y += lines.Count * 15 + 3;
            }
            string Cut(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");
            void RoundRect(float x, float yy, float w, float h, SKColor c, float r = 6)
            { fill.Color = c; canvas.DrawRoundRect(x, yy, w, h, r, r, fill); }

            // ---------- Encabezado ----------
            var siteLabel = m.TenantName.Contains('·') ? m.TenantName[(m.TenantName.LastIndexOf('·') + 1)..].Trim() : m.TenantName;
            var tenantOnly = m.TenantName.Contains('·') ? m.TenantName[..m.TenantName.IndexOf('·')].Trim() : m.TenantName;
            Text($"{pName} Report — {siteLabel}", fTitle, ink, M); y += 20;
            Text($"{tenantOnly} · Housekeeping · {m.PeriodLabel} · {m.GeneratedAt.ToString("MMM d, yyyy · h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}", fSmall, gray, M); y += 22;

            // ---------- Distribución: donut + leyenda ----------
            H2("Status distribution");
            {
                NewPageIfNeeded(150);
                float cx = M + 66, cy = y + 60, rOut = 60, rIn = 34;
                int total = Math.Max(1, m.Total);
                var segs = new (int n, SKColor c)[] { (m.Op, CGreen), (m.Fa, CAmber), (m.Oos, CRed), (m.Un, CGray) };
                var oval = new SKRect(cx - rOut, cy - rOut, cx + rOut, cy + rOut);
                var nonZero = segs.Count(s => s.n > 0);
                if (nonZero == 0)
                {
                    // Sin data: anillo gris claro (evita dona vacía).
                    fill.Color = new SKColor(0xee, 0xf2, 0xf6); canvas.DrawCircle(cx, cy, rOut, fill);
                }
                else if (nonZero == 1)
                {
                    // Una sola categoría al 100%: un arco de 360° no se dibuja en Skia -> círculo completo.
                    fill.Color = segs.First(s => s.n > 0).c; canvas.DrawCircle(cx, cy, rOut, fill);
                }
                else
                {
                    float start = -90f;
                    foreach (var (n, c) in segs)
                    {
                        if (n <= 0) continue;
                        float sweep = 360f * n / total;
                        using var path = new SKPath();
                        path.MoveTo(cx, cy);
                        path.ArcTo(oval, start, sweep, false);
                        path.Close();
                        fill.Color = c; canvas.DrawPath(path, fill);
                        start += sweep;
                    }
                }
                canvas.DrawCircle(cx, cy, rIn, white); // agujero del donut

                // Leyenda a la derecha.
                float lx = cx + rOut + 30, ly = y + 14;
                void LegRow(SKColor c, string label, int n)
                {
                    RoundRect(lx, ly - 9, 11, 11, c, 3);
                    TextAt(label, f, ink, lx + 20, ly);
                    TextAt(n.ToString(), fBold, ink, W - M, ly, SKTextAlign.Right);
                    ly += 24;
                }
                LegRow(CGreen, "Operational", m.Op);
                LegRow(CAmber, "Working with faults", m.Fa);
                LegRow(CRed, "Out of service", m.Oos);
                LegRow(CGray, "No vacuum available", m.Un);
                y = Math.Max(cy + rOut, ly) + 12;
            }

            // ---------- Tendencia (check-ins por día) ----------
            if (m.ShowTrend && m.Trend.Count > 0)
            {
                H2("Check-ins per day");
                NewPageIfNeeded(120);
                float chartTop = y, chartH = 90f, baseline = chartTop + chartH;
                float areaW = W - 2 * M;
                int nb = m.Trend.Count;
                float gap = nb > 12 ? 3f : 6f;
                float bw = Math.Min(26f, (areaW - gap * (nb - 1)) / nb);
                float totW = bw * nb + gap * (nb - 1);
                float x0 = M + (areaW - totW) / 2f;
                int maxT = Math.Max(1, m.Trend.Max(d => d.Total));
                bool showLbl = nb <= 10;
                float x = x0;
                foreach (var d in m.Trend)
                {
                    float hp = chartH * d.Total / maxT;
                    float bt = baseline - hp;
                    RoundRect(x, bt, bw, Math.Max(2f, hp), new SKColor(0x15, 0x60, 0xA8), 3);
                    if (d.Problems > 0)
                    {
                        float php = hp * d.Problems / Math.Max(1, d.Total);
                        RoundRect(x, baseline - php, bw, Math.Max(2f, php), CRed, 3);
                    }
                    if (showLbl) TextAt(d.Label, fSmall, gray, x + bw / 2f, baseline + 12, SKTextAlign.Center);
                    x += bw + gap;
                }
                canvas.DrawLine(M, baseline, W - M, baseline, line);
                y = baseline + (showLbl ? 22 : 12);
                // Leyenda tendencia.
                RoundRect(M, y - 8, 10, 10, new SKColor(0x15, 0x60, 0xA8), 3);
                TextAt("Total", fSmall, gray, M + 16, y);
                RoundRect(M + 70, y - 8, 10, 10, CRed, 3);
                TextAt("Problems", fSmall, gray, M + 86, y);
                y += 14;
            }

            // ---------- Más reportadas (barras horizontales) ----------
            H2("Most reported");
            if (m.TopFaulty.Count == 0) { Text("No problems in this period.", f, gray, M); y += 18; }
            else
            {
                int maxF = Math.Max(1, m.TopFaulty.Max(a => a.Count));
                float barLeft = M + 80, barRight = W - M - 30, barW = barRight - barLeft;
                foreach (var a in m.TopFaulty)
                {
                    NewPageIfNeeded(24);
                    Text(a.Vacuum, fBold, ink, M);
                    float fw = barW * a.Count / maxF;
                    RoundRect(barLeft, y - 9, barW, 11, new SKColor(0xee, 0xf2, 0xf6), 5);
                    RoundRect(barLeft, y - 9, Math.Max(4f, fw), 11, CRed, 5);
                    TextAt(a.Count.ToString(), fBold, ink, W - M, y, SKTextAlign.Right);
                    y += 22;
                }
            }

            // ---------- Detalle ----------
            // Sin columna de notas (el texto se salía); en su lugar Fecha (o Hora en diario).
            var whenHdr = m.Period == "daily" ? "Time" : "Date";
            H2($"Detail ({m.Rows.Count})");
            float[] cx2 = { M, M + 170, M + 270, M + 380 };
            Text("Employee", fBold, gray, cx2[0]); Text("Vacuum", fBold, gray, cx2[1]);
            Text(whenHdr, fBold, gray, cx2[2]); Text("Status", fBold, gray, cx2[3]); y += 6;
            canvas.DrawLine(M, y, W - M, y, line); y += 16;
            if (m.Rows.Count == 0) { Text("No records for this period.", f, gray, M); y += 18; }
            foreach (var r in m.Rows)
            {
                NewPageIfNeeded(18);
                Text(Cut(r.Employee, 26), f, ink, cx2[0]);
                Text(r.Vacuum, f, ink, cx2[1]);
                Text(r.When, f, gray, cx2[2]);
                Text(Cut(StLabel(r.EstadoKey), 24), f, ink, cx2[3]);
                y += 17;
            }
            y += 6; NewPageIfNeeded(20);
            Text($"Vacuums with problems: {(m.ProblemAssets.Count > 0 ? string.Join(", ", m.ProblemAssets) : "None")}", fSmall, gray, M); y += 14;

            // ---------- Equipo no disponible (sin columna de nota, por diseño) ----------
            var whenHdr2 = m.Period == "daily" ? "Time" : "Date";
            H2($"Unavailable equipment ({m.Unavailable.Count})");
            float[] ux = { M, M + 130 };
            Text(whenHdr2, fBold, gray, ux[0]); Text("Employee", fBold, gray, ux[1]); y += 6;
            canvas.DrawLine(M, y, W - M, y, line); y += 16;
            if (m.Unavailable.Count == 0) { Text("No unavailable-equipment reports for this period.", f, gray, M); y += 18; }
            foreach (var u in m.Unavailable)
            {
                NewPageIfNeeded(18);
                Text(u.When, f, gray, ux[0]);
                Text(Cut(u.Employee, 40), f, ink, ux[1]);
                y += 17;
            }

            // ---------- Resumen (stats) ----------
            H2("Summary");
            {
                NewPageIfNeeded(60);
                var cells = new (string n, string l)[]
                {
                    (m.Op.ToString(), "Operational"), (m.Fa.ToString(), "With faults"),
                    (m.Oos.ToString(), "Out of service"), (m.Un.ToString(), "No vacuum"),
                    (m.Total.ToString(), "Total")
                };
                float gap = 8f, cw = (W - 2 * M - gap * (cells.Length - 1)) / cells.Length, ch = 44f, x = M;
                foreach (var (n, l) in cells)
                {
                    RoundRect(x, y, cw, ch, new SKColor(0xf8, 0xfa, 0xfc), 8);
                    TextAt(n, fH2, ink, x + 10, y + 22);
                    TextAt(l, fSmall, gray, x + 10, y + 36);
                    x += cw + gap;
                }
                y += ch + 12;
            }

            // ---------- KPIs ----------
            H2("KPIs");
            Kv("Availability", m.AvailabilityPct + "%");
            Kv("% With faults", m.FaPct + "%");
            Kv("% Out of service", m.OosPct + "%");
            KvWrap("Most faults", m.TopFaulty.Count > 0 ? string.Join(", ", m.TopFaulty.Select(a => $"{a.Vacuum} ({a.Count})")) : "—");

            doc.EndPage();
            } // fin DrawOne

            if (models.Count == 0) { doc.BeginPage(W, Hgt); doc.EndPage(); } // documento no vacío
            else foreach (var sm in models) DrawOne(sm);
            doc.Close();
        }
        return ms.ToArray();
    }
}
