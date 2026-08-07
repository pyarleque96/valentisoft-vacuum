using Valentinos.Api.Kpi;
using Xunit;

namespace Valentinos.Tests.Reports;

// La vista pública de un site no debe exponer NADA de los demás sites: ni el selector,
// ni sus slugs, ni sus códigos. Estas pruebas son sobre el HTML renderizado, que es lo
// que realmente ve quien abre la página.
public class KpiHtmlRenderTests
{
    private static PeriodKpi Modelo(string siteCode = "127 HCC", string period = "daily")
        => Valentinos.Api.Kpi.Kpi.Compute($"MasterCorp · Site {siteCode}",
            System.Array.Empty<KpiCheckin>(), System.Array.Empty<KpiUnavailable>(),
            System.DateTime.Now, period);

    [Fact]
    public void Render_SinSites_NoEmiteElSelectorNiDatosDeOtrosSites()
    {
        var html = KpiHtml.Render(Modelo(), "Kp7Qm", System.Array.Empty<KpiSiteOption>());

        Assert.DoesNotContain("id=\"site\"", html);
        Assert.DoesNotContain("XjUS3", html);
        Assert.DoesNotContain("Site 069", html);
    }

    [Fact]
    public void Render_SinSites_ApuntaALasRutasPublicasDelSite()
    {
        var html = KpiHtml.Render(Modelo(), "Kp7Qm", System.Array.Empty<KpiSiteOption>());

        Assert.Contains("/Kp7Qm/reports/pdf", html);
        Assert.Contains("/Kp7Qm/reports?period=", html);
        Assert.DoesNotContain("/reports/pdf?site=", html);
        Assert.DoesNotContain("/reports?site=", html);
    }

    [Fact]
    public void Render_ConSites_EmiteElSelectorYLasRutasPrivadas()
    {
        var sites = new[]
        {
            new KpiSiteOption("Kp7Qm", "127 HCC"),
            new KpiSiteOption("XjUS3", "069"),
        };

        var html = KpiHtml.Render(Modelo(), "Kp7Qm", sites);

        Assert.Contains("id=\"site\"", html);
        Assert.Contains("XjUS3", html);
        Assert.Contains("/reports/pdf?site=Kp7Qm", html);
    }

    [Fact]
    public void Render_SinSites_ConservaPeriodoPdfYToggleDeIdioma()
    {
        var html = KpiHtml.Render(Modelo(), "Kp7Qm", System.Array.Empty<KpiSiteOption>());

        Assert.Contains("id=\"period\"", html);      // selector de periodo
        Assert.Contains("data-i18n=\"dlpdf\"", html); // botón de PDF
        Assert.Contains("setLang('es')", html);       // toggle ES/EN
    }
}
