# -*- coding: utf-8 -*-
"""Manual de usuario de la parte NO autenticada (lo que usa el personal de limpieza).

Genera el mismo manual en español e inglés desde una sola definición de contenido:
los textos viven en STRINGS[lang] y el armado de láminas es común, para que un cambio
de procedimiento se corrija una vez y no dos.

    python build_manual.py

Salida: VacuumControl-Manual-ES.pptx y VacuumControl-Manual-EN.pptx (y sus PDF si se
convierten con LibreOffice, ver README de la carpeta).

Nota: los helpers de dibujo están duplicados respecto de build_deck.py a propósito.
Son scripts de presentación independientes; compartir un módulo obligaría a tocar el
deck ya revisado, y el riesgo de romperlo supera el ahorro de sesenta líneas.
"""
import os
from pptx import Presentation
from pptx.util import Inches, Pt, Emu
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.join(HERE, "mobile")

# --- Paleta (misma del deck) ---
BLUE  = RGBColor(0x15, 0x60, 0xA8)
DARK  = RGBColor(0x0F, 0x1F, 0x30)
INK   = RGBColor(0x1F, 0x2A, 0x37)
GRAY  = RGBColor(0x64, 0x74, 0x8B)
LIGHT = RGBColor(0xEE, 0xF1, 0xF5)
CARD  = RGBColor(0xFF, 0xFF, 0xFF)
GREEN = RGBColor(0x16, 0xA3, 0x4A)
AMBER = RGBColor(0xF5, 0x9E, 0x0B)
RED   = RGBColor(0xEF, 0x44, 0x44)
PALE  = RGBColor(0xC7, 0xD6, 0xE8)


