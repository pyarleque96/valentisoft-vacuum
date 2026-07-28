using SkiaSharp;

namespace Valentinos.Api.Kpi;

// Genera el PDF del reporte de KPIs (mismo contenido que la página) con SkiaSharp.
public static class KpiPdf
{
    private const float W = 595f, Hgt = 842f, M = 44f;

    public static byte[] Render(KpiModel m)
    {
        using var ms = new MemoryStream();
        using (var doc = SKDocument.CreatePdf(ms))
        {
            var canvas = doc.BeginPage(W, Hgt);
            float y = M;

            using var fTitle = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 20);
            using var fH2 = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 14);
            using var fBold = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 11);
            using var f = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 11);
            using var fSmall = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 9);
            using var ink = new SKPaint { Color = new SKColor(0x0f, 0x17, 0x2a), IsAntialias = true };
            using var blue = new SKPaint { Color = new SKColor(0x15, 0x60, 0xA8), IsAntialias = true };
            using var gray = new SKPaint { Color = new SKColor(0x64, 0x74, 0x8b), IsAntialias = true };
            using var line = new SKPaint { Color = new SKColor(0xe5, 0xe9, 0xf0), IsAntialias = true, StrokeWidth = 1 };

            void NewPageIfNeeded(float need)
            {
                if (y + need <= Hgt - M) return;
                doc.EndPage();
                canvas = doc.BeginPage(W, Hgt);
                y = M;
            }
            void Text(string s, SKFont fnt, SKPaint p, float x) => canvas.DrawText(s ?? "", x, y, fnt, p);
            void H2(string s) { NewPageIfNeeded(30); y += 8; Text(s, fH2, blue, M); y += 8; canvas.DrawLine(M, y, W - M, y, line); y += 16; }
            void Kv(string k, string v) { NewPageIfNeeded(20); Text(k, f, gray, M); Text(v, fBold, ink, M + 220); y += 18; }
            string Cut(string s, int max) => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

            // Título
            Text("KPI Report — Vacuum Control", fTitle, ink, M); y += 20;
            Text($"{m.TenantName} · Housekeeping · generated {m.GeneratedAt:g}", fSmall, gray, M); y += 22;

            // ---- Daily ----
            H2("Daily report");
            float[] cx = { M, M + 150, M + 250, M + 400 };
            Text("Employee", fBold, gray, cx[0]); Text("Vacuum", fBold, gray, cx[1]);
            Text("Status", fBold, gray, cx[2]); Text("Notes", fBold, gray, cx[3]); y += 6;
            canvas.DrawLine(M, y, W - M, y, line); y += 16;
            if (m.DailyRows.Count == 0) { Text("No check-ins today yet.", f, gray, M); y += 18; }
            foreach (var r in m.DailyRows)
            {
                NewPageIfNeeded(18);
                Text(Cut(r.Employee, 24), f, ink, cx[0]);
                Text(r.Vacuum, f, ink, cx[1]);
                Text(Cut(r.StatusLabel, 20), f, ink, cx[2]);
                Text(Cut(r.Notes, 18), f, ink, cx[3]);
                y += 17;
            }
            y += 6;
            Text($"Summary — Operational: {m.DOperational} | With faults: {m.DFaults} | Out of service: {m.DOutOfService} | No vacuum: {m.DUnavailable} | Total: {m.DTotal}",
                fSmall, gray, M); y += 16;
            Text($"Vacuums with problems: {(m.DProblemAssets.Count > 0 ? string.Join(", ", m.DProblemAssets) : "None")}", fSmall, gray, M); y += 10;

            // ---- Weekly ----
            H2("Weekly KPIs");
            Kv("Total check-ins", m.WTotal.ToString());
            Kv("% Operational", m.WOpPct + "%");
            Kv("% With faults", m.WFaultsPct + "%");
            Kv("% Out of service", m.WOosPct + "%");
            Kv("Repairs done", m.WRepairs.ToString());
            Kv("Avg repair time", m.WAvgRepairDays + " days");
            Kv("Most faults", m.WTopFaulty.Count > 0 ? string.Join(", ", m.WTopFaulty.Select(a => $"{a.Vacuum} ({a.Count})")) : "—");

            // ---- Monthly ----
            H2("Monthly KPIs (executive)");
            Kv("Total check-ins", m.MTotal.ToString());
            Kv("Avg availability", m.MAvailabilityPct + "%");
            Kv("Out of service", m.MOos.ToString());
            Kv("Repairs done", m.MRepairs.ToString());
            Kv("Avg out-of-service", m.MAvgOosDays + " days");
            Kv("Most faults", m.MTopFaulty.Count > 0 ? string.Join(", ", m.MTopFaulty.Select(a => $"{a.Vacuum} ({a.Count})")) : "—");
            y += 6; NewPageIfNeeded(30);
            Text("Recommendation:", fBold, ink, M); y += 16;
            Text(Cut(m.MRecommendation, 90), f, ink, M); y += 16;

            doc.EndPage();
            doc.Close();
        }
        return ms.ToArray();
    }
}
