# Task 4 Report: Hoja de QR en lote (PDF)

## Summary

Implemented `IQrSheetRenderer` / `QrSheetItem` in `Valentinos.Application.Qr` and
`SkiaQrSheetRenderer` in `Valentinos.Infrastructure.Qr`, which composes a batch of QR
codes into a multi-page A4 PDF grid (3 columns x 4 rows per page) using `IQrRenderer`
(Task 3) for each item's PNG and `SKDocument.CreatePdf` for the PDF document. Registered
`IQrSheetRenderer` as a singleton in `AddInfrastructure`. Followed strict TDD.

## Files changed

- Created: `src/Valentinos.Application/Qr/IQrSheetRenderer.cs`
- Created: `src/Valentinos.Infrastructure/Qr/SkiaQrSheetRenderer.cs`
- Modified: `src/Valentinos.Infrastructure/DependencyInjection.cs` (added
  `services.AddSingleton<IQrSheetRenderer, SkiaQrSheetRenderer>();`)
- Created: `tests/Valentinos.Tests/Qr/SkiaQrSheetRendererTests.cs`

## TDD evidence

### RED (Step 2)

Wrote `SkiaQrSheetRendererTests.cs` verbatim from the brief before any implementation
existed. Ran:

```
dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SkiaQrSheetRendererTests
```

Result: compilation error, as expected —

```
error CS0246: El nombre del tipo o del espacio de nombres 'SkiaQrSheetRenderer' no se
encontró (¿falta una directiva using o una referencia de ensamblado?)
```

This confirms the test fails for the right reason (missing types), not an environment issue.

### GREEN (Step 6)

After implementing `IQrSheetRenderer`, `QrSheetItem`, `SkiaQrSheetRenderer`, and the DI
registration:

```
dotnet test tests/Valentinos.Tests --filter FullyQualifiedName~SkiaQrSheetRendererTests
```

Result:

```
Correctas! - Con error: 0, Superado: 2, Omitido: 0, Total: 2, Duración: 136 ms
```

Both tests pass:
- `RenderPdf_VariosItems_ProduceUnPdfValido` — 4 items, PDF > 1000 bytes, header `%PDF-`.
- `RenderPdf_ListaVacia_ProducePdfValido` — empty list still yields a valid `%PDF-`
  document (the implementation forces `pages = Math.Max(1, ceil(count/perPage))` so at
  least one empty page is always emitted via `doc.BeginPage`/`doc.EndPage`).

### Full suite (Step 7)

```
dotnet test Valentinos.sln
```

Result:

```
Correctas! - Con error: 0, Superado: 27, Omitido: 0, Total: 27, Duración: 531 ms
```

27/27 tests green (25 pre-existing + 2 new).

## API adaptations vs. brief reference code

The brief's reference implementation compiled and ran essentially as-is against the
installed SkiaSharp 4.150.1 / net10.0. One small change was made, not required for
correctness but to avoid a compiler warning surfaced during the first GREEN run:

- `canvas.DrawBitmap(bmp, SKRect)` (implicit `SKPaint`-less overload) triggered
  `CS0618: 'SKCanvas.DrawBitmap(SKBitmap, SKRect, SKPaint)' está obsoleto: 'Use the
  overload with SKSamplingOptions instead.'`
- Adapted the call to `canvas.DrawBitmap(bmp, SKRect, SKSamplingOptions)`, passing
  `new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)` — the same sampling
  option pattern already used in `SkiaQrRenderer.RenderPng` (Task 3) for consistency.
  No other signature differences were encountered; `SKDocument.CreatePdf(Stream)`,
  `doc.BeginPage(float, float)`, `doc.EndPage()`, and `doc.Close()` all matched the
  brief's reference code exactly.

## Self-review

- **Spec coverage**: fulfills "descarga en lote (hoja PDF)" from Plan 2 scope. QR
  correctness (URL format, logo watermark, printed code) is inherited/guaranteed by
  Task 3's `IQrRenderer`/`SkiaQrRenderer`, which this task only composes into a grid —
  consistent with the brief's stated note that decoding is out of scope here.
- **Placeholder scan**: no TBD/TODO; implementation matches the brief's reference code
  verbatim except for the `SKSamplingOptions` overload adaptation, documented above.
- **Type consistency**: `QrSheetItem(Slug, Codigo, LogoPng)` mirrors `QrRenderRequest`'s
  fields exactly; `IQrSheetRenderer.RenderPdf(IReadOnlyList<QrSheetItem>)` is the only
  public surface, consumed correctly by the test via constructor injection of
  `SkiaQrRenderer`.
