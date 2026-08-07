using Microsoft.Extensions.Logging.Abstractions;
using Valentinos.Api.Reports;
using Xunit;

namespace Valentinos.Tests.Reports;

// DailyReportState persiste la fecha del último envío diario para que el scheduler
// no la pierda al reiniciar el proceso (ver DailyReportScheduleTests para el caso de
// "reinicio a las 10:05 tras un envío ya hecho a las 10:00").
public class DailyReportStateTests : IDisposable
{
    private readonly string _dir;

    public DailyReportStateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "valentinos-daily-report-state-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private DailyReportState CreateState(string fileName = "state.txt")
        => new(Path.Combine(_dir, fileName), NullLogger<DailyReportState>.Instance);

    [Fact]
    public void RoundTrip_EscribeYLee_DevuelveLaMismaFecha()
    {
        var state = CreateState();
        var date = new DateOnly(2026, 8, 7);

        state.WriteLastSent(date);

        Assert.Equal(date, state.ReadLastSent());
    }

    [Fact]
    public void ReadLastSent_ArchivoInexistente_DevuelveNull()
    {
        var state = CreateState();

        Assert.Null(state.ReadLastSent());
    }

    [Fact]
    public void ReadLastSent_ArchivoVacio_DevuelveNull()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "state.txt");
        File.WriteAllText(path, string.Empty);
        var state = new DailyReportState(path, NullLogger<DailyReportState>.Instance);

        Assert.Null(state.ReadLastSent());
    }

    [Fact]
    public void ReadLastSent_ArchivoCorrupto_DevuelveNull_NoLanza()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "state.txt");
        File.WriteAllText(path, "esto no es una fecha ¯\\_(ツ)_/¯");
        var state = new DailyReportState(path, NullLogger<DailyReportState>.Instance);

        var result = Record.Exception(() => state.ReadLastSent());

        Assert.Null(result);
        Assert.Null(state.ReadLastSent());
    }

    [Fact]
    public void WriteLastSent_DirectorioInexistente_LoCrea()
    {
        var nestedDir = Path.Combine(_dir, "no-existe-todavia");
        var path = Path.Combine(nestedDir, "state.txt");
        var state = new DailyReportState(path, NullLogger<DailyReportState>.Instance);

        state.WriteLastSent(new DateOnly(2026, 8, 7));

        Assert.True(Directory.Exists(nestedDir));
        Assert.Equal(new DateOnly(2026, 8, 7), state.ReadLastSent());
    }
}
