"""Builds the static data The Portal needs from the game's own XML and art sheets:

    Portal/site/data/items.json     every item: type, name, tier, slot, description, stat bonuses, damage, icon
    Portal/site/data/classes.json   every class: type, name, description, starting/max stats, slot types, icon
    Portal/site/icons/items/<hex>.png     16x16 item icons scaled x3 (crisp, nearest neighbour)
    Portal/site/icons/classes/<hex>.png   32x32 class portraits (first idle frame) scaled x3

Run from the repo root (deploy -Portal does it):  python Tools/Portal/build_portal_data.py
Only reads Content/Xmls/*.xml, Content/Game.atlas and Content/Sheets/*.png. Never edits art.
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CONTENT = os.path.join(ROOT, 'WaW-Client', 'WaWClient', 'Content')
SITE = os.path.join(ROOT, 'Portal', 'site')
SCALE = 3

# RotMG-style slot ids used by Players.xml <SlotTypes> and Equip.xml <SlotType>
SLOT_NAMES = {
    0: 'Any', 1: 'Sword', 2: 'Dagger', 3: 'Bow', 4: 'Tome', 5: 'Shield', 6: 'Leather Armor', 7: 'Armor', 8: 'Wand', 9: 'Ring',
    10: 'Potion', 11: 'Spell', 12: 'Seal', 13: 'Cloak', 14: 'Robe', 15: 'Quiver', 16: 'Helmet', 17: 'Staff', 18: 'Poison', 19: 'Skull',
    20: 'Trap', 21: 'Orb', 22: 'Prism', 23: 'Scepter', 24: 'Katana', 25: 'Shuriken', 26: 'Consumable',
}
STAT_NAMES = {0: 'HP', 3: 'MP', 20: 'ATT', 21: 'DEF', 22: 'SPD', 23: 'VIT', 24: 'WIS', 25: 'DEX'}


def parse_int(text, default=0):
    if text is None:
        return default
    text = text.strip()
    try:
        return int(text, 16) if text.lower().startswith('0x') else int(text)
    except ValueError:
        return default


def load_atlas():
    """name -> (png file, cell w, cell h, animated)"""
    sheets = {}
    tree = ET.parse(os.path.join(CONTENT, 'Game.atlas'))
    for e in tree.getroot().iter():
        if e.tag in ('Image', 'Animated') and e.get('name'):
            sheets[e.get('name')] = (e.text.strip(), int(e.get('w')), int(e.get('h')), e.tag == 'Animated')
    return sheets


_sheet_cache = {}


def sheet_image(png):
    if png not in _sheet_cache:
        _sheet_cache[png] = Image.open(os.path.join(CONTENT, 'Sheets', png)).convert('RGBA')
    return _sheet_cache[png]


def crop_cell(sheets, name, index, animated_rows=3):
    if name not in sheets:
        return None
    png, w, h, animated = sheets[name]
    img = sheet_image(png)
    cols = img.width // w
    if animated:
        x, y = 0, index * animated_rows * h          # first frame of the character's block (3 rows per character)
    else:
        x, y = (index % cols) * w, (index // cols) * h
    if y + h > img.height or x + w > img.width:
        return None
    cell = img.crop((x, y, x + w, y + h))
    return cell.resize((w * SCALE, h * SCALE), Image.NEAREST)


def texture_of(obj):
    t = obj.find('Texture')
    if t is None:
        t = obj.find('AnimatedTexture')
    if t is None:
        return None, None
    return (t.findtext('File') or '').strip(), parse_int(t.findtext('Index'))


def build_items(sheets):
    items = []
    for xml in sorted(os.listdir(os.path.join(CONTENT, 'Xmls'))):
        if not xml.endswith('.xml'):
            continue
        root = ET.parse(os.path.join(CONTENT, 'Xmls', xml)).getroot()
        for obj in root.iter('Object'):
            if obj.find('Item') is None:
                continue
            type_id = parse_int(obj.get('type'))
            name = obj.get('id') or ''
            slot = parse_int(obj.findtext('SlotType'))
            boosts = []
            for a in obj.findall('ActivateOnEquip'):
                if (a.text or '').strip() == 'IncrementStat':
                    boosts.append({'stat': STAT_NAMES.get(parse_int(a.get('stat')), 'stat ' + (a.get('stat') or '?')), 'amount': parse_int(a.get('amount'))})
            proj = obj.find('Projectile')
            damage = None
            if proj is not None:
                lo, hi = proj.findtext('MinDamage'), proj.findtext('MaxDamage')
                if lo is None and hi is None and proj.findtext('Damage'):
                    lo = hi = proj.findtext('Damage')
                if lo is not None or hi is not None:
                    damage = [parse_int(lo), parse_int(hi)]
            sheet, index = texture_of(obj)
            icon = None
            if sheet:
                cell = crop_cell(sheets, sheet, index)
                if cell is not None:
                    icon = f'items/{type_id:04x}.png'
                    cell.save(os.path.join(SITE, 'icons', icon))
            items.append({
                'type': type_id, 'name': name, 'tier': parse_int(obj.findtext('Tier'), -1) if obj.find('Tier') is not None else None,
                'slot': slot, 'slotName': SLOT_NAMES.get(slot, f'Slot {slot}'),
                'description': (obj.findtext('Description') or '').strip(),
                'boosts': boosts, 'damage': damage,
                'rateOfFire': float(proj_rate) if (proj_rate := obj.findtext('RateOfFire')) else None,
                'numProjectiles': parse_int(obj.findtext('NumProjectiles')) if obj.find('NumProjectiles') is not None else None,
                'consumable': obj.find('Consumable') is not None or obj.find('Potion') is not None,
                'soulbound': obj.find('Soulbound') is not None,
                'icon': icon, 'source': xml,
            })
    items.sort(key=lambda i: (i['slot'], i['tier'] if i['tier'] is not None else -1, i['name']))
    return items


def build_classes(sheets):
    classes = []
    root = ET.parse(os.path.join(CONTENT, 'Xmls', 'Players.xml')).getroot()
    for obj in root.iter('Object'):
        if obj.find('Player') is None:
            continue
        type_id = parse_int(obj.get('type'))
        stats = {}
        for tag, key in [('MaxHitPoints', 'HP'), ('MaxMagicPoints', 'MP'), ('Attack', 'ATT'), ('Defense', 'DEF'), ('Speed', 'SPD'),
                         ('Dexterity', 'DEX'), ('HpRegen', 'VIT'), ('MpRegen', 'WIS')]:
            e = obj.find(tag)
            if e is not None:
                stats[key] = {'start': parse_int(e.text), 'max': parse_int(e.get('max'))}
        slots = [parse_int(x) for x in (obj.findtext('SlotTypes') or '').split(',') if x.strip()]
        equipment = [parse_int(x) for x in (obj.findtext('Equipment') or '').split(',') if x.strip()]
        sheet, index = texture_of(obj)
        icon = None
        if sheet:
            cell = crop_cell(sheets, sheet, index)
            if cell is not None:
                icon = f'classes/{type_id:04x}.png'
                cell.save(os.path.join(SITE, 'icons', icon))
        classes.append({
            'type': type_id, 'name': obj.get('id'), 'description': (obj.findtext('Description') or '').strip(),
            'stats': stats, 'slots': slots[:4], 'slotNames': [SLOT_NAMES.get(s, f'Slot {s}') for s in slots[:4]],
            'startingEquipment': equipment[:4], 'icon': icon,
        })
    return classes


def main():
    for sub in ('data', 'icons/items', 'icons/classes'):
        os.makedirs(os.path.join(SITE, sub), exist_ok=True)
    sheets = load_atlas()
    items = build_items(sheets)
    classes = build_classes(sheets)
    with open(os.path.join(SITE, 'data', 'items.json'), 'w', encoding='utf-8') as f:
        json.dump(items, f, indent=1)
    with open(os.path.join(SITE, 'data', 'classes.json'), 'w', encoding='utf-8') as f:
        json.dump(classes, f, indent=1)
    version = re.search(r'BuildVersion\s*=\s*"([^"]+)"', open(os.path.join(ROOT, 'WaW-Client', 'WaWClient', 'Core', 'Settings.cs'), encoding='utf-8-sig').read())
    with open(os.path.join(SITE, 'data', 'build.json'), 'w', encoding='utf-8') as f:
        json.dump({'gameVersion': version.group(1) if version else '', 'items': len(items), 'classes': len(classes)}, f)
    print(f'portal data: {len(items)} items, {len(classes)} classes, icons in {os.path.join(SITE, "icons")}')


if __name__ == '__main__':
    sys.exit(main())
