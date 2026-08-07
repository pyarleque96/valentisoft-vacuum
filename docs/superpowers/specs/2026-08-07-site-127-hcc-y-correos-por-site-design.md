# Site 127 HCC + correos diarios por site — Diseño

Fecha: 2026-08-07
Estado: aprobado por el usuario, pendiente de plan de implementación

## Contexto

La plataforma tiene un tenant (`mastercorp`) con el site `069` en producción y tres
placeholders sembrados sin contenido: `002`, `003`, `004`. Se solicita implementar el
primero de esos tres como el site real **127 HCC**, con sus 13 vacuums, sus 5 empleados
y sus 3 destinatarios de notificación.

En paralelo cambia el esquema de correos. Hoy sale **un solo** correo diario a las 10:00
por tenant, a `Tenant.NotificationEmails`, con el PDF de todos los sites juntos y el CC
global de `Smtp:Cc`. El campo `Site.CcEmails` existe en el modelo y se edita desde el
panel, pero **no se usa para enviar nada**. Se quiere que cada site reciba su propio
correo con solo su data, y que además siga saliendo el consolidado.

### Restricción operativa

**No se envía ningún correo — ni de prueba — hasta que la implementación esté terminada
y el usuario lo autorice explícitamente.** En los user-secrets del proyecto
`Smtp:Enabled = true`, así que arrancar la app y dejarla viva a las 10:00 dispara envíos
reales. Durante la implementación no se arranca la app con SMTP activo y no se invoca
`POST /admin/reports/sample`.

## Decisiones tomadas

| Tema | Decisión |
|---|---|
| Registro del site | Reemplazar el placeholder `002` (conserva su slug `Kp7Qm`) |
| Código del site | `127 HCC` literal |
| Vacuums con nombre | Códigos propios `VAC-TIMESQUARE` y `VAC-FRONTDESK` |
| Empleados | Scopeados por site (nueva columna `Employee.SiteId`) |
| Formato de nombres | Normalizado a `Apellidos, Nombre` |
| Correo por site | 10:00, TO = emails del site, **sin CC**, solo data de ese site |
| Correo general | 10:00, TO = `christopher.davey@mastercorp.com`, CC = `ramces.rodriguez@mastercorp.com` |
| Site sin actividad | No se envía su correo |
| Entorno | El servidor corre en `Development`, por lo que `DemoSeeder` sí se ejecuta |

## Arquitectura

Cinco piezas independientes. Solo la 4 depende de la 3 (renombre del campo).

### 1. Datos del site 127 HCC (`DemoSeeder`)

El arreglo `seedSites` cambia la entrada `002` por:

```
(Code: "127 HCC", Slug: "Kp7Qm", Emails: "learsy.betancourt@mastercorp.com, carlos.reyes@mastercorp.com, gilberto.espinoza@mastercorp.com")
```

Las entradas `003` y `004` se mantienen como placeholders.

**Idempotencia.** El site ya existe en la base con `Code = "002"`. La siembra debe:

1. Buscar el site por **slug** (`Kp7Qm`), que es la identidad estable.
2. Si existe y su `Code` sigue siendo `"002"`, renombrarlo a `"127 HCC"`. Si el code ya
   es otro valor, no tocarlo (alguien lo editó desde el panel).
3. Asignar los emails **solo si el campo está vacío**, para no pisar ediciones hechas
   desde `/admin/sites/{key}`.
4. Si el site no existe, crearlo con code, slug y emails.

**Vacuums.** Se crean los 13 solo si el site no tiene ninguno (`!await db.Assets.AnyAsync(a => a.SiteId == site127.Id)`):

- `VAC-001` … `VAC-011` (11 numerados)
- `VAC-TIMESQUARE`
- `VAC-FRONTDESK`

Todos con `AssetTypeId` del tipo `VAC` y `Estado = Activo`. El índice único es
`(SiteId, Codigo)`, así que no colisionan con los `VAC-001..016` del 069.

