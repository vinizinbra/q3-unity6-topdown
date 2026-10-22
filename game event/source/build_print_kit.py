"""Run with Python 3 + reportlab + Pillow. Outputs are RGB printer-review PDFs."""
from pathlib import Path
import json
from PIL import Image
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.utils import ImageReader

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'print'
ART = ROOT / 'source/bunny-bomber.png'
FONT = '/System/Library/Fonts/Supplemental/Arial Black.ttf'
pdfmetrics.registerFont(TTFont('Display', FONT))
pdfmetrics.registerFont(TTFont('Body', '/System/Library/Fonts/Supplemental/Arial.ttf'))
MM = 72 / 25.4
PINK = '#FF1687'
CYAN = '#00CBE5'
INK = '#080B18'
manifest = []

def text(c, s, x, y, size, color=INK, font='Display'):
    c.setFillColor(color); c.setFont(font, size); c.drawCentredString(x, y, s)

def shard(c, points, color):
    c.setFillColor(color); p = c.beginPath(); p.moveTo(*points[0])
    for pt in points[1:]: p.lineTo(*pt)
    p.close(); c.drawPath(p, fill=1, stroke=0)

def decor(c, w, h):
    shard(c, [(0,h*.84),(w*.18,h),(w*.08,h*.77)], PINK)
    shard(c, [(w,h*.23),(w*.86,h*.10),(w*.96,h*.37)], CYAN)
    shard(c, [(0,h*.08),(w*.13,0),(w*.23,0)], CYAN)

def brand(c, w, y, size):
    text(c, 'RIFT', w/2, y, size)
    text(c, 'JUMPERS', w/2, y-size*.9, size*.67)

def art(c, x, y, w, h):
    im = Image.open(ART)
    scale = min(w/im.width,h/im.height)
    dw,dh=im.width*scale,im.height*scale
    c.drawImage(ImageReader(im),x+(w-dw)/2,y+(h-dh)/2,dw,dh,mask='auto')

def make(name, width, height, draw, bleed=3, note=''):
    w,h = width*MM,height*MM; b=bleed*MM
    c=canvas.Canvas(str(OUT/(name+'.pdf')),pagesize=(w+2*b,h+2*b))
    c.setTitle('Rift Jumpers / '+name)
    c.setTrimBox((b,b,w+b,h+b)); c.setBleedBox((0,0,w+2*b,h+2*b))
    c.setFillColorRGB(1,1,1); c.rect(0,0,w+2*b,h+2*b,fill=1,stroke=0)
    c.translate(b,b); draw(c,w,h); c.showPage(); c.save()
    manifest.append(dict(file=name+'.pdf',trim_mm=[width,height],bleed_mm=bleed,status=note or 'printer-review RGB PDF'))

def backboard(c,w,h):
    decor(c,w,h); brand(c,w,h*.86,w*.16)
    text(c,'JUMP IN. BREAK WORLDS.',w/2,h*.67,w*.039,PINK)
    text(c,'CO-OP ROGUELITE SHOOTER',w/2,h*.625,w*.028)
    art(c,w*.12,h*.04,w*.76,h*.55)

def play(c,w,h):
    decor(c,w,h); text(c,'PLAY',w/2,h*.85,w*.19)
    brand(c,w,h*.72,w*.115)
    art(c,w*.18,h*.22,w*.64,h*.39)
    text(c,'JUMP IN!',w/2,h*.13,w*.12,PINK)
    text(c,'ASK US FOR A DEMO',w/2,h*.065,w*.037)

def take(c,w,h):
    decor(c,w,h); text(c,'TAKE A',w/2,h*.83,w*.13)
    text(c,'STICKER!',w/2,h*.70,w*.13,PINK)
    art(c,w*.2,h*.20,w*.6,h*.44)
    text(c,'RIFT JUMPERS',w/2,h*.09,w*.065)

def runner(c,w,h):
    # Only bottom 650 mm contains branding. Confirm table drop before manufacturing.
    front=650*MM
    decor(c,w,front); brand(c,w,front*.74,w*.21)
    text(c,'JUMP IN.',w/2,front*.33,w*.12,PINK)
    text(c,'BREAK WORLDS.',w/2,front*.23,w*.075)

def cardfront(c,w,h):
    decor(c,w,h); brand(c,w,h*.86,w*.16)
    art(c,w*.12,h*.13,w*.76,h*.58)
    text(c,'JUMP IN. BREAK WORLDS.',w/2,h*.06,w*.035,PINK)

def cardback(c,w,h):
    decor(c,w,h); brand(c,w,h*.80,w*.16)
    text(c,'CO-OP',w/2,h*.48,w*.12,PINK)
    text(c,'ROGUELITE SHOOTER',w/2,h*.38,w*.043)
    text(c,'THANKS FOR PLAYING!',w/2,h*.17,w*.045)
    text(c,'RIFT JUMPERS',w/2,h*.09,w*.045)

def standee(c,w,h):
    # Rectangular panel intentionally; contour cutting requires supplier dieline.
    art(c,w*.04,h*.05,w*.92,h*.91)
    text(c,'RIFT JUMPERS',w/2,h*.025,w*.055)

def sticker(c,w,h):
    art(c,w*.05,h*.05,w*.90,h*.90)

OUT.mkdir(exist_ok=True)
make('01-backboard-600x800mm',600,800,backboard,note='Confirm artwork resolution and stable mounting; rectangular trim')
make('02-play-sign-A4',210,297,play)
make('03-take-a-sticker-A5',148,210,take)
make('04-table-runner-500x1800mm-DRAFT',500,1800,runner,0,'DRAFT: supplier template, hems and table dimensions required')
make('05-character-card-front-55x85mm',55,85,cardfront)
make('06-character-card-back-55x85mm',55,85,cardback)
make('07-standee-panel-300x450mm',300,450,standee,note='Rectangular print panel; supplier must add rear easel; no contour dieline')
make('08-sticker-art-70x90mm',70,90,sticker,note='Artwork only; printer must create and approve white-border contour dieline')
(ROOT/'print/manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('Built',len(manifest),'PDFs')
