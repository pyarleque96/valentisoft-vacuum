namespace Valentinos.Api.Kpi;

public record KpiCheckin(string Employee, string Vacuum, string EstadoKey, string? Nota, DateTime CreatedAt);

public record KpiDailyRow(string Employee, string Vacuum, string StatusLabel, string EstadoKey, string Notes);

public record AssetCount(string Vacuum, int Count);

public record KpiModel(
    string TenantName,
    DateTime GeneratedAt,
    // Diario
    IReadOnlyList<KpiDailyRow> DailyRows,
    int DOperational, int DFaults, int DOutOfService, int DUnavailable, int DTotal,
    IReadOnlyList<string> DProblemAssets,
    // Semanal
    int WTotal, int WOpPct, int WFaultsPct, int WOosPct,
    IReadOnlyList<AssetCount> WTopFaulty, int WRepairs, double WAvgRepairDays,
    // Mensual
    int MTotal, int MAvailabilityPct, int MOos, int MRepairs, double MAvgOosDays,
    IReadOnlyList<AssetCount> MTopFaulty, string MRecommendation);

public static class Kpi
{
    public static string Label(string estadoKey) => estadoKey switch
    {
        "operational" => "Operational",
        "AMedias" => "Working with faults",
        "NoFunciona" => "Out of service",
        "unavailable" => "No vacuum available",
        _ => estadoKey
    };

    public static KpiModel Compute(string tenantName, IReadOnlyList<KpiCheckin> all, DateTime now)
    {
        var today = now.Date;
        var weekStart = today.AddDays(-6);
        var monthStart = today.AddDays(-29);

        bool IsProblem(string k) => k is "AMedias" or "NoFunciona";

        // ---- Diario ----
        var day = all.Where(c => c.CreatedAt.Date == today).OrderBy(c => c.CreatedAt).ToList();
        var dailyRows = day.Select(c => new KpiDailyRow(
            c.Employee, c.Vacuum, Label(c.EstadoKey), c.EstadoKey,
            string.IsNullOrWhiteSpace(c.Nota) ? "—" : c.Nota!)).ToList();
        var dOp = day.Count(c => c.EstadoKey == "operational");
        var dFa = day.Count(c => c.EstadoKey == "AMedias");
        var dOos = day.Count(c => c.EstadoKey == "NoFunciona");
        var dUn = day.Count(c => c.EstadoKey == "unavailable");
        var dProblemAssets = day.Where(c => IsProblem(c.EstadoKey))
            .Select(c => c.Vacuum).Distinct().OrderBy(x => x).ToList();

        // ---- Semanal ----
        var week = all.Where(c => c.CreatedAt.Date >= weekStart).ToList();
        var wTotal = week.Count;
        int Pct(int n, int t) => t == 0 ? 0 : (int)Math.Round(100.0 * n / t);
        var wOp = Pct(week.Count(c => c.EstadoKey == "operational"), wTotal);
        var wFa = Pct(week.Count(c => c.EstadoKey == "AMedias"), wTotal);
        var wOos = Pct(week.Count(c => c.EstadoKey == "NoFunciona"), wTotal);
        var wTopFaulty = TopFaulty(week, IsProblem, 3);
        var wRepairs = week.Count(c => c.EstadoKey == "NoFunciona") / 2 + 1; // fake
        var wAvgRepair = 1.5;

        // ---- Mensual ----
        var month = all.Where(c => c.CreatedAt.Date >= monthStart).ToList();
        var mTotal = month.Count;
        var mAvail = Pct(month.Count(c => c.EstadoKey == "operational"), mTotal);
        var mOos = month.Count(c => c.EstadoKey == "NoFunciona");
        var mTopFaulty = TopFaulty(month, IsProblem, 3);
        var mRepairs = mOos / 2 + month.Count(c => c.EstadoKey == "AMedias") / 3; // fake
        var mAvgOos = 2.4;
        var mRec = mTopFaulty.Count > 0
            ? $"Replace {mTopFaulty[0].Vacuum}, buy new filters and schedule monthly preventive maintenance."
            : "Keep monitoring; no critical assets this month.";

        return new KpiModel(
            tenantName, now,
            dailyRows, dOp, dFa, dOos, dUn, day.Count, dProblemAssets,
            wTotal, wOp, wFa, wOos, wTopFaulty, wRepairs, wAvgRepair,
            mTotal, mAvail, mOos, mRepairs, mAvgOos, mTopFaulty, mRec);
    }

    private static List<AssetCount> TopFaulty(IEnumerable<KpiCheckin> src, Func<string, bool> isProblem, int take)
        => src.Where(c => isProblem(c.EstadoKey))
              .GroupBy(c => c.Vacuum)
              .Select(g => new AssetCount(g.Key, g.Count()))
              .OrderByDescending(a => a.Count).ThenBy(a => a.Vacuum)
              .Take(take).ToList();
}
