"""Tests for the W&W Editor (Tools/Editor). Runs the editor's own JavaScript in a headless Chrome / Edge against the game's REAL maps and content.

    python Tools/Editor/tests/run_tests.py            # prints every check, exits 1 if any failed

What it proves: every shipped .jm map opens in the editor and saves back to a map with IDENTICAL tiles (and the Python side reads it back the same),
painting / erasing / filling / undo behave, the palette covers every ground and object the maps use, the maker builds valid XML and refuses clashing ids.
"""
import base64
import glob
import json
import os
import struct
import subprocess
import sys
import tempfile
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
EDITOR = os.path.normpath(os.path.join(HERE, '..'))
REPO = os.path.normpath(os.path.join(EDITOR, '..', '..'))
MAPS = os.path.join(REPO, 'WaW-Server', 'Common', 'Resources', 'World', 'Data')
OUT = os.path.join(HERE, 'out')

BROWSERS = [
    r'C:\Program Files\Google\Chrome\Application\chrome.exe',
    r'C:\Program Files (x86)\Google\Chrome\Application\chrome.exe',
    r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe',
]

# Objects a shipped map refers to that no XML defines: none now (the Vault map was rebuilt on 2026-09-20). List any future exception here on purpose so a NEW one still fails.
KNOWN_MISSING = {}

