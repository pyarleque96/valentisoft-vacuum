# Valentino's — Plataforma QR de Reportes para Housekeeping

**Fecha:** 2026-07-28
**Estado:** Diseño aprobado
**Autor:** Valentino's Software Division

## 1. Contexto y objetivo

Valentino's (división de software) ofrece soluciones a otras empresas para mejorar
sus procesos. Este producto digitaliza el reporte de averías de bienes en el área de
**housekeeping** de hoteles.

Caso inicial: **aspiradoras**, extensible a otros bienes (carritos, etc.). Cada bien
lleva pegado un **código QR** con un identificador visible (ej. `VAC-001`). Al
escanearlo, el housekeeper llega a una página de Valentino's donde reporta si el
equipo está averiado. La plataforma es **multitenant**; el primer cliente es
**MasterCorp**.

### Objetivos del MVP
- Housekeepers reportan averías **sin login** escaneando el QR del activo.
- Admins gestionan tipos de activo, activos, QR, reportes, usuarios y KPIs, **con
  login por tenant**.
- **Alerta por email** al crearse un reporte + **reportes automáticos** diario,
  semanal y mensual por email.
- Generación de QR **personalizados** con código visible y **logo del tenant como
  marca de agua**.

## 2. Decisiones tomadas

| Tema | Decisión |
|------|----------|
| Flujo housekeeper | **Anónimo** (sin login). Campo "reportado por" opcional. |
| Flujo admin | **Login por tenant** (JWT), roles Owner/Admin. |
| Primer tenant | **MasterCorp**, tipo Aspiradora con prefijo `VAC`. |
| Multitenancy | **BD compartida + `TenantId`** con global query filter de EF Core. |
| Frontend | **React** (Vite + TypeScript). |
| Backend | **.NET 9** (ASP.NET Core Web API). |
| Códigos de activo | **Tipos con prefijo autogenerado** (VAC-001, VAC-002…). |
| Campos del reporte | Descripción, Foto(s), Severidad, Ubicación/Piso. |
| Panel admin MVP | Ciclo de vida de reporte, KPIs, CRUD activos/tipos, gestión usuarios. |
| Notificaciones | **Email activo**. WhatsApp/SMS/In-App diseñados y **apagados por default**. |
| Almacenamiento fotos | Abstracción `IFileStorage`; disco local en MVP, listo para Azure Blob/S3. |
| Jobs periódicos | **Hangfire** (recurrentes + dashboard). |

## 3. Arquitectura

Backend **.NET 9 ASP.NET Core Web API** + frontend **React (Vite + TS)**.

El frontend sirve dos experiencias:
- **Página pública de reporte**: ruta `/r/:tenant/:codigo` — sin login, ligera,
  optimizada para móvil. Es la que abre el QR.
- **Panel de administración**: rutas `/admin/*` — con login por tenant.

Resolución de tenant:
- Página pública: **slug en la URL** (`mastercorp`).
- Panel admin: **claim `tenant_id` del JWT**.

Aislamiento multitenant: `TenantId` en toda entidad de negocio + **global query
filter** de EF Core para que ningún query se ejecute sin el filtro de tenant. Un
`ITenantContext` (resuelto por middleware) provee el tenant actual del request.

### Estructura de capas (backend)
- `Valentinos.Api` — controllers, middleware, DI, config.
- `Valentinos.Domain` — entidades y reglas de negocio (sin dependencias de infra).
- `Valentinos.Application` — casos de uso / servicios (generación de códigos,
  KPIs, notificaciones), interfaces (`IFileStorage`, `IQrRenderer`,
  `INotificationChannel`).
- `Valentinos.Infrastructure` — EF Core, SQL Server, implementaciones de storage,
  QR, email, Hangfire.

## 4. Modelo de datos

Todas las entidades de negocio incluyen `TenantId`.

- **Tenant**: `Id`, `Slug`, `Nombre`, `LogoUrl`, `NotificationConfig` (canales
  activos, emails destino), timestamps.
- **AssetType**: `Id`, `TenantId`, `Nombre`, `Prefijo`, `CorrelativoActual`.
- **Asset**: `Id`, `TenantId`, `AssetTypeId`, `Codigo` (ej. `VAC-001`), `Estado`
  (Activo/Baja), `Ubicacion`, timestamps.
