using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Valentinos.Api.Kpi;

public static class KpiHtml
{
    private static string H(string s) => WebUtility.HtmlEncode(s);
    private static string F(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Render(PeriodKpi m, string slug, IReadOnlyList<KpiSiteOption> sites)
    {
        var slugJs = JsonSerializer.Serialize(slug);

        string Sel(string v, string en) =>
            $@"<option value=""{v}""{(m.Period == v ? " selected" : "")} data-i18n=""p_{v}"">{en}</option>";

        // Opciones del dropdown de sites; el actual queda seleccionado.
        var siteOpts = new StringBuilder();
        foreach (var s in sites)
            siteOpts.Append($@"<option value=""{H(s.Slug)}""{(s.Slug == slug ? " selected" : "")}>Site {H(s.Code)}</option>");

        // Encabezado de la columna temporal: hora en diario, fecha en semanal/mensual.
        var whenKey = m.Period == "daily" ? "thTime" : "thDate";
        var whenEn = m.Period == "daily" ? "Time" : "Date";

        // Filas de detalle.
        var rows = new StringBuilder();
        if (m.Rows.Count == 0)
            rows.Append(@"<tr><td colspan=""5"" class=""empty"" data-i18n=""empty"">No records for this period.</td></tr>");
        foreach (var r in m.Rows)
            rows.Append($@"<tr><td>{H(r.Employee)}</td><td class=""mono"">{H(r.Vacuum)}</td>
              <td class=""when"">{H(r.When)}</td><td>{Pill(r.EstadoKey)}</td><td class=""notes"">{H(r.Notes)}</td></tr>");

        // Donut.
        int t = Math.Max(1, m.Total);
        double e1 = 100.0 * m.Op / t, e2 = e1 + 100.0 * m.Fa / t, e3 = e2 + 100.0 * m.Oos / t;
        var donut = $"conic-gradient(#16a34a 0 {F(e1)}%, #f59e0b {F(e1)}% {F(e2)}%, #ef4444 {F(e2)}% {F(e3)}%, #94a3b8 {F(e3)}% 100%)";
        string Leg(string c, string key, string en, int n) =>
            $@"<div class=""lg""><span class=""ld"" style=""background:{c}""></span><span data-i18n=""{key}"">{en}</span><b>{n}</b></div>";

        // Tendencia (semanal/mensual).
        var trendCard = "";
        if (m.ShowTrend && m.Trend.Count > 0)
        {
            var maxT = Math.Max(1, m.Trend.Max(d => d.Total));
            var showLbl = m.Trend.Count <= 10;
            var bars = new StringBuilder();
            foreach (var d in m.Trend)
            {
                var hp = 100.0 * d.Total / maxT;
                var pp = d.Total == 0 ? 0 : 100.0 * d.Problems / d.Total;
                bars.Append($@"<div class=""col"" title=""{d.Total} / {d.Problems} problems""><div class=""bar"" style=""height:{F(hp)}%""><i style=""height:{F(pp)}%""></i></div>{(showLbl ? $"<span class=\"bl\">{H(d.Label)}</span>" : "")}</div>");
            }
            trendCard = $@"<div class=""card"">
    <h2 data-i18n=""trendTitle"">Check-ins per day</h2>
    <div class=""bars"">{bars}</div>
    <div class=""note""><span class=""pill""><span class=""pdot"" style=""background:#1560A8""></span><span data-i18n=""lTot"">Total</span></span>
      &nbsp;&nbsp;<span class=""pill""><span class=""pdot r""></span><span data-i18n=""lProb"">Problems</span></span></div>
  </div>";
        }

        // Top con más fallas.
        var maxF = Math.Max(1, m.TopFaulty.Count == 0 ? 1 : m.TopFaulty.Max(a => a.Count));
        var top = new StringBuilder();
        if (m.TopFaulty.Count == 0) top.Append(@"<div class=""muted"" data-i18n=""noFaults"">No problems in this period 🎉</div>");
        foreach (var a in m.TopFaulty)
            top.Append($@"<div class=""hb""><span class=""hbl"">{H(a.Vacuum)}</span>
              <div class=""hbt""><div class=""hbf"" style=""width:{F(100.0 * a.Count / maxF)}%""></div></div><b>{a.Count}</b></div>");

        string Kv(string key, string en, string v) =>
            $@"<div class=""kv""><span data-i18n=""{key}"">{en}</span><b>{H(v)}</b></div>";
        var probs = m.ProblemAssets.Count > 0 ? string.Join(", ", m.ProblemAssets) : "None";

        // Reportes de "equipo no disponible" (QR fijo del cuarto de housekeeping).
        var unav = new StringBuilder();
        if (m.Unavailable.Count == 0)
            unav.Append(@"<tr><td colspan=""3"" class=""empty"" data-i18n=""unEmpty"">No unavailable-equipment reports for this period.</td></tr>");
        foreach (var u in m.Unavailable)
            unav.Append($@"<tr><td class=""when"">{H(u.When)}</td><td>{H(u.Employee)}</td><td class=""notes"">{H(u.Note)}</td></tr>");

        return $@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>KPI Report · {H(m.TenantName)}</title>
<link rel=""icon"" href=""/favicon.ico"" sizes=""any"">
<link rel=""icon"" type=""image/png"" href=""/favicon-32.png"">
<link rel=""apple-touch-icon"" href=""/favicon-180.png"">
<style>
  :root {{ color-scheme: light; }} * {{ box-sizing:border-box; }}
  body {{ margin:0; background:#eef1f5; color:#334155; font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif; -webkit-text-size-adjust:100%; }}
  .wrap {{ max-width:760px; margin:0 auto; padding:16px 14px 60px; }}
  .card {{ background:#fff; border:1px solid #e5e9f0; border-radius:16px; padding:18px; box-shadow:0 8px 24px rgba(15,23,42,.05); margin-bottom:14px; }}
  .head {{ display:flex; align-items:center; gap:12px; }}
  .logo {{ width:46px; height:46px; border-radius:12px; flex:0 0 auto; background:#fff url(/images/brand/mastercorp-logo.png) no-repeat 50%/contain; border:1px solid #e5e9f0; }}
  .head .t {{ flex:1 1 auto; min-width:0; }}
  h1 {{ font-size:18px; margin:0; color:#2b3440; }}
  h2 {{ font-size:15px; margin:0 0 12px; color:#1560A8; }}
  .sub {{ color:#64748b; font-size:12px; margin-top:2px; }}
  .langs {{ display:flex; gap:6px; flex:0 0 auto; }}
  .flag {{ padding:0; background:transparent; border:0; cursor:pointer; border-radius:999px; line-height:0; opacity:.45; transition:opacity .15s,box-shadow .15s; }}
  .flag.active {{ opacity:1; box-shadow:0 0 0 2px #1560A8; }}
  .flag img {{ display:block; width:24px; height:24px; border-radius:999px; }}
  .controls {{ display:flex; gap:10px; margin-top:14px; flex-wrap:wrap; }}
  select {{ flex:1 1 140px; padding:12px; border-radius:10px; border:1px solid #cbd5e1; background:#fff; color:#1f2937; font-size:15px; font-weight:600; }}
  .btn {{ flex:2 1 180px; background:#1560A8; color:#fff; border:0; border-radius:10px; padding:12px 16px; font-weight:700; font-size:14px; cursor:pointer; }}
  .btn:hover {{ background:#0f4c85; }} .btn:disabled {{ opacity:.6; cursor:progress; }}
  .tblshell {{ position:relative; }}
  .tblshell::after {{ content:''; position:absolute; top:0; right:0; bottom:0; width:44px; pointer-events:none;
    background:linear-gradient(to right, rgba(255,255,255,0), #fff 88%); opacity:1; transition:opacity .2s; }}
  .tblshell.at-end::after {{ opacity:0; }}
  .tblwrap {{ overflow-x:auto; -webkit-overflow-scrolling:touch; }}
  table {{ width:100%; border-collapse:collapse; font-size:13.5px; min-width:460px; }}
  th,td {{ text-align:left; padding:9px 8px; border-bottom:1px solid #eef2f6; vertical-align:top; white-space:nowrap; }}
  th {{ color:#64748b; font-size:11px; text-transform:uppercase; letter-spacing:.3px; }}
  td.mono {{ font-family:ui-monospace,monospace; font-weight:600; color:#0f172a; }}
  td.when {{ color:#64748b; white-space:nowrap; }}
  td.notes {{ min-width:270px; max-width:380px; white-space:normal; overflow-wrap:break-word; }}
  td.empty {{ color:#94a3b8; text-align:center; padding:16px; white-space:normal; }}
  .pill {{ display:inline-flex; align-items:center; gap:6px; font-weight:600; font-size:13px; white-space:nowrap; }}
  .pdot {{ width:10px; height:10px; border-radius:50%; flex:0 0 auto; }}
  .g{{background:#16a34a}} .a{{background:#f59e0b}} .r{{background:#ef4444}} .x{{background:#94a3b8}}
  .stats {{ display:grid; grid-template-columns:repeat(auto-fit,minmax(92px,1fr)); gap:8px; margin-top:14px; }}
  .stat {{ background:#f8fafc; border:1px solid #e5e9f0; border-radius:12px; padding:10px 12px; }}
  .stat .n {{ font-size:20px; font-weight:800; color:#0f172a; }} .stat .l {{ font-size:11px; color:#64748b; }}
  .note {{ margin-top:10px; color:#475569; font-size:13.5px; }} .muted {{ color:#94a3b8; font-size:13px; }}
  .donutbox {{ display:flex; align-items:center; gap:18px; flex-wrap:wrap; }}
  .donut {{ width:130px; height:130px; border-radius:50%; flex:0 0 auto; -webkit-mask:radial-gradient(circle 42px at center,transparent 98%,#000 100%); mask:radial-gradient(circle 42px at center,transparent 98%,#000 100%); }}
  .legend {{ flex:1 1 160px; }}
  .lg {{ display:flex; align-items:center; gap:8px; font-size:14px; padding:6px 0; }} .lg .ld {{ width:12px; height:12px; border-radius:3px; }} .lg b {{ margin-left:auto; color:#0f172a; }}
  .bars {{ display:flex; align-items:flex-end; gap:5px; height:130px; margin-top:6px; }}
  .col {{ flex:1; display:flex; flex-direction:column; align-items:center; justify-content:flex-end; height:100%; }}
  .bar {{ width:80%; max-width:34px; background:#1560A8; border-radius:5px 5px 0 0; position:relative; min-height:3px; }}
  .bar i {{ position:absolute; left:0; right:0; bottom:0; background:#ef4444; }}
  .bl {{ font-size:10px; color:#64748b; margin-top:5px; }}
  .hb {{ display:flex; align-items:center; gap:10px; padding:6px 0; font-size:14px; }}
  .hbl {{ width:74px; font-family:ui-monospace,monospace; font-weight:600; color:#0f172a; flex:0 0 auto; }}
  .hbt {{ flex:1; background:#eef2f6; border-radius:999px; height:12px; overflow:hidden; }}
  .hbf {{ height:100%; background:#ef4444; border-radius:999px; }} .hb b {{ width:24px; text-align:right; color:#0f172a; }}
  .kv {{ display:flex; justify-content:space-between; gap:12px; padding:9px 0; border-bottom:1px solid #eef2f6; font-size:14px; }}
  .kv:last-child {{ border-bottom:0; }} .kv span {{ color:#64748b; }} .kv b {{ color:#0f172a; }}
  #msg {{ margin-top:12px; }}
  .ok {{ background:#eaf7ee; border:1px solid #16a34a; color:#166534; padding:12px; border-radius:10px; font-size:14px; }}
  .err {{ background:#fef2f2; border:1px solid #fecaca; color:#b91c1c; padding:12px; border-radius:10px; font-size:14px; }}
  /* Modal de confirmación de envío. */
  .modal-bg {{ position:fixed; inset:0; background:rgba(15,23,42,.55); display:none; align-items:center; justify-content:center; z-index:100; padding:18px; }}
  .modal-bg.open {{ display:flex; }}
  .modal {{ background:#fff; border-radius:16px; padding:22px; max-width:400px; width:100%; box-shadow:0 24px 60px rgba(15,23,42,.35); animation:mpop .2s ease-out; }}
  @keyframes mpop {{ from {{ transform:translateY(8px) scale(.98); opacity:0; }} to {{ transform:none; opacity:1; }} }}
  .modal .mico {{ width:46px; height:46px; border-radius:12px; background:#eaf1f9; color:#1560A8; display:flex; align-items:center; justify-content:center; margin-bottom:12px; }}
  .modal .mico svg {{ width:24px; height:24px; }}
  .modal h3 {{ margin:0 0 6px; font-size:18px; color:#0f172a; }}
  .modal p {{ margin:0 0 18px; color:#475569; font-size:14px; line-height:1.5; }}
  .modal p b {{ color:#0f172a; }}
  .modal-actions {{ display:flex; gap:10px; justify-content:flex-end; }}
  .mbtn {{ padding:11px 18px; border-radius:10px; font-weight:700; font-size:14px; cursor:pointer; border:0; }}
  .mbtn.ghost {{ background:#f1f5f9; color:#334155; }} .mbtn.ghost:hover {{ background:#e2e8f0; }}
  .mbtn.primary {{ background:#1560A8; color:#fff; }} .mbtn.primary:hover {{ background:#0f4c85; }}
</style></head>
<body><div class=""wrap"">

  <div class=""card"">
    <div class=""head"">
      <div class=""logo""></div>
      <div class=""t""><h1 data-i18n=""title"">KPI Report — Vacuum Control</h1>
        <div class=""sub"">{H(m.TenantName)} · Housekeeping · {H(m.GeneratedAt.ToString("MMM d, yyyy · h:mm tt", System.Globalization.CultureInfo.InvariantCulture))}</div></div>
      <div class=""langs"">
        <button type=""button"" class=""flag"" id=""flag-en"" onclick=""setLang('en')""><img src=""/images/flags/us-circle.svg"" alt=""EN""></button>
        <button type=""button"" class=""flag"" id=""flag-es"" onclick=""setLang('es')""><img src=""/images/flags/es-circle.svg"" alt=""ES""></button>
      </div>
    </div>
    <div class=""controls"">
      <select id=""site"" onchange=""location.href='/reports?site='+this.value+'&period={H(m.Period)}'"">
        {siteOpts}
      </select>
      <select id=""period"" onchange=""location.href='/reports?site={H(slug)}&period='+this.value"">
        {Sel("daily", "Daily")}{Sel("weekly", "Weekly")}{Sel("monthly", "Monthly")}
      </select>
      <a class=""btn"" href=""/reports/pdf?site={H(slug)}&period={H(m.Period)}"" target=""_blank"" data-i18n=""dlpdf"">📄 Download PDF</a>
    </div>
    <div id=""msg""></div>
  </div>


  <div class=""card"">
    <h2 data-i18n=""distTitle"">Status distribution</h2>
    <div class=""donutbox"">
      <div class=""donut"" style=""background:{donut}""></div>
      <div class=""legend"">
        {Leg("#16a34a", "stOp", "Operational", m.Op)}
        {Leg("#f59e0b", "stFa", "Working with faults", m.Fa)}
        {Leg("#ef4444", "stOos", "Out of service", m.Oos)}
        {Leg("#94a3b8", "stUn", "No vacuum available", m.Un)}
      </div>
    </div>
  </div>
  {trendCard}
  <div class=""card""><h2 data-i18n=""topTitle"">Most reported</h2>{top}</div>

  <div class=""card"">
    <h2 data-i18n=""detailTitle"">Detail</h2>
    <div class=""tblshell""><div class=""tblwrap""><table>
      <thead><tr><th data-i18n=""thEmp"">Employee</th><th data-i18n=""thVac"">Vacuum</th><th data-i18n=""{whenKey}"">{whenEn}</th><th data-i18n=""thSt"">Status</th><th data-i18n=""thNo"">Notes</th></tr></thead>
      <tbody>{rows}</tbody>
    </table></div></div>
    <div class=""stats"">
      <div class=""stat""><div class=""n"">{m.Op}</div><div class=""l"" data-i18n=""stOp"">Operational</div></div>
      <div class=""stat""><div class=""n"">{m.Fa}</div><div class=""l"" data-i18n=""lFa"">With faults</div></div>
      <div class=""stat""><div class=""n"">{m.Oos}</div><div class=""l"" data-i18n=""stOos"">Out of service</div></div>
      <div class=""stat""><div class=""n"">{m.Un}</div><div class=""l"" data-i18n=""lNo"">No vacuum</div></div>
      <div class=""stat""><div class=""n"">{m.Total}</div><div class=""l"" data-i18n=""lTot"">Total</div></div>
    </div>
    <div class=""note""><span data-i18n=""probLbl"">Vacuums with problems:</span> <b>{H(probs)}</b></div>
  </div>

  <div class=""card"">
    <h2 data-i18n=""unTitle"">Unavailable equipment</h2>
    <div class=""tblshell""><div class=""tblwrap""><table>
      <thead><tr><th data-i18n=""{whenKey}"">{whenEn}</th><th data-i18n=""thEmp"">Employee</th><th data-i18n=""thNo"">Notes</th></tr></thead>
      <tbody>{unav}</tbody>
    </table></div></div>
    <div class=""note""><b>{m.Un}</b> <span data-i18n=""unCount"">reports in this period</span></div>
  </div>

  <div class=""card"">
    <h2 data-i18n=""kpiTitle"">KPIs</h2>
    {Kv("kAvail", "Availability", m.AvailabilityPct + "%")}
    {Kv("kFa", "% With faults", m.FaPct + "%")}
    {Kv("kOos", "% Out of service", m.OosPct + "%")}
    <div class=""note""><b data-i18n=""recLbl"">Recommendation:</b> <span>{H(m.Recommendation)}</span></div>
  </div>

</div>
<script>
  const I18N = {{
    en: {{ title:'KPI Report — Vacuum Control', dlpdf:'📄 Download PDF', send:'📄 Send report', sending:'Sending…',
      p_daily:'Daily', p_weekly:'Weekly', p_monthly:'Monthly',
      distTitle:'Status distribution', trendTitle:'Check-ins per day', topTitle:'Most reported',
      detailTitle:'Detail', kpiTitle:'KPIs', empty:'No records for this period.', noFaults:'No problems in this period 🎉',
      stOp:'Operational', stFa:'Working with faults', stOos:'Out of service', stUn:'No vacuum available',
      lFa:'With faults', lNo:'No vacuum', lTot:'Total', lProb:'Problems',
      thEmp:'Employee', thVac:'Vacuum', thDate:'Date', thTime:'Time', thSt:'Status', thNo:'Notes', probLbl:'Vacuums with problems:',
      kAvail:'Availability', kFa:'% With faults', kOos:'% Out of service', recLbl:'Recommendation:',
      unTitle:'Unavailable equipment', unEmpty:'No unavailable-equipment reports for this period.', unCount:'reports in this period',
      confirmTitle:'Send report?', confirmMsg:""A PDF will be generated and emailed to the report's recipients. Do you want to continue?"", confirmCancel:'Cancel', confirmSend:'Send' }},
    es: {{ title:'Reporte de KPIs — Aspiradoras', dlpdf:'📄 Descargar PDF', send:'📄 Enviar reporte', sending:'Enviando…',
      p_daily:'Diario', p_weekly:'Semanal', p_monthly:'Mensual',
      distTitle:'Distribución de estado', trendTitle:'Check-ins por día', topTitle:'Más reportadas',
      detailTitle:'Detalle', kpiTitle:'KPIs', empty:'Sin registros en este periodo.', noFaults:'Sin problemas en este periodo 🎉',
      stOp:'Operativa', stFa:'Funciona con fallas', stOos:'Fuera de servicio', stUn:'Sin aspiradora',
      lFa:'Con fallas', lNo:'Sin aspiradora', lTot:'Total', lProb:'Problemas',
      thEmp:'Empleado', thVac:'Aspiradora', thDate:'Fecha', thTime:'Hora', thSt:'Estado', thNo:'Notas', probLbl:'Aspiradoras con problemas:',
      kAvail:'Disponibilidad', kFa:'% Con fallas', kOos:'% Fuera de servicio', recLbl:'Recomendación:',
      unTitle:'Equipo no disponible', unEmpty:'Sin reportes de equipo no disponible en este periodo.', unCount:'reportes en este periodo',
      confirmTitle:'¿Enviar reporte?', confirmMsg:'Se generará un PDF y se enviará por correo a los destinatarios del reporte. ¿Deseas continuar?', confirmCancel:'Cancelar', confirmSend:'Enviar' }}
  }};
  const ST = {{ operational:'stOp', AMedias:'stFa', NoFunciona:'stOos', unavailable:'stUn' }};
  const PERIOD = {JsonSerializer.Serialize(m.Period)};
  let LANG = 'en';
  function setLang(l) {{
    LANG = I18N[l] ? l : 'en'; const d = I18N[LANG];
    document.querySelectorAll('[data-i18n]').forEach(e => {{ const k=e.getAttribute('data-i18n'); if (d[k]) e.textContent=d[k]; }});
    document.querySelectorAll('[data-st]').forEach(e => {{ const k=ST[e.getAttribute('data-st')]; if (k&&d[k]) e.textContent=d[k]; }});
    document.getElementById('flag-en').classList.toggle('active', LANG==='en');
    document.getElementById('flag-es').classList.toggle('active', LANG==='es');
    document.documentElement.lang = LANG;
    try {{ localStorage.setItem('lang', LANG); }} catch(e) {{}}
  }}
  // Fade a la derecha de la tabla: se oculta cuando el scroll llega al final.
  document.querySelectorAll('.tblshell').forEach(function (sh) {{
    const w = sh.querySelector('.tblwrap'); if (!w) return;
    function upd() {{
      const atEnd = w.scrollLeft + w.clientWidth >= w.scrollWidth - 2;
      const scrollable = w.scrollWidth > w.clientWidth + 2;
      sh.classList.toggle('at-end', atEnd || !scrollable);
    }}
    w.addEventListener('scroll', upd, {{ passive:true }});
    window.addEventListener('resize', upd);
    upd();
  }});
  setLang('en'); // default inglés al cargar
</script>
</body></html>";
    }

    // Email profesional (sin imágenes) del reporte generado, con el periodo seleccionado.
    public static string RenderReportEmail(PeriodKpi m)
    {
        string Row(string label, string value, string color = "#0f172a") =>
$@"<tr><td style=""padding:9px 0;border-bottom:1px solid #e5e9f0;color:#64748b;font-size:13px;"">{H(label)}</td>
   <td style=""padding:9px 0;border-bottom:1px solid #e5e9f0;color:{color};font-size:14px;font-weight:700;text-align:right;"">{H(value)}</td></tr>";
        var problems = m.ProblemAssets.Count > 0 ? string.Join(", ", m.ProblemAssets) : "None";
        var pName = m.Period switch { "weekly" => "Weekly", "monthly" => "Monthly", _ => "Daily" };

        return
$@"<!doctype html><html><body style=""margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#eef2f7;padding:24px 12px;""><tr><td align=""center"">
    <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 6px 24px rgba(15,23,42,.08);"">
      <tr><td style=""background:#1560A8;padding:22px 28px;"">
        <div style=""color:#fff;font-size:12px;letter-spacing:1px;text-transform:uppercase;opacity:.85;"">ValentiSoft</div>
        <div style=""color:#fff;font-size:20px;font-weight:800;margin-top:2px;"">{H(pName)} report generated</div>
      </td></tr>
      <tr><td style=""padding:24px 28px 6px;"">
        <p style=""margin:0 0 4px;color:#0f172a;font-size:15px;"">The {H(pName.ToLowerInvariant())} KPI report for <b>{H(m.TenantName)}</b> is ready ({H(m.PeriodLabel)}).</p>
        <p style=""margin:0;color:#64748b;font-size:13px;"">{H(m.GeneratedAt.ToString("dddd, MMM d yyyy · h:mm tt"))}</p>
      </td></tr>
      <tr><td style=""padding:12px 28px 6px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">
          {Row("Operational", m.Op.ToString(), "#16a34a")}
          {Row("Working with faults", m.Fa.ToString(), "#d97706")}
          {Row("Out of service", m.Oos.ToString(), "#dc2626")}
          {Row("No vacuum available", m.Un.ToString(), "#64748b")}
          {Row("Total check-ins", m.Total.ToString())}
      </table></td></tr>
      <tr><td style=""padding:6px 28px 22px;"">
        <div style=""background:#f8fafc;border:1px solid #e5e9f0;border-radius:10px;padding:12px 14px;color:#0f172a;font-size:13px;"">Vacuums with problems: <b>{H(problems)}</b></div>
        <p style=""margin:16px 0 0;color:#475569;font-size:14px;"">📎 The full report is attached as a PDF.</p>
      </td></tr>
      <tr><td style=""background:#f8fafc;padding:16px 28px;border-top:1px solid #e5e9f0;"">
        <p style=""margin:0;color:#94a3b8;font-size:12px;"">Generated automatically by ValentiSoft platform. Please do not reply to this email.</p>
      </td></tr>
    </table>
  </td></tr></table>
</body></html>";
    }

    private static string Pill(string key)
    {
        var cls = key switch { "operational" => "g", "AMedias" => "a", "NoFunciona" => "r", _ => "x" };
        var en = key switch { "operational" => "Operational", "AMedias" => "Working with faults", "NoFunciona" => "Out of service", _ => "No vacuum available" };
        return $@"<span class=""pill""><span class=""pdot {cls}""></span><span data-st=""{key}"">{en}</span></span>";
    }
}