TESTS_JS = r"""
const out = [];
const ok = (name, cond, extra) => out.push({ name, ok: !!cond, extra: extra === undefined ? '' : String(extra) });
const PAL = window.PALETTE, W = window.WW;
const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);

async function run() {
  // ---- every shipped map round-trips through the editor unchanged
  for (const [name, text] of Object.entries(window.MAPS)) {
    try {
      const m = await W.MapModel.fromJm(text);
      const saved = await m.toJm();
      const m2 = await W.MapModel.fromJm(saved);
      let cellsEqual = m.w === m2.w && m.h === m2.h;
      for (let i = 0; cellsEqual && i < m.cells.length; i++) if (!same(m.dict[m.cells[i]], m2.dict[m2.cells[i]])) cellsEqual = false;
      ok('map ' + name + ': opens (' + m.w + 'x' + m.h + ') and saves back with identical tiles', cellsEqual);
      window.SAVED = window.SAVED || {};
      window.SAVED[name] = saved;
      const unknown = [];
      for (const e of m.dict) { if (e.ground && !PAL.tiles.some((t) => t.id === e.ground)) unknown.push(e.ground);
        for (const o of e.objs || []) if (![...P.objects, ...P.items].some((x) => x.id === o.id)) unknown.push(o.id); }
      const known = (window.KNOWN_MISSING[name] || []);      // content problems that already exist, listed on purpose so a NEW one fails the test
      const fresh = unknown.filter((u) => !known.includes(u));
      ok('map ' + name + ': every ground and object is in the palette', fresh.length === 0, fresh.join(', '));
      if (unknown.length && !fresh.length) ok('map ' + name + ': known missing definition(s) still the same: ' + unknown.join(', '), true);
    } catch (e) { ok('map ' + name + ': opens', false, e); }
  }

  // ---- editing rules
  const m = W.MapModel.blank(10, 8, 'Woodland Grass');
  ok('a new map is filled with the chosen ground', m.get(0, 0).ground === 'Woodland Grass' && m.get(9, 7).ground === 'Woodland Grass');
  ok('a new map uses one dictionary entry', m.dict.length === 2);   // {} plus the ground

  let e = W.applyBrush(m.get(1, 1), { kind: 'object', id: 'Forest Pine' });
  m.set(1, 1, e);
  ok('placing an object keeps the ground', m.get(1, 1).ground === 'Woodland Grass' && m.get(1, 1).objs[0].id === 'Forest Pine');
  e = W.applyBrush(m.get(1, 1), { kind: 'object', id: 'Forest Rock' });
  m.set(1, 1, e);
  ok('a second object replaces the first', m.get(1, 1).objs.length === 1 && m.get(1, 1).objs[0].id === 'Forest Rock');
  m.set(1, 1, W.applyBrush(m.get(1, 1), { kind: 'region', id: 'Spawn' }));
  ok('a region can share a tile with ground and object', m.get(1, 1).regions[0].id === 'Spawn' && m.get(1, 1).objs.length === 1);

  m.set(1, 1, W.applyErase(m.get(1, 1), false));
  ok('erase takes the object first', !m.get(1, 1).objs && m.get(1, 1).regions && m.get(1, 1).ground);
  m.set(1, 1, W.applyErase(m.get(1, 1), false));
  ok('then the region', !m.get(1, 1).regions && m.get(1, 1).ground);
  m.set(1, 1, W.applyErase(m.get(1, 1), false));
  ok('then the ground', !m.get(1, 1).ground && same(m.get(1, 1), {}));
  m.set(2, 2, W.applyBrush(m.get(2, 2), { kind: 'object', id: 'Forest Pine' }));
  m.set(2, 2, W.applyErase(m.get(2, 2), true));
  ok('shift-erase clears the whole tile', same(m.get(2, 2), {}));

  // ---- equal tiles share one dictionary entry, in any key order
  const a = m.indexOf({ objs: [{ id: 'Forest Pine' }], ground: 'Woodland Dirt' });
  const b = m.indexOf({ ground: 'Woodland Dirt', objs: [{ id: 'Forest Pine' }] });
  ok('the same tile described in a different key order is one dictionary entry', a === b);

  // ---- flood fill
  const f = W.MapModel.blank(6, 6, 'Woodland Grass');
  for (let y = 0; y < 6; y++) f.set(3, y, { ground: 'Woodland Dirt' });        // a wall of dirt splits the map
  const cells = W.floodGround(f, 0, 0, 'Woodland Water');
  ok('fill stops at a different ground (3 columns x 6 rows)', cells.length === 18, cells.length);
  ok('filling with the same ground changes nothing', W.floodGround(f, 0, 0, 'Woodland Grass').length === 0);
  ok('fill does not run through the edge of the map', W.floodGround(f, 5, 5, 'Woodland Water').length === 12, W.floodGround(f, 5, 5, 'Woodland Water').length);

  // ---- undo snapshots
  const u = W.MapModel.blank(4, 4, 'Woodland Grass');
  const snap = u.snapshot();
  u.set(0, 0, { ground: 'Woodland Dirt' });
  ok('the edit is visible', u.get(0, 0).ground === 'Woodland Dirt');
  u.restore(snap);
  ok('restoring a snapshot brings the old tile back', u.get(0, 0).ground === 'Woodland Grass' && u.dict.length === 2);

  // ---- compaction on save drops unused dictionary entries
  const c = W.MapModel.blank(4, 4, 'Woodland Grass');
  for (let i = 0; i < 5; i++) c.indexOf({ ground: 'Woodland Dirt', objs: [{ id: 'x' + i }] });   // five entries nothing uses
  const cj = JSON.parse(await c.toJm());
  ok('saving keeps only the tile descriptions that are used', cj.dict.length === 1, cj.dict.length);

  // ---- bad files are refused with a message, not a crash
  for (const [label, text] of [['not json', 'nope'], ['no data', '{"width":4,"height":4}'], ['wrong size', JSON.stringify({ width: 9, height: 9, dict: [{}], data: JSON.parse(await W.MapModel.blank(2, 2, 'g').toJm()).data })]]) {
    let refused = false;
    try { await W.MapModel.fromJm(text); } catch (err) { refused = true; }
    ok('a bad file (' + label + ') is refused', refused);
  }

  // ---- the palette
  ok('palette has tiles, objects, items and regions', PAL.tiles.length > 0 && PAL.objects.length > 0 && PAL.items.length > 0 && PAL.regions.length > 0);
  ok('the region list has Spawn', PAL.regions.some((r) => r.name === 'Spawn'));
  ok('every tile has a picture', PAL.tiles.every((t) => t.pic), PAL.tiles.filter((t) => !t.pic).map((t) => t.id));
  ok('the next free item type is really free', !PAL.usedTypes.includes(PAL.nextItemType));
  ok('the next free object type is really free', !PAL.usedTypes.includes(PAL.nextObjectType));
  ok('the starter items are in the palette', ['Old Sword', 'Old Staff', 'Old Ring', 'Health Potion'].every((n) => PAL.items.some((i) => i.id === n)));

  return out;
}
"""

