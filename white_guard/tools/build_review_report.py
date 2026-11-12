#!/usr/bin/env python3
"""Build standalone HTML and DOCX from the reviewed repository documents.

Requires python-docx only for the DOCX output. No book text or network calls.
"""
from __future__ import annotations
import argparse
import base64
import html
import json
import re
from pathlib import Path
from docx import Document
from docx.shared import Mm, Pt, RGBColor
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT=Path(__file__).resolve().parents[1]
DOCUMENTS=['ADAPTATION_PLAN.md','TECHNICAL_DESIGN.md','PRODUCTION_PLAN.md','ASSET_REUSE.md','VALIDATION.md']
REPO='https://github.com/masterofmagic234/Kisa/tree/feat/white-guard-foundation/white_guard/'

def plain(text):
    text=re.sub(r'\[([^]]+)\]\([^)]+\)',r'\1',text)
    return text.replace('**','').replace('`','')

def inline(text):
    text=html.escape(text)
    text=re.sub(r'`([^`]+)`',r'<code>\1</code>',text)
    text=re.sub(r'\*\*([^*]+)\*\*',r'<strong>\1</strong>',text)
    return re.sub(r'\[([^]]+)\]\(([^)]+)\)',r'<span>\1</span>',text)

def blocks(text):
    lines=text.splitlines();i=0
    while i<len(lines):
        line=lines[i]
        if not line.strip():i+=1;continue
        if line.startswith('```'):
            lang=line[3:];buf=[];i+=1
            while i<len(lines) and not lines[i].startswith('```'):buf.append(lines[i]);i+=1
            yield ('code',lang,'\n'.join(buf));i+=1;continue
        if line.startswith('|'):
            rows=[]
            while i<len(lines) and lines[i].startswith('|'):
                row=[x.strip() for x in lines[i].strip().strip('|').split('|')]
                if not all(re.fullmatch(r':?-+:?',x) for x in row):rows.append(row)
                i+=1
            yield ('table',rows);continue
        match=re.match(r'^(#{1,6}) (.*)',line)
        if match:yield ('heading',len(match[1]),match[2]);i+=1;continue
        if re.match(r'^[-*] ',line):yield ('bullet',line[2:]);i+=1;continue
        if re.match(r'^\d+\. ',line):yield ('number',line);i+=1;continue
        buf=[line];i+=1
        while i<len(lines) and lines[i].strip() and not re.match(r'^(#|\||```|[-*] |\d+\. )',lines[i]):buf.append(lines[i]);i+=1
        yield ('paragraph',' '.join(buf))