# ---------------- Contenido ----------------
# Cada paso es (numero, texto). Cada FAQ es (pregunta, respuesta).
STRINGS = {
    "ES": {
        "file": "VacuumControl-Manual-ES.pptx",
        "cover_kicker": "MASTERCORP  ·  HOUSEKEEPING",
        "cover_title": "Manual de uso",
        "cover_sub": "Cómo reportar el estado de las aspiradoras",
        "cover_note": ["No necesitas usuario ni contraseña.",
                       "No necesitas instalar ninguna aplicación."],
        "cover_foot": "Personal de limpieza  ·  Guía rápida",

        "s2_kicker": "PARA EMPEZAR",
        "s2_title": "Hay dos códigos QR, y cada uno sirve para algo distinto",
        "s2_cards": [
            (BLUE, "El QR de la aspiradora",
             "Está pegado en cada equipo. Lo usas para decir cómo está esa aspiradora: si funciona bien, si funciona a medias o si no funciona."),
            (AMBER, "El QR de la pared",
             "Está en el cuarto de housekeeping. Lo usas para avisar que no queda ninguna aspiradora disponible para trabajar."),
        ],
        "s2_foot_b": "Los dos se abren igual: ",
        "s2_foot": "apunta la cámara de tu teléfono al código y toca el aviso que aparece.",

        "s3_kicker": "PASO 1",
        "s3_title": "Escanea el QR de la aspiradora",
        "s3_steps": [
            "Abre la cámara de tu teléfono y apúntala al código pegado en la aspiradora.",
            "Toca el aviso que aparece en la pantalla. Se abre solo, sin instalar nada.",
            "Vas a ver el código del equipo, por ejemplo VAC-001. Revisa que sea el mismo de la calcomanía.",
            "Toca el botón azul para continuar.",
        ],
        "s3_tip_b": "Para leer en español: ",
        "s3_tip": "toca la bandera de España, arriba a la derecha. Queda guardado para la próxima vez.",
        "s3_img": "07-equipment-intro.jpg",

        "s4_kicker": "PASO 2",
        "s4_title": "Di cómo está la aspiradora",
        "s4_steps": [
            "Escribe tu nombre. Aparecen sugerencias mientras escribes: elige el tuyo de la lista.",
            "Elige el estado del equipo entre las tres opciones.",
            "Si algo falla, escribe qué le pasa. Mientras más claro lo expliques, más rápido lo arreglan.",
            "Puedes tomar fotos del problema si ayuda a entenderlo.",
            "Toca Enviar. Listo, toma menos de un minuto.",
        ],
        "s4_states": [
            (GREEN, "Operativa", "funciona bien"),
            (AMBER, "A medias", "sirve, pero algo anda mal"),
            (RED, "No funciona", "no se puede usar"),
        ],
        "s4_img": "08-report-form.jpg",

        "s5_kicker": "EL OTRO QR",
        "s5_title": "Cuando no queda ninguna aspiradora disponible",
        "s5_steps": [
            "Ve al QR fijo pegado en la pared del cuarto de housekeeping.",
            "Escanéalo igual que el de una aspiradora.",
            "Toca el botón para continuar al formulario.",
        ],
        "s5_warn_b": "Ojo: ",
        "s5_warn": "este aviso es para decir que NO HAY equipo disponible. Si una aspiradora en particular está fallando, usa el QR pegado en esa aspiradora.",
        "s5_img": "09-housekeeping-intro.jpg",

        "s6_kicker": "PASO 2",
        "s6_title": "Completa el aviso",
        "s6_steps": [
            "Escribe tu nombre y elígelo de las sugerencias.",
            "Deja el tipo de equipo en Aspiradora.",
            "En comentarios, cuenta el detalle: por ejemplo, que no quedan aspiradoras para el turno de la noche.",
            "Toca Enviar.",
        ],
        "s6_note_b": "Los comentarios importan: ",
        "s6_note": "son lo que le permite a tu supervisor entender qué pasó sin tener que preguntarte.",
        "s6_img": "10-housekeeping-form.jpg",

        "s7_kicker": "QUÉ PASA DESPUÉS",
        "s7_title": "Todo lo que reportas se ve en la pantalla de resultados",
        "s7_bullets": [
            "El resumen del día: cuántos equipos están bien, cuántos a medias y cuántos fuera de servicio.",
            "Las aspiradoras más reportadas, para saber cuáles dan más problemas.",
            "El detalle de cada reporte, con quién lo hizo y a qué hora.",
            "El selector de arriba cambia entre hoy, últimos 7 días y últimos 30 días.",
            "Se puede descargar en PDF para imprimir o compartir.",
        ],
        "s7_foot_b": "Además, todos los días a las 10:00 de la mañana ",
        "s7_foot": "sale un correo automático con este mismo resumen para los encargados.",
        "s7_img": "13-public-kpi.jpg",

        "s8_kicker": "DUDAS FRECUENTES",
        "s8_title": "Preguntas que nos hacen seguido",
        "s8_faq": [
            ("¿Necesito usuario y contraseña?",
             "No. Escaneas el código y reportas. Nada más."),
            ("¿Tengo que instalar una aplicación?",
             "No. Se abre en el navegador del teléfono, como cualquier página."),
            ("¿Y si el código no lee o está despegado?",
             "Avísale a tu supervisor para que impriman uno nuevo. No reportes ese equipo con el QR de otro."),
            ("¿Puedo reportar la misma aspiradora dos veces el mismo día?",
             "Sí. Cada reporte queda guardado con su hora, así que no se pierde nada."),
            ("¿Está en español?",
             "Sí. Toca la bandera de España arriba a la derecha y toda la pantalla cambia."),
            ("Me equivoqué al enviar, ¿qué hago?",
             "Vuelve a escanear y manda el reporte correcto. Coméntaselo a tu supervisor para que sepa cuál vale."),
        ],
        "s8_foot": "Si algo no funciona como dice este manual, avísale a tu supervisor.",
    },

    "EN": {
        "file": "VacuumControl-Manual-EN.pptx",
        "cover_kicker": "MASTERCORP  ·  HOUSEKEEPING",
        "cover_title": "User guide",
        "cover_sub": "How to report the status of the vacuums",
        "cover_note": ["You don't need a username or a password.",
                       "You don't need to install any app."],
        "cover_foot": "Housekeeping staff  ·  Quick guide",

        "s2_kicker": "GETTING STARTED",
        "s2_title": "There are two QR codes, and each one is for something different",
        "s2_cards": [
            (BLUE, "The vacuum's QR code",
             "It's stuck on each unit. Use it to say how that vacuum is doing: whether it works fine, works with faults, or doesn't work at all."),
            (AMBER, "The QR code on the wall",
             "It's in the housekeeping room. Use it to report that there are no vacuums left available to work with."),
        ],
        "s2_foot_b": "Both open the same way: ",
        "s2_foot": "point your phone's camera at the code and tap the banner that appears.",

        "s3_kicker": "STEP 1",
        "s3_title": "Scan the vacuum's QR code",
        "s3_steps": [
            "Open your phone's camera and point it at the code stuck on the vacuum.",
            "Tap the banner that appears on screen. It opens on its own, nothing to install.",
            "You'll see the unit's code, for example VAC-001. Check it matches the sticker.",
            "Tap the blue button to continue.",
        ],
        "s3_tip_b": "To read in Spanish: ",
        "s3_tip": "tap the Spanish flag at the top right. It's remembered for next time.",
        "s3_img": "07-equipment-intro.jpg",

        "s4_kicker": "STEP 2",
        "s4_title": "Tell us how the vacuum is doing",
        "s4_steps": [
            "Type your name. Suggestions appear as you type: pick yours from the list.",
            "Choose the unit's status from the three options.",
            "If something is wrong, describe it. The clearer you are, the faster it gets fixed.",
            "You can take photos of the problem if that helps explain it.",
            "Tap Send. That's it — it takes less than a minute.",
        ],
        "s4_states": [
            (GREEN, "Operational", "works fine"),
            (AMBER, "With faults", "usable, but something's wrong"),
            (RED, "Out of service", "cannot be used"),
        ],
        "s4_img": "08-report-form.jpg",

        "s5_kicker": "THE OTHER QR",
        "s5_title": "When there are no vacuums left available",
        "s5_steps": [
            "Go to the fixed QR code on the housekeeping room wall.",
            "Scan it just like a vacuum's code.",
            "Tap the button to continue to the form.",
        ],
        "s5_warn_b": "Careful: ",
        "s5_warn": "this report means there is NO equipment available. If one particular vacuum is faulty, use the QR code stuck on that vacuum instead.",
        "s5_img": "09-housekeeping-intro.jpg",

        "s6_kicker": "STEP 2",
        "s6_title": "Fill in the report",
        "s6_steps": [
            "Type your name and pick it from the suggestions.",
            "Leave the equipment type as Vacuum.",
            "In comments, give the detail: for example, that no vacuums are left for the night shift.",
            "Tap Send.",
        ],
        "s6_note_b": "Comments matter: ",
        "s6_note": "they're what lets your supervisor understand what happened without having to ask you.",
        "s6_img": "10-housekeeping-form.jpg",

        "s7_kicker": "WHAT HAPPENS NEXT",
        "s7_title": "Everything you report shows up on the results screen",
        "s7_bullets": [
            "The day's summary: how many units are fine, how many have faults, how many are out of service.",
            "The most reported vacuums, so you know which ones give the most trouble.",
            "The detail of every report, with who filed it and at what time.",
            "The selector at the top switches between today, the last 7 days and the last 30 days.",
            "It can be downloaded as a PDF to print or share.",
        ],
        "s7_foot_b": "On top of that, every day at 10:00 in the morning ",
        "s7_foot": "an automatic email goes out with this same summary for the managers.",
        "s7_img": "13-public-kpi.jpg",

        "s8_kicker": "COMMON QUESTIONS",
        "s8_title": "Questions we get often",
        "s8_faq": [
            ("Do I need a username and password?",
             "No. You scan the code and report. That's all."),
            ("Do I have to install an app?",
             "No. It opens in your phone's browser, like any web page."),
            ("What if the code won't scan or has come off?",
             "Tell your supervisor so a new one gets printed. Don't report that unit using another one's QR code."),
            ("Can I report the same vacuum twice in one day?",
             "Yes. Every report is saved with its own time, so nothing is lost."),
            ("Is it available in Spanish?",
             "Yes. Tap the Spanish flag at the top right and the whole screen changes."),
            ("I sent it by mistake — what now?",
             "Scan again and send the correct report. Let your supervisor know which one counts."),
        ],
        "s8_foot": "If anything doesn't work the way this guide says, tell your supervisor.",
    },
}