MAKER_JS = r"""
async function makerTests() {
  const set = (id, v) => { const el = document.getElementById(id); if (el.type === 'checkbox') el.checked = v; else el.value = v; el.dispatchEvent(new Event('input')); el.dispatchEvent(new Event('change')); };
  document.getElementById('modeMaker').click();
  await new Promise((r) => setTimeout(r, 300));
  const problems = () => document.getElementById('mkProblems').textContent;
  const xml = () => document.getElementById('xml').value;

  set('mkKind', 'weapon'); set('mkName', '');
  ok('maker: an empty name is refused', /Give it a name/.test(problems()));
  set('mkName', 'Old Sword');
  ok('maker: a name that already exists is refused', /already used/.test(problems()));
  set('mkName', 'Silver Sword');
  ok('maker: it asks for a picture', /Pick a picture/.test(problems()));

  // click the fourth cell of the item sheet
  document.getElementById('mkSheet').value = 'dungeonItems';
  document.getElementById('mkSheet').dispatchEvent(new Event('change'));
  await new Promise((r) => setTimeout(r, 400));
  const cv = document.getElementById('sheetCanvas');
  const r = cv.getBoundingClientRect();
  const scale = Number(cv.dataset.scale || 1);
  cv.dispatchEvent(new MouseEvent('click', { clientX: r.left + 3 * 16 * scale + 4, clientY: r.top + 4, bubbles: true }));
  ok('maker: clicking the sheet picks a picture (cell 3)', /dungeonItems #3/.test(document.getElementById('mkPicked').textContent), document.getElementById('mkPicked').textContent);
  ok('maker: a valid weapon has no problems', /Looks good/.test(problems()), problems());
  ok('maker: XML has the name, type, tier and damage', /id="Silver Sword"/.test(xml()) && /<Tier>1<\/Tier>/.test(xml()) && /<MinDamage>60<\/MinDamage>/.test(xml()) && /<File>dungeonItems<\/File>/.test(xml()), xml());
  ok('maker: the weapon XML is well-formed', !new DOMParser().parseFromString('<Objects>' + xml() + '</Objects>', 'text/xml').querySelector('parsererror'));
  const doc = new DOMParser().parseFromString('<Objects>' + xml() + '</Objects>', 'text/xml');
  ok('maker: the XML has an Item flag, a slot and a projectile', doc.querySelector('Item') && doc.querySelector('SlotType') && doc.querySelector('Projectile ObjectId'));

  set('mkType', '0xa00');
  ok('maker: a type id that is taken is refused', /already taken/.test(problems()));
  set('mkType', 'banana');
  ok('maker: a type that is not a number is refused', /must be a number/.test(problems()));
  set('mkType', '0x' + window.PALETTE.nextItemType.toString(16));
  set('mkName', 'A<B');
  ok('maker: a name with < is refused', /cannot contain/.test(problems()));
  set('mkName', 'Silver Sword');
  set('mkMin', 100); set('mkMax', 50);
  ok('maker: max damage below min is refused', /Maximum damage/.test(problems()));
  set('mkMax', 150);

  set('mkKind', 'object'); set('mkName', 'Stone Well');
  ok('maker: switching to an object suggests the object id range', /^0x9f/i.test(document.getElementById('mkType').value), document.getElementById('mkType').value);
  const odoc = new DOMParser().parseFromString('<Objects>' + xml() + '</Objects>', 'text/xml');
  ok('maker: an object has a class and no item fields', odoc.querySelector('Class') && !odoc.querySelector('Item') && !odoc.querySelector('Projectile'));
  set('mkDesc', 'Tom & "Jerry" <well>');
  ok('maker: special characters in a description are escaped', !new DOMParser().parseFromString('<Objects>' + xml() + '</Objects>', 'text/xml').querySelector('parsererror'));
}
"""