def image_data(path):
    return 'data:image/png;base64,'+base64.b64encode(path.read_bytes()).decode()

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('destination',type=Path)
    args=ap.parse_args();out=args.destination;out.mkdir(parents=True,exist_ok=True)
    doc=Document()
    section=doc.sections[0]
    section.page_width=Mm(210);section.page_height=Mm(297)
    section.top_margin=Mm(18);section.bottom_margin=Mm(18)
    section.left_margin=Mm(19);section.right_margin=Mm(17)
    normal=doc.styles['Normal'];normal.font.name='Calibri';normal.font.size=Pt(11)
    normal.paragraph_format.space_after=Pt(6)
    for name in ['Title','Heading 1','Heading 2','Heading 3']:
        doc.styles[name].font.color.rgb=RGBColor.from_string('294A3B')
    footer=section.footer.paragraphs[0]
    footer.text='Белая гвардия · план и техническая основа · '
    field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');footer._p.append(field)
    doc.add_heading('БЕЛАЯ ГВАРДИЯ',0)
    doc.add_paragraph('Игровая экранизация книги Александра Бушкова',style='Subtitle')
    doc.add_paragraph('План, переносимая основа Arkon Hunter и следующий производственный этап')
    doc.add_paragraph('Статус: подготовка для обсуждения. Сюжетные уровни ещё не реализованы.')
    doc.add_paragraph('9 эпизодов · 11 глав источника · 12 обязательных диалоговых сцен · 58 перенесённых файлов · 88 проверок')
    doc.add_paragraph(REPO)
    doc.add_page_break()
    toc=[];parts=[];heading_number=0
    for filename in DOCUMENTS:
        parts.append('<section class="document">')
        for block in blocks((ROOT/'docs'/filename).read_text()):
            kind=block[0]
            if kind=='heading':
                level,title=block[1:]
                heading_number+=1
                ident='section-'+str(heading_number)
                if level<=2:toc.append((level,ident,plain(title)))
                parts.append(f'<h{level} id="{ident}">{inline(title)}</h{level}>')
                doc.add_heading(plain(title),min(level,3))
            elif kind=='table':
                rows=block[1];parts.append('<table>')
                table=doc.add_table(rows=1,cols=len(rows[0]));table.style='Light Shading Accent 1'
                for j,value in enumerate(rows[0]):table.rows[0].cells[j].text=plain(value)
                header=OxmlElement('w:tblHeader');table.rows[0]._tr.get_or_add_trPr().append(header)
                for k,row in enumerate(rows):
                    tag='th' if k==0 else 'td'
                    parts.append('<tr>'+''.join(f'<{tag}>{inline(value)}</{tag}>' for value in row)+'</tr>')
                    if k:
                        cells=table.add_row().cells
                        for j,value in enumerate(row[:len(cells)]):cells[j].text=plain(value)
                parts.append('</table>')
            elif kind=='code':
                lang,code=block[1:]
                if lang=='mermaid':
                    diagram='Arkon Hunter 1.68 → проверяемое извлечение → Arkon Runtime → режиссёры новой кампании → игровые сцены. Отдельно: данные книги и поведенческие проверки.'
                    parts.append('<div class="architecture">'+html.escape(diagram)+'</div>')
                    doc.add_paragraph(diagram)
                else:
                    parts.append('<pre>'+html.escape(code)+'</pre>')
                    p=doc.add_paragraph(code);p.style=doc.styles['No Spacing']
                    for run in p.runs:run.font.name='Consolas';run.font.size=Pt(8)
            else:
                value=block[1]
                parts.append('<p'+(' class="bullet"' if kind in ['bullet','number'] else '')+'>'+('• ' if kind=='bullet' else '')+inline(value)+'</p>')
                doc.add_paragraph(plain(value),style='List Bullet' if kind=='bullet' else 'Normal')
        parts.append('</section>')
        if filename!=DOCUMENTS[-1]:doc.add_page_break()
    screenshots=[]
    for filename,label in [('planning-room.png','Редакторский просмотр плана'),('runtime-lab.png','Технический стенд — не сюжетный уровень')]:
        p=ROOT/'test-results'/filename
        if p.exists():
            screenshots.append('<figure><img src="'+image_data(p)+'"><figcaption>'+html.escape(label)+' · X11 / GL Compatibility / llvmpipe</figcaption></figure>')
            doc.add_heading(label,2);doc.add_picture(str(p),width=Mm(170))
    font=base64.b64encode((ROOT/'assets/reused/fonts/Manrope-Variable.ttf').read_bytes()).decode()
    title_font=base64.b64encode((ROOT/'assets/reused/fonts/Forum-Regular.ttf').read_bytes()).decode()
    css="""@font-face{font-family:Body;src:url(data:font/ttf;base64,BODYFONT)}@font-face{font-family:Title;src:url(data:font/ttf;base64,TITLEFONT)}
    *{box-sizing:border-box}body{margin:0;color:#23342d;background:#eeeade;font:16px/1.65 Body,Arial,sans-serif}main{max-width:1120px;margin:auto;padding:44px 42px}header{padding:45px 0;border-bottom:3px solid #b49250}header h1{font:64px/1.05 Title,serif;margin:0 0 20px}header p{max-width:850px}.badge{display:inline-block;padding:7px 12px;background:#294a3b;color:#fff;border-radius:3px}.stats{display:flex;gap:12px;flex-wrap:wrap}.stats span{padding:9px 15px;background:#fff8}nav{padding:25px;background:#e4dfd0;margin:30px 0}nav a{color:#294a3b;display:block;text-decoration:none}nav a.sub{padding-left:18px;font-size:14px}.document{padding-top:30px}h1{font:36px Title,serif;border-bottom:1px solid #b49250;padding-bottom:12px}h2{font-size:25px;margin-top:35px}h3{font-size:20px;margin-top:30px}p{margin:12px 0}table{border-collapse:collapse;width:100%;font-size:13px;margin:22px 0}td,th{border:1px solid #bdb7a6;text-align:left;vertical-align:top;padding:8px;overflow-wrap:anywhere}th{background:#294a3b;color:white}tr:nth-child(even){background:#fff6}code{font-family:monospace;font-size:.9em;overflow-wrap:anywhere}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#e2ddce;padding:16px;font-size:12px}.architecture{padding:18px;background:#dce4d9;border-left:4px solid #294a3b}.bullet{padding-left:14px}figure{margin:30px 0}img{max-width:100%;border:1px solid #bbb}figcaption{font-size:13px}.actions{display:flex;gap:12px;margin:20px 0}.actions button{padding:10px 18px;font:inherit;background:#294a3b;color:white;border:0;cursor:pointer}@media(max-width:700px){main{padding:22px 16px}header h1{font-size:45px}table{display:block;overflow-x:auto}}
    @media print{body{background:white;font-size:10pt;line-height:1.45}main{padding:0;max-width:none}header{min-height:210mm;padding-top:35mm;break-after:page}header h1{font-size:43pt}nav,.actions{display:none}.document{padding:0}.document>h1{break-before:page}h1,h2,h3{break-after:avoid}table{font-size:8pt}tr{break-inside:avoid}pre{font-size:8pt}figure{break-inside:avoid}}
    """.replace('BODYFONT',font).replace('TITLEFONT',title_font)
    nav='<nav><strong>Содержание</strong>'+''.join(f'<a class="{"sub" if level==2 else ""}" href="#{ident}">{html.escape(title)}</a>' for level,ident,title in toc)+'</nav>'
    cover='''<header><span class="badge">ПОДГОТОВЛЕНО ДЛЯ ОБСУЖДЕНИЯ</span><h1>Белая гвардия</h1><p>Игровая экранизация книги Александра Бушкова. План кампании, отбор диалогов, переносимая основа Arkon Hunter и производственный маршрут.</p><div class="stats"><span>11 глав → 9 эпизодов</span><span>12 обязательных разговоров</span><span>58 перенесённых файлов</span><span>88 проверок</span></div><p>Сюжетные уровни ещё не созданы. Следующий этап начинается после решения автора проекта.</p><p><a href="'''+REPO+'''">Проект и исходные документы в GitHub</a></p></header><div class="actions"><button onclick="window.print()">Печатать / сохранить PDF</button></div>'''
    document='<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Белая гвардия — план экранизации</title><style>'+css+'</style><main>'+cover+nav+''.join(parts)+''.join(screenshots)+'</main></html>'
    (out/'White-Guard-Review.html').write_text(document)
    doc.save(out/'White-Guard-Review.docx')
    (out/'report-manifest.json').write_text(json.dumps({'documents':DOCUMENTS,'source_release':'v1.68','source_commit':'a1fd971558fcfe8d8fb99e944cc532edee5c32ba','story_approval':'pending','headless_checks':88},ensure_ascii=False,indent=2))
    print(out/'White-Guard-Review.html');print(out/'White-Guard-Review.docx')

if __name__=='__main__':main()
