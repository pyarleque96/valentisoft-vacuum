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

    [Fact]
    public void ShouldSend_AntesDeLaHoraObjetivo_NoEnviadoHoy_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 9, 59, 0);
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, lastSentDate: null));
    }

    [Fact]
    public void ShouldSend_DespuesDeLaHoraObjetivo_NoEnviadoHoy_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, lastSentDate: null));
    }

    [Fact]
    public void ShouldSend_DespuesDeLaHoraObjetivo_YaEnviadoHoy_DevuelveFalse()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        var lastSent = DateOnly.FromDateTime(now);
        Assert.False(DailyReportSchedule.ShouldSend(now, SendAt, lastSentDate: lastSent));
    }

    [Fact]
    public void ShouldSend_DespuesDeLaHoraObjetivo_EnviadoAyer_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 5, 0);
        var lastSent = DateOnly.FromDateTime(now.AddDays(-1));
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, lastSentDate: lastSent));
    }

    [Fact]
    public void ShouldSend_ExactamenteALaHoraObjetivo_DevuelveTrue()
    {
        var now = new DateTime(2026, 8, 7, 10, 0, 0);
        Assert.True(DailyReportSchedule.ShouldSend(now, SendAt, lastSentDate: null));
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
