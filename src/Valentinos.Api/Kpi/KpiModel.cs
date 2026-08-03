using System.Globalization;

namespace Valentinos.Api.Kpi;

public record KpiCheckin(string Employee, string Vacuum, string EstadoKey, string? Nota, DateTime CreatedAt);
public record KpiUnavailable(string Employee, string? Nota, DateTime CreatedAt);
public record KpiRow(string Employee, string Vacuum, string EstadoKey, string Notes, string When);
public record UnavailableRow(string Employee, string Note, string When);
public record AssetCount(string Vacuum, int Count);
public record DayCount(string Label, int Total, int Problems);

public record PeriodKpi(
    string TenantName,
    DateTime GeneratedAt,
    string Period,          // daily | weekly | monthly
    string PeriodLabel,     // "Today" / "Last 7 days" / "Last 30 days"
    int Op, int Fa, int Oos, int Un, int Total,
    int OpPct, int FaPct, int OosPct, int UnPct,
    IReadOnlyList<KpiRow> Rows,
    IReadOnlyList<AssetCount> TopFaulty,
    IReadOnlyList<string> ProblemAssets,
    int Repairs, double AvgRepairDays,
    int AvailabilityPct, string Recommendation,
    IReadOnlyList<DayCount> Trend, bool ShowTrend,
    IReadOnlyList<UnavailableRow> Unavailable);

public static class Kpi
{
    public static PeriodKpi Compute(string tenantName, IReadOnlyList<KpiCheckin> all,
        IReadOnlyList<KpiUnavailable> unavailableAll, DateTime now, string period)
    {
        period = period is "weekly" or "monthly" ? period : "daily";
        var today = now.Date;
        var (start, label, days) = period switch
        {
            "weekly" => (today.AddDays(-6), "Last 7 days", 7),
            "monthly" => (today.AddDays(-29), "Last 30 days", 30),
            _ => (today, "Today", 1)
        };

        bool IsProblem(string k) => k is "AMedias" or "NoFunciona";
        int Pct(int n, int t) => t == 0 ? 0 : (int)Math.Round(100.0 * n / t);
        // Diario -> hora del registro; semanal/mensual -> fecha.
        string WhenStr(DateTime dt) => period == "daily"
            ? dt.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : dt.ToString("MMM d", CultureInfo.InvariantCulture);

        var items = all.Where(c => c.CreatedAt.Date >= start && c.CreatedAt.Date <= today)
                       .OrderByDescending(c => c.CreatedAt).ToList();

        // Reportes de "equipo no disponible" del periodo (tabla/flujo propio).
        var unItems = unavailableAll.Where(u => u.CreatedAt.Date >= start && u.CreatedAt.Date <= today)
                                    .OrderByDescending(u => u.CreatedAt).ToList();

        var op = items.Count(c => c.EstadoKey == "operational");
        var fa = items.Count(c => c.EstadoKey == "AMedias");
        var oos = items.Count(c => c.EstadoKey == "NoFunciona");
        var un = unItems.Count;
        var total = op + fa + oos + un;

        // Detalle: en diario todas las filas; en semanal/mensual solo los problemas (accionables).
        var detailSrc = period == "daily" ? items : items.Where(c => IsProblem(c.EstadoKey)).ToList();
        // Notas completas (sin recortar). En la página se muestran con salto de línea
        // (wrap) a cierto ancho; no se truncan.
        string NoteStr(string? n) => string.IsNullOrWhiteSpace(n) ? "—" : n!.Trim();
        var rows = detailSrc.Take(60).Select(c => new KpiRow(
            c.Employee, c.Vacuum, c.EstadoKey, NoteStr(c.Nota), WhenStr(c.CreatedAt))).ToList();

        // Lista de "equipo no disponible": hora (diario) / fecha (semanal), nota recortada.
        var unavailable = unItems.Take(60).Select(u => new UnavailableRow(
            u.Employee, NoteStr(u.Nota), WhenStr(u.CreatedAt))).ToList();

        var topFaulty = items.Where(c => IsProblem(c.EstadoKey))
            .GroupBy(c => c.Vacuum).Select(g => new AssetCount(g.Key, g.Count()))
            .OrderByDescending(a => a.Count).ThenBy(a => a.Vacuum).Take(6).ToList();
        var problemAssets = items.Where(c => IsProblem(c.EstadoKey))
            .Select(c => c.Vacuum).Distinct().OrderBy(x => x).ToList();

        var repairs = oos / 2 + fa / 3 + 1;                 // fake
        var avgRepair = period == "monthly" ? 2.4 : 1.5;    // fake
        var availability = Pct(op, total);
        var rec = topFaulty.Count > 0
            ? $"Replace {topFaulty[0].Vacuum}, buy new filters and schedule preventive maintenance."
            : "All good — keep monitoring.";

        var trend = new List<DayCount>();
        var showTrend = period != "daily";
        if (showTrend)
        {
            for (var i = days - 1; i >= 0; i--)
            {
                var d0 = today.AddDays(-i);
                var di = all.Where(c => c.CreatedAt.Date == d0).ToList();
                trend.Add(new DayCount(d0.ToString(days > 7 ? "d" : "ddd", CultureInfo.InvariantCulture),
                    di.Count, di.Count(c => IsProblem(c.EstadoKey))));
            }
        }

        return new PeriodKpi(tenantName, now, period, label,
            op, fa, oos, un, total,
            Pct(op, total), Pct(fa, total), Pct(oos, total), Pct(un, total),
            rows, topFaulty, problemAssets,
            repairs, avgRepair, availability, rec, trend, showTrend, unavailable);
    }
}
