using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Valentinos.Api.Reports;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Reports;

// Ningún test de este archivo abre una conexión SMTP: el IEmailSender es un doble
// que solo registra en memoria lo que se le pidió enviar.
public class ReportEmailerTests
{
    private sealed record Sent(IReadOnlyList<string> To, string Subject, string Body, bool IncludeCc);

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<Sent> Sends { get; } = new();
        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
            EmailAttachment? attachment = null, bool includeConfiguredCc = true, CancellationToken ct = default)
        {
            Sends.Add(new Sent(to.ToList(), subject, body, includeConfiguredCc));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    // Escenario común: tenant con dos sites, cada uno con un check-in de HOY hecho
    // por un empleado de nombre distinto (para poder detectar fugas de data entre sites).
    private static (AppDbContext db, Tenant tenant, Site s069, Site s127) NewScenario(
        bool conActividadEn127 = true)
    {
        var ctx = new FakeTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, ctx);

        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.NotificationEmails = "christopher.davey@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        ctx.Set(tenant.Id);
        var s069 = new Site { Code = "069", Slug = "XjUS3", Emails = "ramces.rodriguez@mastercorp.com" };
        var s127 = new Site
        {
            Code = "127 HCC",
            Slug = "Kp7Qm",
            Emails = "learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com"
        };
        db.Sites.AddRange(s069, s127);
        db.SaveChanges();

        // Hora fija de hoy: el periodo "daily" filtra por fecha, no por hora.
        var hoy = DateTime.Today.AddHours(9);
        db.StatusCheckins.Add(new StatusCheckin
        {
            SiteId = s069.Id, EmployeeName = "EmpleadoDel069", AssetCodigo = "VAC-SOLO069",
            EstadoKey = "NoFunciona", CreatedAt = hoy
        });
        if (conActividadEn127)
            db.StatusCheckins.Add(new StatusCheckin
            {
                SiteId = s127.Id, EmployeeName = "EmpleadoDel127", AssetCodigo = "VAC-SOLO127",
                EstadoKey = "NoFunciona", CreatedAt = hoy
            });
        db.SaveChanges();

        return (db, tenant, s069, s127);
    }

    private static ReportEmailer NewEmailer(AppDbContext db, IEmailSender sender)
        => new(db, sender, NullLogger<ReportEmailer>.Instance);

    [Fact]
    public async Task SendSiteAsync_EnviaSoloALosEmailsDelSite_YSinCc()
    {
        var (db, tenant, _, s127) = NewScenario();
        var sender = new RecordingEmailSender();

        var to = await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        var sent = Assert.Single(sender.Sends);
        Assert.Equal(new[]
        {
            "learsy.betancourt@mastercorp.com",
            "carlos.reyes@mastercorp.com",
            "gilberto.espinoza@mastercorp.com"
        }, sent.To);
        Assert.False(sent.IncludeCc);
        Assert.Contains("Site 127 HCC", sent.Subject);
        Assert.Equal(sent.To, to);
    }

    [Fact]
    public async Task SendSiteAsync_NoIncluyeDataDeOtroSite()
    {
        var (db, tenant, _, s127) = NewScenario();
        var sender = new RecordingEmailSender();

        await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        var sent = Assert.Single(sender.Sends);
        Assert.Contains("VAC-SOLO127", sent.Body);
        Assert.Contains("Site 127 HCC", sent.Body);
        Assert.DoesNotContain("VAC-SOLO069", sent.Body);
        Assert.DoesNotContain("Site 069", sent.Body);
    }

    [Fact]
    public async Task SendSiteAsync_SinActividadEnElPeriodo_NoEnvia()
    {
        var (db, tenant, _, s127) = NewScenario(conActividadEn127: false);
        var sender = new RecordingEmailSender();

        var to = await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        Assert.Empty(sender.Sends);
        Assert.Empty(to);
    }

    [Fact]
    public async Task SendSiteAsync_SiteSinEmails_NoEnvia()
    {
        var (db, tenant, _, s127) = NewScenario();
        s127.Emails = null;
        var sender = new RecordingEmailSender();

        var to = await NewEmailer(db, sender).SendSiteAsync(tenant, s127, "daily");

        Assert.Empty(sender.Sends);
        Assert.Empty(to);
    }

    [Fact]
    public async Task SendAsync_Consolidado_VaAlTenantConCc_YTraeAmbosSites()
    {
        var (db, tenant, _, _) = NewScenario();
        var sender = new RecordingEmailSender();

        await NewEmailer(db, sender).SendAsync(tenant, "daily");

        var sent = Assert.Single(sender.Sends);
        Assert.Equal(new[] { "christopher.davey@mastercorp.com" }, sent.To);
        Assert.True(sent.IncludeCc);
        Assert.Contains("VAC-SOLO069", sent.Body);
        Assert.Contains("VAC-SOLO127", sent.Body);
    }
}