- **Report**: `Id`, `TenantId`, `AssetId`, `Descripcion`, `Severidad`
  (Leve/AMedias/NoFunciona), `Ubicacion`, `ReportadoPor` (opcional), `Estado`
  (Nuevo/EnReparacion/Resuelto), `Notas`, `CreatedAt`, `ResolvedAt`.
- **ReportPhoto**: `Id`, `ReportId`, `FileKey`, `ContentType`.
- **User** (admin): ASP.NET Core Identity + `TenantId`, `Rol` (Owner/Admin).

**MTTR** se calcula con `ResolvedAt - CreatedAt` sobre reportes resueltos.

### Generación de códigos
La asignación de `Codigo` es transaccional: incrementa `AssetType.CorrelativoActual`
y formatea `PREFIJO-###` de forma atómica para evitar duplicados bajo concurrencia.
Lógica cubierta por TDD.

## 5. Generación de QR

Servicio detrás de la interfaz `IQrRenderer`. Implementación con **QRCoder** (QR) +
**SkiaSharp** (composición de imagen).

La imagen resultante contiene:
- QR apuntando a `https://app.valentinos.com/r/{slug}/{codigo}` (dominio
  configurable por entorno).
- **Código impreso** visible (`VAC-001`).
- **Logo del tenant como marca de agua** al centro (nivel de corrección de error del
  QR suficientemente alto para que el logo no rompa la lectura).

Endpoints:
- `GET /api/assets/{id}/qr` — PNG/SVG de un activo.
- `POST /api/assets/qr-batch` — **hoja PDF** con múltiples QR para imprimir y pegar.

## 6. Reporte y notificaciones

Flujo público: escaneo → formulario (descripción, severidad, piso, fotos) →
`POST /api/public/{slug}/reports` crea el `Report`. Fotos suben vía `IFileStorage`
(disco local en MVP).

Notificaciones vía `INotificationChannel` (patrón strategy, configurable por tenant):
- **EmailChannel** — ✅ activo (SMTP/SendGrid). Dispara **alerta inmediata** al crear
  un reporte, a los emails destino del tenant.
- **InAppChannel**, **WhatsAppChannel** (Twilio), **SmsChannel** (Twilio) —
  implementados como stubs config-driven, **desactivados por default**. Encendibles
  desde `NotificationConfig` sin cambios de código.

**Reportes automáticos** con **Hangfire** (jobs recurrentes por tenant): resúmenes
**diario, semanal y mensual** enviados por email (conteos por estado/severidad,
activos que más fallan, MTTR del periodo).

## 7. Panel admin, auth y KPIs

- **Auth**: ASP.NET Core Identity + **JWT**. Login por tenant; roles Owner/Admin.
- **CRUD** de tipos de activo (nombre + prefijo) y activos, con generación/descarga
  de QR (individual y en lote).
- **Gestión de reportes**: tablero con ciclo de vida Nuevo→EnReparación→Resuelto +
  notas; filtros por estado, severidad, activo, piso, fecha.
- **Usuarios admin**: Owner puede invitar/crear usuarios admin dentro del tenant.
- **KPIs (Recharts)**: reportes por periodo, activos que más fallan, distribución por
  severidad, distribución por piso, y **MTTR (tiempo promedio de resolución)**.

## 8. Stack técnico

- **Backend**: .NET 9, EF Core, SQL Server, Hangfire, QRCoder, SkiaSharp, ASP.NET
  Identity + JWT, xUnit.
- **Frontend**: React + Vite + TypeScript, Tailwind + shadcn/ui, Recharts.
- **Calidad**: TDD en lógica de negocio (generación de códigos, filtros de tenant,
  cálculo de KPIs, generación de resúmenes). Capas con responsabilidades acotadas y
  archivos enfocados.

## 9. Alcance del MVP

Incluye todo lo anterior con **un solo canal activo (email)** y **almacenamiento
local** de fotos. WhatsApp/SMS/In-App y Azure Blob quedan diseñados pero
desactivados. Seed inicial: tenant **MasterCorp**, un usuario Owner, tipo
**Aspiradora (VAC)**.

### Fuera de alcance (futuro)
- Activación de WhatsApp/SMS/Twilio.
- Almacenamiento en la nube (Azure Blob/S3).
- App móvil nativa (la web pública es responsive y suficiente).
- Onboarding self-service de nuevos tenants (se crean manualmente por ahora).