`AdminController.ParseVacNumber` devuelve `0` para sufijos no numéricos, de modo que el
botón "Add vacuums" del panel seguirá numerando desde `VAC-012` sin tropezar con los dos
códigos con nombre. No requiere cambios.

**Empleados.** Los 5 del site, con `SiteId = site127.Id`:

```
Gonzalez Guerra, Yanet
Jeronimo, Brissman
Martea, Lidia
Pacheco, Wendy
Zdor, Galina
```

Se siembran solo si el site no tiene empleados todavía.

**Arreglo necesario en la siembra del 069.** El bloque actual cuenta assets de forma
global:

```csharp
var nAssets = await db.Assets.CountAsync(a => a.AssetTypeId == tipo.Id);
for (var i = nAssets; i < 16; i++) { ... }
```

Con 13 vacuums más en el site 127, ese conteo pasa de 16 y el bloque deja de sembrar
aunque al 069 le falten vacuums. Se cambia a contar por site
(`a.SiteId == site069.Id && a.AssetTypeId == tipo.Id`) y a numerar por site en vez de
usar el contador global `tipo.CorrelativoActual`, que es compartido entre sites y ya no
corresponde a la numeración por-site introducida en la migración `SiteScopedVacuumCode`.

Sin este arreglo el seeder queda silenciosamente roto para el 069.

### 2. Empleados por site (migración `EmployeeSiteScoped`)

- Nueva propiedad `Employee.SiteId` (`Guid`, requerido).
- Índice `(TenantId, SiteId, Nombre)` reemplaza al actual `(TenantId, Nombre)`.
- **Backfill dentro del `Up()` de la migración**, en SQL, no en el seeder: las filas
  existentes con `SiteId = '00000000-0000-0000-0000-000000000000'` se asignan al site
  cuyo `Code` es `'069'` dentro del mismo tenant. Ponerlo en la migración lo hace
  independiente de que `DemoSeeder` corra.
- `GET /api/public/{siteSlug}/employees` (en `DemoController`) filtra además por
  `SiteId == site.Id`. El autocompletar del formulario del 127 solo sugiere a sus 5
  empleados, y el del 069 solo a los 16 suyos.

`Employee` sigue siendo `ITenantOwned`; el filtro global por tenant no cambia.

### 3. Renombrar `Site.CcEmails` → `Site.Emails` (migración `SiteEmailsRename`)

El campo deja de ser un CC y pasa a ser la lista de destinatarios directos del correo
del site. Mantener el nombre `CcEmails` describiría mal el modelo justo en el punto donde
cambia la semántica.

- Propiedad del dominio: `Site.Emails`.
- `RenameColumn` en la migración (`CcEmails` → `Emails`), conservando `HasMaxLength(1000)`.
- Se actualizan los usos en `AdminController` (creación, guardado, grilla y formulario)
  y en `DemoSeeder`.

### 4. Correos (`ReportEmailer` + `DailyReportScheduler`)

`ReportEmailer` hoy hace dos cosas mezcladas dentro de `SendAsync`: arma el modelo KPI de
cada site y decide a quién enviar el consolidado. Se separa:

- `BuildSiteKpiAsync(tenant, site, period, asOf, ct) -> PeriodKpi` — arma el modelo de un
  site (check-ins y no-disponibles filtrados por `SiteId`, ventana de 29 días hacia
  atrás). Es el cuerpo del `foreach` actual, extraído.
- `SendSiteAsync(tenant, site, period, asOf, ct)` — construye el modelo de **un** site y
  lo envía a `Site.Emails` con `includeCc: false`. Si el site no tiene emails o no tuvo
  actividad en el periodo, no envía y devuelve vacío.
- `SendAsync(...)` — sin cambios de comportamiento: el consolidado de todos los sites a
  `Tenant.NotificationEmails`, con el CC configurado.

Reutiliza el mismo `KpiPdf.RenderMulti` y `KpiHtml.RenderReportEmail` con una lista de un
solo elemento, para que el correo por site se vea igual que el consolidado pero con una
sola sección.

