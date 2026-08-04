using System.Net;

namespace Valentinos.Api.Auth;

// Plantillas profesionales (sin imágenes externas, para máxima compatibilidad) de los
// correos del flujo de autenticación. Branding del tenant (p. ej. MasterCorp) en el
// cuerpo; el remitente visible es "ValentiSoft" (configurado en el SmtpEmailSender).
public static class AuthEmail
{
    public static string Subject(string tenantNombre) =>
        $"[{tenantNombre}] Your password reset code";

    public static string RenderResetCode(string tenantNombre, string code, int minutes)
    {
        var tenant = WebUtility.HtmlEncode(tenantNombre);
        var codeH = WebUtility.HtmlEncode(code);
        var body =
$@"        <tr><td style=""padding:28px 28px 4px;"">
          <h1 style=""margin:0 0 6px;color:#0f172a;font-size:20px;"">Password reset</h1>
          <p style=""margin:0;color:#64748b;font-size:14px;line-height:1.5;"">
            We received a request to reset the password for your <strong style=""color:#0f172a;"">{tenant}</strong> admin account.
            Use the code below to continue.</p>
        </td></tr>
        <tr><td style=""padding:18px 28px 6px;"">
          <div style=""background:#f1f6fc;border:1px solid #dbe8f7;border-radius:12px;padding:18px;text-align:center;"">
            <div style=""color:#64748b;font-size:12px;text-transform:uppercase;letter-spacing:.6px;margin-bottom:8px;"">Verification code</div>
            <div style=""color:#1560A8;font-size:34px;font-weight:800;letter-spacing:8px;font-family:ui-monospace,Consolas,monospace;"">{codeH}</div>
          </div>
        </td></tr>
        <tr><td style=""padding:10px 28px 26px;"">
          <p style=""margin:0 0 6px;color:#475569;font-size:13.5px;line-height:1.5;"">
            This code expires in <strong>{minutes} minutes</strong>. Enter it on the reset page along with your new password.</p>
          <p style=""margin:0;color:#94a3b8;font-size:12.5px;line-height:1.5;"">
            If you didn't request a password reset, you can safely ignore this email — your password won't change.</p>
        </td></tr>";
        return Shell(tenant, body);
    }

    private static string Shell(string tenantNombre, string bodyRows) =>
$@"<!doctype html>
<html><body style=""margin:0;padding:0;background:#eef2f7;font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#eef2f7;padding:24px 12px;""><tr><td align=""center"">
    <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 6px 24px rgba(15,23,42,.08);"">
      <tr><td style=""background:#1560A8;padding:22px 28px;"">
        <div style=""color:#fff;font-size:12px;letter-spacing:1px;text-transform:uppercase;opacity:.85;"">{tenantNombre} · Housekeeping</div>
        <div style=""color:#fff;font-size:20px;font-weight:800;margin-top:2px;"">Account security</div>
      </td></tr>
{bodyRows}
      <tr><td style=""background:#f8fafc;padding:16px 28px;border-top:1px solid #e5e9f0;"">
        <p style=""margin:0;color:#94a3b8;font-size:12px;"">Sent automatically by ValentiSoft on behalf of {tenantNombre}. Please do not reply to this email.</p>
      </td></tr>
    </table>
  </td></tr></table>
</body></html>";
}
