# -*- coding: utf-8 -*-
"""Captura las páginas PÚBLICAS en viewport móvil (emulación iPhone) con Playwright.

A diferencia de shoot_mobile.py, este script NO necesita credenciales: solo recorre
rutas accesibles sin sesión. Se usa para el manual de usuario de la parte no autenticada.
"""
import os
from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "mobile"); os.makedirs(OUT, exist_ok=True)
BASE = os.environ.get("DEMO_BASE", "https://mastercorp.valentisoft.com")
SITE = os.environ.get("DEMO_SITE", "XjUS3")

IPHONE = {
    "viewport": {"width": 390, "height": 844},
    "device_scale_factor": 3,
    "is_mobile": True,
    "has_touch": True,
    "user_agent": ("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) "
                   "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"),
}

def shot(page, name, full=False):
    page.wait_for_timeout(900)  # deja asentar las gráficas
    path = os.path.join(OUT, name)
    page.screenshot(path=path, full_page=full)
    print("  saved", name)

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True)
    ctx = browser.new_context(ignore_https_errors=True, **IPHONE)
    page = ctx.new_page()

    # Dashboard público de KPIs del site, tal como abre por defecto (vista diaria).
    print(f"KPIs públicos de {SITE}…")
    page.goto(f"{BASE}/{SITE}/reports", wait_until="networkidle")
    shot(page, "13-public-kpi.jpg")

    ctx.close(); browser.close()
print("done")