# ---------------- Helpers de dibujo ----------------
def build(lang):
    L = STRINGS[lang]
    prs = Presentation()
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)
    SW, SH = prs.slide_width, prs.slide_height
    BLANK = prs.slide_layouts[6]

    def slide(): return prs.slides.add_slide(BLANK)

    def bg(s, color):
        s.background.fill.solid()
        s.background.fill.fore_color.rgb = color

    def rect(s, x, y, w, h, color, line=None):
        shp = s.shapes.add_shape(MSO_SHAPE.RECTANGLE, x, y, w, h)
        shp.fill.solid(); shp.fill.fore_color.rgb = color
        if line is None: shp.line.fill.background()
        else: shp.line.color.rgb = line; shp.line.width = Pt(1)
        shp.shadow.inherit = False
        return shp

    def rrect(s, x, y, w, h, color, line=None):
        shp = s.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, x, y, w, h)
        try: shp.adjustments[0] = 0.06
        except Exception: pass
        shp.fill.solid(); shp.fill.fore_color.rgb = color
        if line is None: shp.line.fill.background()
        else: shp.line.color.rgb = line; shp.line.width = Pt(1)
        shp.shadow.inherit = False
        return shp

    def text(s, x, y, w, h, runs, align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, space=1.0):
        tb = s.shapes.add_textbox(x, y, w, h); tf = tb.text_frame
        tf.word_wrap = True; tf.vertical_anchor = anchor
        tf.margin_left = tf.margin_right = Inches(0.05)
        tf.margin_top = tf.margin_bottom = Inches(0.03)
        for i, para in enumerate(runs):
            p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
            p.alignment = align; p.line_spacing = space; p.space_after = Pt(4)
            for (txt, size, color, bold) in para:
                r = p.add_run(); r.text = txt
                r.font.size = Pt(size); r.font.color.rgb = color; r.font.bold = bold
                r.font.name = "Segoe UI"
        return tb

    def phone(s, name, box_x, box_y, box_w, box_h):
        """Coloca la captura del teléfono (contain) centrada, con marco y sombra."""
        path = os.path.join(SHOTS, name)
        with Image.open(path) as im:
            iw, ih = im.size
        ar = iw / ih
        h = box_h; w = int(box_h * ar)
        if w > box_w:
            w = box_w; h = int(box_w / ar)
        x = box_x + (box_w - w) // 2
        y = box_y + (box_h - h) // 2
        rect(s, x + Emu(20000), y + Emu(20000), w, h, RGBColor(0xD5, 0xDB, 0xE3))
        pic = s.shapes.add_picture(path, x, y, w, h)
        pic.line.color.rgb = RGBColor(0xE5, 0xE9, 0xF0); pic.line.width = Pt(1)
        return pic

    def header(s, kicker, title):
        rect(s, 0, 0, SW, Inches(0.12), BLUE)
        text(s, Inches(0.6), Inches(0.32), Inches(12), Inches(0.4), [[(kicker, 12, BLUE, True)]])
        text(s, Inches(0.6), Inches(0.60), Inches(11.0), Inches(0.85), [[(title, 26, DARK, True)]])

    def numbered(s, steps, x, y, w, gap=0.86):
        """Lista de pasos con círculo numerado azul."""
        for i, txt in enumerate(steps):
            cy = y + Inches(gap * i)
            circ = s.shapes.add_shape(MSO_SHAPE.OVAL, x, cy, Inches(0.42), Inches(0.42))
            circ.fill.solid(); circ.fill.fore_color.rgb = BLUE
            circ.line.fill.background(); circ.shadow.inherit = False
            tf = circ.text_frame; tf.word_wrap = False
            tf.margin_top = tf.margin_bottom = 0
            p = tf.paragraphs[0]; p.alignment = PP_ALIGN.CENTER
            r = p.add_run(); r.text = str(i + 1)
            r.font.size = Pt(14); r.font.bold = True; r.font.color.rgb = CARD
            r.font.name = "Segoe UI"
            text(s, x + Inches(0.62), cy - Inches(0.04), w, Inches(0.85),
                 [[(txt, 14, INK, False)]], space=1.12)

    def note(s, x, y, w, bold_txt, txt, color=BLUE, fill=RGBColor(0xEA, 0xF1, 0xF9)):
        rrect(s, x, y, w, Inches(0.95), fill)
        rect(s, x, y, Inches(0.07), Inches(0.95), color)
        text(s, x + Inches(0.28), y + Inches(0.14), w - Inches(0.5), Inches(0.7),
             [[(bold_txt, 13.5, color, True), (txt, 13.5, INK, False)]], space=1.1)

    IMG_X, IMG_Y, IMG_W, IMG_H = Inches(9.55), Inches(1.55), Inches(3.3), Inches(5.6)

    # ============ 1. PORTADA ============
    s = slide(); bg(s, DARK)
    rect(s, 0, 0, SW, SH, DARK)
    rect(s, 0, Inches(6.9), SW, Inches(0.6), BLUE)
    text(s, Inches(0.9), Inches(1.6), Inches(11.5), Inches(0.5),
         [[(L["cover_kicker"], 14, RGBColor(0x8F, 0xB4, 0xDA), True)]])
    text(s, Inches(0.9), Inches(2.2), Inches(11.5), Inches(1.7),
         [[(L["cover_title"], 54, CARD, True)],
          [(L["cover_sub"], 25, PALE, False)]])
    text(s, Inches(0.9), Inches(4.5), Inches(11.5), Inches(1.4),
         [[(n, 17, RGBColor(0xB6, 0xC6, 0xDA), False)] for n in L["cover_note"]], space=1.25)
    text(s, Inches(0.9), Inches(6.98), Inches(11.5), Inches(0.45),
         [[(L["cover_foot"], 13, CARD, True)]], anchor=MSO_ANCHOR.MIDDLE)

    # ============ 2. LOS DOS QR ============
    s = slide(); bg(s, LIGHT); header(s, L["s2_kicker"], L["s2_title"])
    x = Inches(0.6)
    for (color, t, d) in L["s2_cards"]:
        rrect(s, x, Inches(1.95), Inches(6.0), Inches(3.4), CARD)
        rect(s, x, Inches(1.95), Inches(6.0), Inches(0.14), color)
        text(s, x + Inches(0.35), Inches(2.35), Inches(5.3), Inches(0.6), [[(t, 20, INK, True)]])
        text(s, x + Inches(0.35), Inches(3.05), Inches(5.3), Inches(2.0),
             [[(d, 15, GRAY, False)]], space=1.15)
        x += Inches(6.35)
    note(s, Inches(0.6), Inches(5.7), Inches(12.1), L["s2_foot_b"], L["s2_foot"])

    # ============ 3. ESCANEAR EL QR DEL EQUIPO ============
    s = slide(); bg(s, LIGHT); header(s, L["s3_kicker"], L["s3_title"])
    numbered(s, L["s3_steps"], Inches(0.7), Inches(1.75), Inches(8.0))
    note(s, Inches(0.7), Inches(5.55), Inches(8.5), L["s3_tip_b"], L["s3_tip"])
    phone(s, L["s3_img"], IMG_X, IMG_Y, IMG_W, IMG_H)

    # ============ 4. REPORTAR EL ESTADO ============
    s = slide(); bg(s, LIGHT); header(s, L["s4_kicker"], L["s4_title"])
    numbered(s, L["s4_steps"], Inches(0.7), Inches(1.7), Inches(8.0), gap=0.72)
    y = Inches(5.45)
    x = Inches(0.7)
    for (color, t, d) in L["s4_states"]:
        rrect(s, x, y, Inches(2.75), Inches(1.15), CARD)
        rect(s, x, y, Inches(2.75), Inches(0.1), color)
        text(s, x + Inches(0.2), y + Inches(0.24), Inches(2.4), Inches(0.4), [[(t, 15, color, True)]])
        text(s, x + Inches(0.2), y + Inches(0.62), Inches(2.4), Inches(0.4), [[(d, 12, GRAY, False)]])
        x += Inches(2.92)
    phone(s, L["s4_img"], IMG_X, IMG_Y, IMG_W, IMG_H)

    # ============ 5. EL QR FIJO DEL CUARTO ============
    s = slide(); bg(s, LIGHT); header(s, L["s5_kicker"], L["s5_title"])
    numbered(s, L["s5_steps"], Inches(0.7), Inches(1.9), Inches(8.0))
    note(s, Inches(0.7), Inches(4.9), Inches(8.5), L["s5_warn_b"], L["s5_warn"],
         color=AMBER, fill=RGBColor(0xFE, 0xF6, 0xE7))
    phone(s, L["s5_img"], IMG_X, IMG_Y, IMG_W, IMG_H)

    # ============ 6. COMPLETAR EL AVISO ============
    s = slide(); bg(s, LIGHT); header(s, L["s6_kicker"], L["s6_title"])
    numbered(s, L["s6_steps"], Inches(0.7), Inches(1.9), Inches(8.0))
    note(s, Inches(0.7), Inches(5.4), Inches(8.5), L["s6_note_b"], L["s6_note"])
    phone(s, L["s6_img"], IMG_X, IMG_Y, IMG_W, IMG_H)

    # ============ 7. LA PANTALLA DE RESULTADOS ============
    s = slide(); bg(s, LIGHT); header(s, L["s7_kicker"], L["s7_title"])
    y = Inches(1.8)
    for b in L["s7_bullets"]:
        dot = s.shapes.add_shape(MSO_SHAPE.OVAL, Inches(0.75), y + Inches(0.12),
                                 Inches(0.15), Inches(0.15))
        dot.fill.solid(); dot.fill.fore_color.rgb = BLUE
        dot.line.fill.background(); dot.shadow.inherit = False
        text(s, Inches(1.1), y, Inches(7.9), Inches(0.8), [[(b, 14.5, INK, False)]], space=1.12)
        y += Inches(0.78)
    note(s, Inches(0.7), Inches(5.85), Inches(8.5), L["s7_foot_b"], L["s7_foot"])
    phone(s, L["s7_img"], IMG_X, IMG_Y, IMG_W, IMG_H)

    # ============ 8. PREGUNTAS FRECUENTES ============
    s = slide(); bg(s, LIGHT); header(s, L["s8_kicker"], L["s8_title"])
    col_x = [Inches(0.6), Inches(6.85)]
    for i, (q, a) in enumerate(L["s8_faq"]):
        cx = col_x[i % 2]
        cy = Inches(1.85) + Inches(1.62) * (i // 2)
        rrect(s, cx, cy, Inches(5.9), Inches(1.42), CARD)
        text(s, cx + Inches(0.28), cy + Inches(0.18), Inches(5.35), Inches(0.5),
             [[(q, 14.5, BLUE, True)]], space=1.05)
        text(s, cx + Inches(0.28), cy + Inches(0.62), Inches(5.35), Inches(0.7),
             [[(a, 13, GRAY, False)]], space=1.1)
    text(s, Inches(0.6), Inches(6.85), Inches(12.1), Inches(0.5),
         [[(L["s8_foot"], 14, INK, True)]], align=PP_ALIGN.CENTER)

    out = os.path.join(HERE, L["file"])
    prs.save(out)
    print("  saved", L["file"])
    return out


if __name__ == "__main__":
    for lang in ("ES", "EN"):
        print(f"building {lang}…")
        build(lang)
    print("done")
