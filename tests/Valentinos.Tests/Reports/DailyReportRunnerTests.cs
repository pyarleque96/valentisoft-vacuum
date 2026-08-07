using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Valentinos.Api.Reports;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Reports;

// Ningún test de este archivo abre una conexión SMTP.
public class DailyReportRunnerTests
{
    private sealed record Sent(IReadOnlyList<string> To, string Subject, bool IncludeCc);

    private sealed class RecordingEmailSender : IEmailSender
    {
        // Si el primer destinatario contiene este texto, el envío explota (para
        // probar que un site que falla no tumba a los demás ni al consolidado).
        public string? FallaSiContiene { get; set; }
        public List<Sent> Sends { get; } = new();

        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
            EmailAttachment? attachment = null, bool includeConfiguredCc = true, CancellationToken ct = default)
        {
            if (FallaSiContiene is not null && to.Any(t => t.Contains(FallaSiContiene)))
                throw new InvalidOperationException("SMTP caído");
            Sends.Add(new Sent(to.ToList(), subject, includeConfiguredCc));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    // Tenant con: un site con destinatarios y actividad, y otro SIN destinatarios.
    private static AppDbContext NewScenario(string? notificationEmails = "christopher.davey@mastercorp.com")
    {
        var ctx = new FakeTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new AppDbContext(options, ctx);

        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.NotificationEmails = notificationEmails;
        db.Tenants.Add(tenant);
        db.SaveChanges();

        ctx.Set(tenant.Id);
        var s069 = new Site { Code = "069", Slug = "XjUS3", Emails = "ramces.rodriguez@mastercorp.com" };
        var sinEmails = new Site { Code = "003", Slug = "Ra9Zt", Emails = null };
        db.Sites.AddRange(s069, sinEmails);
        db.SaveChanges();

        var hoy = DateTime.Today.AddHours(9);
        db.StatusCheckins.AddRange(
            new StatusCheckin { SiteId = s069.Id, EmployeeName = "Ana", AssetCodigo = "VAC-001", EstadoKey = "operational", CreatedAt = hoy },
            new StatusCheckin { SiteId = sinEmails.Id, EmployeeName = "Beto", AssetCodigo = "VAC-001", EstadoKey = "operational", CreatedAt = hoy });
        db.SaveChanges();

        return db;
    }

    private static DailyReportRunner NewRunner(AppDbContext db, IEmailSender sender)
        => new(db, new ReportEmailer(db, sender, NullLogger<ReportEmailer>.Instance),
               NullLogger<DailyReportRunner>.Instance);

    [Fact]
    public async Task RunAsync_EnviaUnCorreoPorSiteConEmails_MasElConsolidado()
    {
        var db = NewScenario();
        var sender = new RecordingEmailSender();

        await NewRunner(db, sender).RunAsync();

        Assert.Equal(2, sender.Sends.Count);

        var porSite = sender.Sends.Single(s => s.To.Contains("ramces.rodriguez@mastercorp.com"));
        Assert.False(porSite.IncludeCc);
        Assert.Contains("Site 069", porSite.Subject);

        var consolidado = sender.Sends.Single(s => s.To.Contains("christopher.davey@mastercorp.com"));
        Assert.True(consolidado.IncludeCc);
        Assert.DoesNotContain("Site", consolidado.Subject);
    }

    [Fact]
    public async Task RunAsync_SiFallaElCorreoDeUnSite_IgualEnviaElConsolidado()
    {
        var db = NewScenario();
        var sender = new RecordingEmailSender { FallaSiContiene = "ramces" };

        await NewRunner(db, sender).RunAsync();

        var consolidado = Assert.Single(sender.Sends);
        Assert.Contains("christopher.davey@mastercorp.com", consolidado.To);
    }

    [Fact]
    public async Task RunAsync_TenantSinNotificationEmails_EnviaSoloLosCorreosPorSite()
    {
        var db = NewScenario(notificationEmails: null);
        var sender = new RecordingEmailSender();

        await NewRunner(db, sender).RunAsync();

        var unico = Assert.Single(sender.Sends);
        Assert.Contains("ramces.rodriguez@mastercorp.com", unico.To);
        Assert.False(unico.IncludeCc);
        Assert.DoesNotContain(sender.Sends, s => s.To.Contains("christopher.davey@mastercorp.com"));
    }
}