- **DI registration**: added alongside the existing `IQrRenderer` registration in
  `AddInfrastructure`, singleton lifetime matching `IQrRenderer`'s lifetime (both are
  stateless, thread-safe renderers).
- **Out of scope** (per brief, unchanged): HTTP endpoints to expose this batch PDF
  generation are deferred to Plan 4 (requires JWT admin auth).

## Commit

```
a7576f8 feat: generación de hoja PDF con múltiples QR en grilla
```

## Fix wave (revisión final Plan 2)

Applied three Minor-severity cleanup items from the final review of Plan 2, no public
interface or behavior changes beyond what's described below.

### Fix 1 — dead constant removed

`src/Valentinos.Infrastructure/Qr/SkiaQrRenderer.cs`: removed the unused
`private const int QuietModules = 4;`. QRCoder's `ModuleMatrix` already includes the
quiet zone (confirmed by the existing comment on `matrix` — "incluye quiet zone"), so
this constant was never read anywhere in the class. No other change to the file.

### Fix 2 — asset list ordering fixed past 999

`src/Valentinos.Infrastructure/Assets/AssetService.cs`, `ListAssetsAsync`: changed
`OrderBy(a => a.Codigo)` to `OrderBy(a => a.CreatedAt).ThenBy(a => a.Codigo)`. The old
lexical string ordering on `Codigo` broke down once the correlativo crossed the 3-to-4
digit boundary: e.g. `"VAC-1000"` sorts before `"VAC-999"` as a string (`'1' < '9'`),
even though `VAC-999` was created first. Ordering by `CreatedAt` (creation/correlative
order) with `Codigo` as a tiebreaker fixes this. `ListAssetTypesAsync` (ordered by
`Prefijo`) was left untouched — it isn't subject to the same digit-count problem.

Added `ListAssetsAsync_OrdenaPorCreacionNoLexicalmente_MasAllaDe999` to
`tests/Valentinos.Tests/Assets/AssetServiceTests.cs`: creates an `AssetType`, loads the
tracked entity and force-sets `CorrelativoActual = 998`, saves, then creates three
assets in sequence, asserting codes `VAC-999`, `VAC-1000`, `VAC-1001` in that order.
`ListAssetsAsync()` is then asserted to return exactly `["VAC-999", "VAC-1000",
"VAC-1001"]`.

**This test genuinely distinguishes creation-order from lexical order**: under the old
`OrderBy(a => a.Codigo)` implementation, string comparison would place `"VAC-1000"` and
`"VAC-1001"` before `"VAC-999"` (since `"1"` < `"9"` lexically at the first differing
character), producing `["VAC-1000", "VAC-1001", "VAC-999"]` — which fails the
`Assert.Equal(new[] { "VAC-999", "VAC-1000", "VAC-1001" }, ...)` assertion. The new
`CreatedAt`-based ordering returns the assets in the order they were actually created,
satisfying the assertion. (In-memory EF Core `DateTime.UtcNow` timestamps assigned in
`BaseEntity` are monotonically increasing across the three sequential
`await CreateAssetAsync(...)` calls, so `CreatedAt` ordering is deterministic here.)

### Fix 3 — multi-page PDF coverage

`tests/Valentinos.Tests/Qr/SkiaQrSheetRendererTests.cs`: added
`RenderPdf_MasDeUnaPagina_ProduceUnPdfValidoYMasGrandeQueUnaSolaPagina`, which renders
13 items (grid is `Columns=3 x RowsPerPage=4` = 12 per page in
`SkiaQrSheetRenderer`, so 13 forces a second page via the `pages > 1` branch of
`RenderPdf`) and asserts the output starts with `%PDF-` and is strictly larger than the
existing 4-item single-page PDF. `LogoPng` is `null` for all items to keep the test
fast.

### Verification

```
dotnet build Valentinos.sln
```
Result: `Compilación correcta. 0 Advertencia(s). 0 Errores.` — build is warning-free.

```
dotnet test Valentinos.sln
```
Result:
```
Correctas! - Con error:     0, Superado:    29, Omitido:     0, Total:    29, Duración: 513 ms
```
29/29 tests green (27 pre-existing + 2 new: the ordering test and the multi-page PDF
test).

### Commit

```
refactor: limpieza de hallazgos menores de la revisión de Plan 2 (orden de listado, constante muerta, cobertura PDF multipágina)
```