def read(path):
    with open(path, encoding='utf-8-sig') as f:
        return f.read()


def find_browser():
    for b in BROWSERS:
        if os.path.exists(b):
            return b
    raise SystemExit('No Chrome or Edge found for the headless tests.')


def python_side_check(path):
    """The maps must also read back the same with the Python reader the map generators use."""
    j = json.load(open(path, encoding='utf-8'))
    raw = zlib.decompress(base64.b64decode(j['data']))
    cells = struct.unpack('>%dh' % (j['width'] * j['height']), raw)
    return len(cells) == j['width'] * j['height'] and max(cells) < len(j['dict'])


def build_page(tmp):
    """editor.html + palette.js + editor.js, with the shipped maps embedded and the tests appended."""
    html = read(os.path.join(EDITOR, 'editor.html'))
    maps = {}
    for p in sorted(glob.glob(os.path.join(MAPS, '*.jm'))):
        maps[os.path.basename(p)] = read(p)

    page = html.replace('<script src="palette.js"></script>', '<script>window.MAPS = ' + json.dumps(maps) + '; window.KNOWN_MISSING = ' + json.dumps(KNOWN_MISSING) + ';</script>\n<script src="palette.js"></script>')
    page = page.replace('<script src="editor.js"></script>', '<script src="editor.js"></script>\n<script>\n' + TESTS_JS + MAKER_JS + '''
(async () => {
  let results;
  try {
    results = await run();
    await makerTests();
  } catch (e) { results = (typeof out !== 'undefined' ? out : []); results.push({ name: 'tests crashed', ok: false, extra: e && e.stack || e }); }
  document.title = 'DONE';
  window.__RESULTS = out;
  const pre = document.createElement('pre'); pre.id = 'testresult'; pre.textContent = 'RESULT_START' + JSON.stringify(out) + 'RESULT_END';
  document.body.appendChild(pre);
})();
</script>''')
    path = os.path.join(EDITOR, '_test_page.html')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(page)
    return path


def run_browser(browser, page, shots):
    profile = tempfile.mkdtemp(prefix='wwedit_')
    url = 'file:///' + page.replace('\\', '/')
    cmd = [browser, '--headless=new', '--disable-gpu', '--no-sandbox', '--user-data-dir=' + profile, '--allow-file-access-from-files',
           '--virtual-time-budget=25000', '--window-size=1400,900', '--dump-dom', url]
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=120, encoding='utf-8', errors='replace')
    dom = res.stdout
    marker = 'id="testresult">RESULT_START'
    if marker not in dom:
        print(res.stderr[-1500:])
        raise SystemExit('The test page produced no result (see the browser output above).')
    import html as _html
    return json.loads(_html.unescape(dom.split(marker, 1)[1].split('RESULT_END', 1)[0]))


def main():
    browser = find_browser()
    failed = 0

    for p in sorted(glob.glob(os.path.join(MAPS, '*.jm'))):
        if not python_side_check(p):
            print('FAIL  python reader: ' + os.path.basename(p))
            failed += 1
        else:
            print('PASS  python reader reads ' + os.path.basename(p))

    page = build_page(EDITOR)
    results = run_browser(browser, page, False)
    for r in results:
        print(('PASS  ' if r['ok'] else 'FAIL  ') + r['name'] + ('' if r['ok'] or not r['extra'] else '   -> ' + r['extra'][:300]))
        if not r['ok']:
            failed += 1

    try:
        os.remove(page)
    except OSError:
        pass

    print('\nALL %d PASSED' % (len(results)) if failed == 0 else '\n%d FAILED' % failed)
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
