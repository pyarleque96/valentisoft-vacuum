using System.Net;
using System.Text;
using System.Text.Json;

namespace Valentinos.Api.Kpi;

public static class KpiHtml
{
    private static string H(string s) => WebUtility.HtmlEncode(s);

    public static string Render(KpiModel m, string slug)
    {
        var slugJs = JsonSerializer.Serialize(slug);
        var sb = new StringBuilder();

        // Filas de la tabla diaria.
        var rows = new StringBuilder();
        if (m.DailyRows.Count == 0)
            rows.Append(@"<tr><td colspan=""4"" class=""empty"">No check-ins today yet.</td></tr>");
        foreach (var r in m.DailyRows)
        {
            rows.Append($@"<tr>
              <td>{H(r.Employee)}</td>
              <td class=""mono"">{H(r.Vacuum)}</td>
              <td>{StatusPill(r.EstadoKey, r.StatusLabel)}</td>
              <td>{H(r.Notes)}</td>
            </tr>");
        }

        string Kv(string k, string v) =>
            $@"<div class=""kv""><span>{H(k)}</span><b>{H(v)}</b></div>";

        var wTop = m.WTopFaulty.Count > 0
            ? string.Join(", ", m.WTopFaulty.Select(a => $"{a.Vacuum} ({a.Count})")) : "—";
        var mTop = m.MTopFaulty.Count > 0
            ? string.Join(", ", m.MTopFaulty.Select(a => $"{a.Vacuum} ({a.Count})")) : "—";
        var dProblems = m.DProblemAssets.Count > 0 ? string.Join(", ", m.DProblemAssets) : "None";

        sb.Append($@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>KPI Report · {H(m.TenantName)}</title>
<style>
  :root {{ color-scheme: light; }}
  * {{ box-sizing:border-box; }}
  body {{ margin:0; background:#eef1f5; color:#334155;
    font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; }}
  .wrap {{ max-width:820px; margin:0 auto; padding:20px 16px 60px; }}
  .card {{ background:#fff; border:1px solid #e5e9f0; border-radius:16px; padding:22px;
    box-shadow:0 10px 30px rgba(15,23,42,.06); margin-bottom:18px; }}
  .head {{ display:flex; align-items:center; justify-content:space-between; gap:14px; flex-wrap:wrap; }}
  .brand {{ display:flex; align-items:center; gap:12px; }}
  .logo {{ width:46px; height:46px; border-radius:50%; background:#1560A8; flex:0 0 auto;
    background-image:url(/images/brand/mastercorp-logo.png); background-size:cover; background-position:center; }}
  h1 {{ font-size:20px; margin:0; color:#2b3440; }}
  h2 {{ font-size:16px; margin:0 0 12px; color:#1560A8; }}
  .sub {{ color:#64748b; font-size:13px; margin-top:2px; }}
  .btn {{ background:#1560A8; color:#fff; border:0; border-radius:10px; padding:12px 18px;
    font-weight:700; font-size:14px; cursor:pointer; }}
  .btn:hover {{ background:#0f4c85; }}
  .btn:disabled {{ opacity:.6; cursor:progress; }}
  table {{ width:100%; border-collapse:collapse; font-size:14px; }}
  th, td {{ text-align:left; padding:10px 8px; border-bottom:1px solid #eef2f6; vertical-align:top; }}
  th {{ color:#64748b; font-size:12px; text-transform:uppercase; letter-spacing:.3px; }}
  td.mono {{ font-family:ui-monospace,monospace; font-weight:600; color:#0f172a; }}
  td.empty {{ color:#94a3b8; text-align:center; padding:18px; }}
  .pill {{ display:inline-flex; align-items:center; gap:7px; font-weight:600; font-size:13px; }}
  .pdot {{ width:11px; height:11px; border-radius:50%; flex:0 0 auto; }}
  .g{{background:#16a34a}} .a{{background:#f59e0b}} .r{{background:#ef4444}} .x{{background:#94a3b8}}
  .stats {{ display:flex; gap:10px; flex-wrap:wrap; margin-top:14px; }}
  .stat {{ flex:1 1 120px; background:#f8fafc; border:1px solid #e5e9f0; border-radius:12px; padding:12px 14px; }}
  .stat .n {{ font-size:22px; font-weight:800; color:#0f172a; }}
  .stat .l {{ font-size:12px; color:#64748b; }}
  .kv {{ display:flex; justify-content:space-between; gap:12px; padding:9px 0; border-bottom:1px solid #eef2f6; font-size:14px; }}
  .kv:last-child {{ border-bottom:0; }}
  .kv span {{ color:#64748b; }}
  .kv b {{ color:#0f172a; }}
  .note {{ margin-top:10px; color:#475569; font-size:14px; }}
  #msg {{ margin-top:12px; }}
  .ok {{ background:#eaf7ee; border:1px solid #16a34a; color:#166534; padding:12px; border-radius:10px; font-size:14px; }}
  .err {{ background:#fef2f2; border:1px solid #fecaca; color:#b91c1c; padding:12px; border-radius:10px; font-size:14px; }}
</style></head>
<body><div class=""wrap"">

  <div class=""card head"">
    <div class=""brand"">
      <div class=""logo""></div>
      <div>
        <h1>KPI Report — Vacuum Control</h1>
        <div class=""sub"">{H(m.TenantName)} · Housekeeping · generated {H(m.GeneratedAt.ToString("g"))}</div>
      </div>
    </div>
    <button class=""btn"" id=""gen"">📄 Generate daily report</button>
  </div>
  <div id=""msg""></div>

  <div class=""card"">
    <h2>Daily report</h2>
    <table>
      <thead><tr><th>Employee</th><th>Vacuum</th><th>Status</th><th>Notes</th></tr></thead>
      <tbody>{rows}</tbody>
    </table>
    <div class=""stats"">
      <div class=""stat""><div class=""n"">{m.DOperational}</div><div class=""l"">Operational</div></div>
      <div class=""stat""><div class=""n"">{m.DFaults}</div><div class=""l"">With faults</div></div>
      <div class=""stat""><div class=""n"">{m.DOutOfService}</div><div class=""l"">Out of service</div></div>
      <div class=""stat""><div class=""n"">{m.DUnavailable}</div><div class=""l"">No vacuum</div></div>
      <div class=""stat""><div class=""n"">{m.DTotal}</div><div class=""l"">Total</div></div>
    </div>
    <div class=""note"">Vacuums with problems: <b>{H(dProblems)}</b></div>
  </div>

  <div class=""card"">
    <h2>Weekly KPIs</h2>
    {Kv("Total check-ins", m.WTotal.ToString())}
    {Kv("% Operational", m.WOpPct + "%")}
    {Kv("% With faults", m.WFaultsPct + "%")}
    {Kv("% Out of service", m.WOosPct + "%")}
    {Kv("Repairs done", m.WRepairs.ToString())}
    {Kv("Avg repair time", m.WAvgRepairDays + " days")}
    {Kv("Most faults", wTop)}
  </div>

  <div class=""card"">
    <h2>Monthly KPIs (executive)</h2>
    {Kv("Total check-ins", m.MTotal.ToString())}
    {Kv("Avg availability", m.MAvailabilityPct + "%")}
    {Kv("Out of service", m.MOos.ToString())}
    {Kv("Repairs done", m.MRepairs.ToString())}
    {Kv("Avg out-of-service", m.MAvgOosDays + " days")}
    {Kv("Most faults", mTop)}
    <div class=""note""><b>Recommendation:</b> {H(m.MRecommendation)}</div>
  </div>

</div>
<script>
  const g = document.getElementById('gen');
  const msg = document.getElementById('msg');
  g.addEventListener('click', async () => {{
    g.disabled = true; const t = g.textContent; g.textContent = 'Generating…'; msg.innerHTML='';
    try {{
      const r = await fetch('/reports/' + encodeURIComponent({slugJs}) + '/generate', {{ method:'POST' }});
      const j = await r.json().catch(()=>({{}}));
      if (r.ok) msg.innerHTML = '<div class=""ok"">✅ Daily report generated and emailed'+(j.to?(' to '+j.to):'')+' (PDF attached).</div>';
      else msg.innerHTML = '<div class=""err"">Could not generate ('+r.status+'). '+(j.error||'')+'</div>';
    }} catch (e) {{ msg.innerHTML = '<div class=""err"">Network error: '+e+'</div>'; }}
    g.disabled = false; g.textContent = t;
  }});
</script>
</body></html>");
        return sb.ToString();
    }

    private static string StatusPill(string key, string label)
    {
        var cls = key switch { "operational" => "g", "AMedias" => "a", "NoFunciona" => "r", _ => "x" };
        return $@"<span class=""pill""><span class=""pdot {cls}""></span>{H(label)}</span>";
    }
}