Asunto del correo por site: `[ValentiSoft] Daily report · MasterCorp · Site 127 HCC`
(el consolidado conserva el suyo: `[ValentiSoft] Daily report · MasterCorp`).

`DailyReportScheduler.SendDailyForAllTenantsAsync` pasa a, por cada tenant:

1. Recorrer sus sites con `Emails` no vacío y llamar `SendSiteAsync` para cada uno.
2. Luego enviar el consolidado con `SendAsync`, si el tenant tiene
   `NotificationEmails`.

Cada envío va en su propio `try/catch`: que falle el correo de un site no debe impedir
los demás ni el consolidado. Hoy el scheduler filtra los tenants por
`NotificationEmails != null`; ese filtro se relaja para que un tenant que solo tenga
emails por site también sea procesado.

**Destinatario del consolidado.** `DemoSeeder` se invoca desde `Program.cs` con
`"christopher.strait@mastercorp.com,christopher.davey@mastercorp.com"`. Pasa a ser solo
`christopher.davey@mastercorp.com`. El CC (`ramces.rodriguez@mastercorp.com`) ya está
configurado en `Smtp:Cc` en los user-secrets y no requiere cambios.

Nota de comportamiento aceptada: ramces recibe el correo por-site del 069 (es su
encargado) y además queda en CC del consolidado. Son dos correos distintos, es lo
esperado.

### 5. UI del panel (`AdminController`)

Renombrar la etiqueta `Recipients (CC)` a `Emails` en los cuatro lugares donde aparece:

- Cabecera de la tabla en `SitesHtml` (`<th>Recipients (CC)</th>`).
- Label del formulario oculto "New site".
- Label del formulario de configuración del site (`Recipients (CC) — comma separated`
  → `Emails — comma separated`).
- Comentarios del código y del bloque de validación JS que mencionan "Recipients (CC)",
  incluido el comentario de la regla CSS que oculta esa columna en móvil.

La clase CSS `.cccell` es interna y no se muestra al usuario; puede quedarse como está o
renombrarse, es indiferente.

## Manejo de errores

- Seeder: todo idempotente y condicionado a la existencia previa de la fila; correr el
  seeder N veces produce el mismo estado.
- Migración: el backfill de `SiteId` solo toca filas con `Guid.Empty`; si el site `069`
  no existiera en ese tenant, las filas quedan en `Guid.Empty` y simplemente no aparecen
  en el autocompletar de ningún site (falla cerrado, no filtra datos entre sites).
- Envío: un `try/catch` por site y otro por el consolidado, con log del error y del
  destinatario. Un fallo de SMTP en un site no aborta el resto.

## Verificación (sin enviar ningún correo)

Tests con un `IEmailSender` falso que captura destinatarios, asunto, cuerpo, adjunto y el
flag `includeConfiguredCc` en memoria — nunca abre una conexión SMTP.

1. `SendSiteAsync` del site 127 envía solo a sus 3 emails y con `includeConfiguredCc == false`.
2. El cuerpo/modelo del correo del 127 no contiene check-ins ni no-disponibles del 069
   (aislamiento de data por site).
3. El consolidado va a `Tenant.NotificationEmails` con `includeConfiguredCc == true` e
   incluye ambos sites.
4. Un site sin actividad en el periodo no genera envío.
5. El endpoint de empleados devuelve solo los del site consultado.
6. La siembra es idempotente: correrla dos veces no duplica vacuums ni empleados, y el
   site `002` termina como `127 HCC`.

Más la suite existente (`dotnet test`) y una revisión visual de `/admin/sites`,
`/admin/sites/Kp7Qm` y `/reports?site=Kp7Qm`.

No se arranca la app con `Smtp:Enabled = true` ni se invoca `POST /admin/reports/sample`
en ningún momento de la implementación.

## Fuera de alcance

- Los sites `003` y `004` siguen como placeholders vacíos.
- No se agrega UI para gestionar empleados; siguen viniendo del seeder.
- No se cambia el correo de issue/operational (`EmailChannel`), solo el de reportes KPI.
