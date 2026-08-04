# -*- coding: utf-8 -*-
"""Captura la app en viewport MÓVIL real (emulación iPhone) con Playwright."""
import os
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "mobile"); os.makedirs(OUT, exist_ok=True)
BASE = os.environ.get("DEMO_BASE", "https://mastercorp.valentisoft.com")
# Credenciales del admin: NUNCA hardcodear. Se leen del entorno.
#   PowerShell:  $env:DEMO_ADMIN_EMAIL="..."; $env:DEMO_ADMIN_PWD="..."; python shoot_mobile.py
EMAIL = os.environ.get("DEMO_ADMIN_EMAIL")
PWD = os.environ.get("DEMO_ADMIN_PWD")
if not EMAIL or not PWD:
    raise SystemExit("Falta DEMO_ADMIN_EMAIL / DEMO_ADMIN_PWD en el entorno.")

IPHONE = {
    "viewport": {"width": 390, "height": 844},
    "device_scale_factor": 3,
    "is_mobile": True,
    "has_touch": True,
    "user_agent": ("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) "
                   "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"),
}

def shot(page, path, full=False):
    page.wait_for_timeout(700)
    page.screenshot(path=path, full_page=full)
    print("  saved", os.path.basename(path))

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(ignore_https_errors=True, **IPHONE)
    page = ctx.new_page()

    # 1) Login
    print("login…")
    page.goto(f"{BASE}/login", wait_until="networkidle")
    shot(page, os.path.join(OUT, "01-login.jpg"))
    page.fill("#email", EMAIL)
    page.fill("#password", PWD)
    page.click("button[type=submit]")
    page.wait_for_url("**/admin/sites", timeout=15000)
    page.wait_for_load_state("networkidle")

    # 2) Panel de sedes
    shot(page, os.path.join(OUT, "02-admin-sites.jpg"))

    # 3) Config de sede
    page.goto(f"{BASE}/admin/sites/XjUS3", wait_until="networkidle")
    shot(page, os.path.join(OUT, "03-site-config.jpg"))

    # 4) Generador de QR
    page.goto(f"{BASE}/admin/sites/XjUS3/qr", wait_until="networkidle")
    shot(page, os.path.join(OUT, "04-qr-generator.jpg"))

    # 5) Dashboard KPIs (arriba: dona + más reportadas)
    page.goto(f"{BASE}/admin/sites/XjUS3/reports?period=daily", wait_until="networkidle")
    shot(page, os.path.join(OUT, "05-kpi-dashboard.jpg"))
    # 6) Detalle (scroll a la tabla)
    page.evaluate("window.scrollBy(0, 780)")
    shot(page, os.path.join(OUT, "06-kpi-detail.jpg"))

    # 7) Escanear equipo (público)
    page.goto(f"{BASE}/XjUS3/e/VAC-001", wait_until="networkidle")
    shot(page, os.path.join(OUT, "07-equipment-intro.jpg"))
    # 8) Formulario de estado
    page.goto(f"{BASE}/XjUS3/e/VAC-001/report", wait_until="networkidle")
    shot(page, os.path.join(OUT, "08-report-form.jpg"))
    # 9) Housekeeping intro
    page.goto(f"{BASE}/XjUS3/f/hk", wait_until="networkidle")
    shot(page, os.path.join(OUT, "09-housekeeping-intro.jpg"))
    # 10) Housekeeping form
    page.goto(f"{BASE}/XjUS3/f/report", wait_until="networkidle")
    shot(page, os.path.join(OUT, "10-housekeeping-form.jpg"))

    # 11) Forgot
    page.goto(f"{BASE}/forgot", wait_until="networkidle")
    shot(page, os.path.join(OUT, "11-forgot.jpg"))
    # 12) Reset
    page.goto(f"{BASE}/reset?e={EMAIL}", wait_until="networkidle")
    shot(page, os.path.join(OUT, "12-reset.jpg"))

    ctx.close(); browser.close()
print("done")
