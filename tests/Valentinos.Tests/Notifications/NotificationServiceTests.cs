using Microsoft.EntityFrameworkCore;
using Valentinos.Application.Abstractions;
using Valentinos.Application.Notifications;
using Valentinos.Domain.Entities;
using Valentinos.Infrastructure.Notifications;
using Valentinos.Infrastructure.Persistence;
using Xunit;

namespace Valentinos.Tests.Notifications;

public class NotificationServiceTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public void Set(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public int Calls;
        public List<string> LastTo = new();
        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, bool isHtml = false,
            EmailAttachment? attachment = null, CancellationToken ct = default)
        {
            Calls++;
            LastTo = to.ToList();
            return Task.CompletedTask;
        }
    }

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new AppDbContext(options, new NoTenantContext());
    }

    [Fact]
    public async Task NotifyReportCreated_EmailHabilitado_EnviaEmail()
    {
        var db = NewDb();
        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.EmailNotificationsEnabled = true;
        tenant.NotificationEmails = "admin@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var sender = new RecordingEmailSender();
        var svc = new NotificationService(
            new INotificationChannel[] { new EmailChannel(sender) }, db);

        await svc.NotifyReportCreatedAsync(new ReportCreatedNotification(
            tenant.Id, "VAC-001", "NoFunciona", "No aspira", "Piso 1", "Ana"));

        Assert.Equal(1, sender.Calls);
        Assert.Contains("admin@mastercorp.com", sender.LastTo);
    }

    [Fact]
    public async Task NotifyReportCreated_EmailDeshabilitado_NoEnvia()
    {
        var db = NewDb();
        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.EmailNotificationsEnabled = false;
        tenant.NotificationEmails = "admin@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var sender = new RecordingEmailSender();
        var svc = new NotificationService(
            new INotificationChannel[] { new EmailChannel(sender) }, db);

        await svc.NotifyReportCreatedAsync(new ReportCreatedNotification(
            tenant.Id, "VAC-001", "Leve", "x", null, null));

        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task NotifyReportCreated_CanalQueFalla_NoPropagaExcepcion()
    {
        var db = NewDb();
        var tenant = Tenant.Create("mastercorp", "MasterCorp");
        tenant.EmailNotificationsEnabled = true;
        tenant.NotificationEmails = "admin@mastercorp.com";
        db.Tenants.Add(tenant);
        db.SaveChanges();

        var svc = new NotificationService(
            new INotificationChannel[] { new ThrowingChannel() }, db);

        // No debe lanzar aunque el canal explote.
        await svc.NotifyReportCreatedAsync(new ReportCreatedNotification(
            tenant.Id, "VAC-001", "Leve", "x", null, null));
    }

    private sealed class ThrowingChannel : INotificationChannel
    {
        public string Name => "throwing";
        public bool IsEnabled(Tenant tenant) => true;
        public Task SendReportCreatedAsync(Tenant tenant, ReportCreatedNotification n, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }
}
