using Valentinos.Api.Reports;
using Xunit;

namespace Valentinos.Tests.Reports;

// DailyReportSchedule es la lógica pura extraída de DailyReportScheduler para poder
// probar la decisión de "toca enviar" sin timers ni relojes reales. Estos tests son
// la prueba de que el bug de suspensión/reanudación (envío a destiempo o duplicado)
// está resuelto: antes no existía ningún punto de decisión testeable.
public class DailyReportScheduleTests
{
    private static readonly TimeSpan SendAt = new(10, 0, 0);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    [Fact]
    public void ShouldSend_AntesDeLaHoraObjetivo_NoEnviadoHoy_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 9, 59, 0);
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: null));
    }

    [Fact]
    public void ShouldSend_DespuesDeLaHoraObjetivo_DentroDeLaVentana_NoEnviadoHoy_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: null));
    }

    [Fact]
    public void ShouldSend_DespuesDeLaHoraObjetivo_YaEnviadoHoy_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        var lastSent = DateOnly.FromDateTime(now);
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: lastSent));
    }

    [Fact]
    public void ShouldSend_DespuesDeLaHoraObjetivo_EnviadoAyer_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        var lastSent = DateOnly.FromDateTime(now.AddDays(-1));
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: lastSent));
    }

    [Fact]
    public void ShouldSend_ExactamenteALaHoraObjetivo_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 0, 0);
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: null));
    }

    // Caso "reinicio a las 15:00 tras un envío ya hecho a las 10:00": muy fuera de la
    // ventana de gracia, no debe reenviar (aunque lastSentDate esté vacío en memoria,
    // como pasaría tras un reinicio sin persistencia -- ver DailyReportStateTests).
    [Fact]
    public void ShouldSend_FueraDeLaVentanaDeGracia_MaquinaReanudoTarde_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 14, 0, 0); // 4 horas tarde
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: null));
    }

    // Caso "reinicio a las 10:05 tras un envío ya hecho a las 10:00": dentro de la
    // ventana, pero lastSentDate (persistido y releído al arrancar) ya marca hoy.
    [Fact]
    public void ShouldSend_DentroDeLaVentana_PeroYaEnviadoHoy_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        var lastSent = DateOnly.FromDateTime(now);
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: lastSent));
    }

    [Fact]
    public void ShouldSend_ExactamenteEnElLimiteDeLaVentana_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 10, 0, 0) + Grace; // 10:15:00, límite exclusivo
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: null));
    }

    [Fact]
    public void ShouldSend_UnSegundoAntesDelLimiteDeLaVentana_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 0, 0) + Grace - TimeSpan.FromSeconds(1); // 10:14:59
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, Grace, lastSentDate: null));
    }

    [Fact]
    public void Lateness_ALaHoraObjetivo_EsCero()
    {
        var now = new DateTime(2026, 8, 7, 10, 0, 0);
        Assert.Equal(TimeSpan.Zero, DailyReportSchedule.Lateness(now, SendAt));
    }

    [Fact]
    public void Lateness_DespuesDeLaHoraObjetivo_DevuelveElRetrasoCorrecto()
    {
        var now = new DateTime(2026, 8, 7, 10, 42, 0);
        Assert.Equal(TimeSpan.FromMinutes(42), DailyReportSchedule.Lateness(now, SendAt));
    }
}
