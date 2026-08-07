# Reportes: vista privada multi-site y vista pública por site — Diseño

Fecha: 2026-08-07
Estado: aprobado por el usuario, pendiente de plan de implementación

## Contexto

Hoy existe una sola página de KPIs: `GET /reports`, marcada `[AllowAnonymous]`
(`DemoController.cs:442`). Recibe un site por query string (`?site={slugOrGuid}`), y si
no se le pasa ninguno usa el site por defecto. Además **pinta un dropdown con todos los
sites del tenant**, así que cualquiera que tenga el link puede ver los KPIs de un site y
saltar libremente a los demás.

Se pide separarla en dos páginas:

- una **con login**, que es la de todos los sites con el combo selector;
- otra **sin login**, acotada a un solo site, sin combo, que muestre directamente su data.

El efecto neto no es solo partir la página en dos: es **cerrar** la vista multi-site
detrás de autenticación y dejar pública únicamente una vista que no permite enumerar ni
alcanzar otros sites. Respecto del estado actual, esto reduce la exposición.

## Decisiones tomadas

| Tema | Decisión |
|---|---|
| Ruta privada | `GET /reports` (la actual), ahora con `[Authorize(Roles = "admin")]` |
| Ruta pública | `GET /{siteSlug}/reports` |
| Protección de la pública | Únicamente el slug aleatorio del site, igual que los QR |
| PDF privado | `GET /reports/pdf?site=X`, pasa a requerir login |
| PDF público | `GET /{siteSlug}/reports/pdf` |
| La pública conserva | Selector de periodo, botón de PDF, tabla de detalle, toggle ES/EN |
| Links `/reports?site=X` ya compartidos | Siguen existiendo, pero ahora piden login |

La ruta pública sigue el patrón que ya usan los QR impresos (`/{siteSlug}/e/{codigo}`,
`/{siteSlug}/f/hk`, `DemoController.cs:261,287`), de modo que `/Kp7Qm/reports` no
introduce una convención nueva. La barrera es la misma que ya protege los formularios
que se llegan por QR: un slug aleatorio de 5-6 caracteres.

## Arquitectura

Tres piezas. Ninguna toca el cálculo de KPIs ni los correos.

### 1. Ruteo y autorización

`DemoController` gana dos acciones y cambia el atributo de dos existentes.

**Privadas** (`[Authorize(Roles = "admin")]`, consistente con `AdminController`):

- `GET /reports` — sin cambios de comportamiento salvo el atributo. Sigue aceptando
  `?site=` y `?period=`, sigue resolviendo el site por defecto vía `ReportsSiteKeyAsync`
  cuando no se pasa ninguno, y sigue pasando la lista de sites del tenant al render.
- `GET /reports/pdf` — solo cambia el atributo.

**Públicas** (`[AllowAnonymous]`):

- `GET /{siteSlug}/reports` — resuelve el site por slug, **ignora cualquier `?site=`**, y
  renderiza con lista de sites vacía. Acepta `?period=`. Devuelve 404 si el slug no
  existe.
- `GET /{siteSlug}/reports/pdf` — el PDF de ese site y solo de ese site. Acepta
  `?period=`. Devuelve 404 si el slug no existe.

Las públicas usan el `ResolveSiteAsync(siteSlug)` que ya existe (`DemoController.cs:57`),
que resuelve el site por slug y obtiene su tenant, en vez de `ReportsSiteKeyAsync`, que
acepta guid además de slug y cae al site por defecto cuando no recibe nada. Esa
diferencia es deliberada: la ruta pública no debe tener ningún camino que devuelva un
site distinto del que nombra la URL.

**Coincidencia de tenant en las rutas privadas.** `AdminController` implementa
`IActionFilter` para exigir que el tenant del claim de la cookie coincida con el del
subdominio (`AdminController.cs:35-41`), como defensa en profundidad frente a un cruce de
tenants. `DemoController` no tiene esa verificación, y `/reports` resuelve el site contra
`Tid`, que sale del subdominio. Las cookies son host-only, así que el cruce no ocurre en
condiciones normales; aun así, al pasar `/reports` y `/reports/pdf` a ser rutas
autenticadas conviene aplicarles la misma comprobación que ya protege al panel, en vez de
dejar dos criterios distintos para páginas autenticadas del mismo producto.

**Riesgo de colisión de rutas.** `/{siteSlug}/reports` es una plantilla de dos segmentos
con el primero variable. Ya existen `/{siteSlug}/e/{codigo}`, `/{siteSlug}/f/hk` y
`/{siteSlug}/f/report`, así que el patrón convive con las rutas actuales; pero hay que
verificar que `/{siteSlug}/reports` no capture peticiones destinadas a rutas literales de
dos segmentos. La lista de slugs reservados de `AdminController.ReservedSlugs`
(`admin`, `login`, `logout`, `api`, `qr`, `e`, `f`, `images`, `favicon`) existe justamente
para impedir que un slug colisione con una ruta top-level, y el emisor de slugs la
respeta.

### 2. Renderizado

`KpiHtml.Render(PeriodKpi m, string slug, IReadOnlyList<KpiSiteOption> sites)`
(`KpiHtml.cs:20`) construye hoy las opciones del `<select>` en un bucle y las inserta
siempre.

Pasa a **omitir el bloque completo del selector cuando `sites` viene vacía**. La vista
pública le pasa una lista vacía; la privada le pasa los sites del tenant. Queda un solo
camino de render, sin duplicar la plantilla ni crear una segunda función paralela que
haya que mantener en sincronía.

No basta con dejar el `<select>` vacío ni con ocultarlo por CSS: el marcado no debe
contener los slugs ni los códigos de los demás sites, porque eso sigue siendo una fuga
para quien mire el HTML.

El resto de la página pública queda idéntico a la actual: selector de periodo, botón de
exportar PDF, tabla de detalle (empleado, vacuum, hora, notas) y toggle de idioma ES/EN.

### 3. Enlaces internos

Los enlaces del panel admin a `/reports?site={slug}` (`AdminController.cs:199,241`) viven
dentro de páginas ya autenticadas y siguen funcionando sin cambios.

El botón de exportar PDF de la página renderizada apunta hoy a `/reports/pdf?site=…`.
En la vista pública debe apuntar a `/{siteSlug}/reports/pdf`, o el botón fallaría con un
redirect a login. `Render` ya recibe el `slug`, así que puede construir la URL correcta
según reciba o no la lista de sites.

## Manejo de errores

- Slug inexistente en una ruta pública: 404, sin filtrar si el site existe en otro tenant.
- Site sin actividad en el periodo: la página ya maneja ese caso (muestra ceros y el botón
  de PDF abre un modal de "sin data" en vez de exportar vacío). No cambia.
- Petición no autenticada a una ruta privada: redirect 302 a `/login`, que es el
  comportamiento que ya configura `Program.cs:47` para navegaciones de página.

## Verificación

Tests de endpoint con `WebApplicationFactory`, siguiendo el patrón de
`PublicReportsEndpointTests` y `EmployeesEndpointTests`:

1. `GET /reports` sin cookie → 302 a `/login`.
2. `GET /reports/pdf` sin cookie → 302 a `/login`.
3. `GET /{slug}/reports` sin cookie → 200.
4. `GET /{slug}/reports/pdf` sin cookie → 200 y `Content-Type: application/pdf`.
5. `GET /{slugInexistente}/reports` → 404.
6. **Aislamiento:** el HTML de `/{slug}/reports` de un tenant con dos sites **no contiene**
   el `<select>` de sites, ni el slug, ni el código del otro site. Esta es la prueba que
   distingue "quitamos el combo" de "la página realmente no expone los otros sites".
7. `GET /{slugDelSiteA}/reports` no muestra check-ins del site B (aislamiento de data, no
   solo de navegación).

## Fuera de alcance

- El cálculo de KPIs (`Kpi.Compute`), el renderizado del PDF (`KpiPdf`) y los correos de
  reporte no se tocan.
- No se agrega un token adicional para la vista pública: la decisión es que el slug es la
  única barrera, igual que en los QR.
- No se cambia el panel admin ni sus enlaces.
- No se migran ni redirigen los links `/reports?site=X` ya compartidos; pasan a pedir
  login, que es el resultado aceptado.
