/* The Portal - one script for every page. Which page runs is chosen by <body data-page="...">.
   Data comes from two places: the static files data/items.json + data/classes.json (built by Tools/Portal/build_portal_data.py
   from the game's own XML) and the read-only API /api/public/... (the account server, proxied by nginx). Nothing here can change
   anything on the server. */
(function () {
  'use strict';

  const API = '/api/public';
  const PRETTY = location.protocol !== 'file:' && !/^(localhost|127\.)/.test(location.hostname);   // nginx rewrites /player/Name -> player.html?name=Name; locally use the query form
  const $ = (sel, root) => (root || document).querySelector(sel);
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const num = n => Number(n ?? 0).toLocaleString('en-US');
  const hex = n => (n >>> 0).toString(16).padStart(4, '0');
  const STATS = ['hp', 'mp', 'att', 'def', 'spd', 'dex', 'vit', 'wis'];
  const STAT_KEYS = { hp: 'HP', mp: 'MP', att: 'ATT', def: 'DEF', spd: 'SPD', dex: 'DEX', vit: 'VIT', wis: 'WIS' };

  const url = {
    player: name => PRETTY ? '/player/' + encodeURIComponent(name) : 'player.html?name=' + encodeURIComponent(name),
    guild: name => PRETTY ? '/guild/' + encodeURIComponent(name) : 'guild.html?name=' + encodeURIComponent(name),
    top: kind => PRETTY ? '/top/' + kind : 'leaderboards.html?kind=' + kind,
    items: () => PRETTY ? '/wiki/items' : 'items.html',
    item: type => PRETTY ? '/wiki/item/' + hex(type) : 'items.html?type=' + hex(type),
    classes: () => PRETTY ? '/wiki/classes' : 'classes.html',
    klass: type => PRETTY ? '/wiki/class/' + hex(type) : 'classes.html?type=' + hex(type),
    graveyard: () => PRETTY ? '/graveyard' : 'graveyard.html',
    releases: () => PRETTY ? '/releases.html' : 'releases.html',   // a plain page: works on the VPS with no nginx change
    home: () => PRETTY ? '/' : 'index.html',
  };

  // The page's argument: the last path segment on nginx (/player/Name) or the query string (player.html?name=Name).
  function arg(key) {
    const q = new URLSearchParams(location.search).get(key);
    if (q) return q;
    const parts = location.pathname.split('/').filter(Boolean);
    if (parts.length >= 2 && !parts[parts.length - 1].endsWith('.html')) return decodeURIComponent(parts[parts.length - 1]);
    return '';
  }

  async function api(path) {
    const r = await fetch(API + path, { headers: { Accept: 'application/json' } });
    if (!r.ok) throw new Error('The Portal API answered ' + r.status);
    const j = await r.json();
    if (j && j.error) throw new Error(j.error);
    return j;
  }

  const data = { items: null, classes: null, byType: new Map(), classByType: new Map(), starGoals: [] };
  async function loadData() {
    if (data.items) return data;
    const base = PRETTY ? '/' : '';
    const [items, classes] = await Promise.all([fetch(base + 'data/items.json').then(r => r.json()), fetch(base + 'data/classes.json').then(r => r.json())]);
    data.items = items; data.classes = classes;
    items.forEach(i => data.byType.set(i.type, i));
    classes.forEach(c => data.classByType.set(c.type, c));
    return data;
  }
  const iconPath = p => (PRETTY ? '/icons/' : 'icons/') + p;
  const maxStars = () => (data.classes ? data.classes.length : 0) * (data.starGoals ? data.starGoals.length : 0);

  // ---- shared chrome ------------------------------------------------------------------------------------------------------
  function chrome(page) {
    const nav = `
<nav class="site-nav" aria-label="Site">
  <a class="nav-brand" href="${url.home()}"><img src="${PRETTY ? '/' : ''}assets/favicon-32.png" alt="" width="20" height="20"> The Portal <small>W&amp;W</small></a>
  <div class="nav-links">
    <a href="${url.top('fame')}" class="nav-link ${page === 'top' ? 'is-current' : ''}">Top Players</a>
    <a href="${url.top('guilds')}" class="nav-link ${page === 'guilds' ? 'is-current' : ''}">Guilds</a>
    <a href="${url.items()}" class="nav-link ${page === 'items' ? 'is-current' : ''}">Items</a>
    <a href="${url.classes()}" class="nav-link ${page === 'classes' ? 'is-current' : ''}">Classes</a>
    <a href="${url.graveyard()}" class="nav-link ${page === 'graveyard' ? 'is-current' : ''}">Graveyard</a>
    <a href="${url.releases()}" class="nav-link ${page === 'releases' ? 'is-current' : ''}">Releases</a>
    <a href="https://warriorsandwizards.com" class="nav-link">Home</a>
    <a href="https://play.warriorsandwizards.com" class="nav-link nav-play">Play</a>
    <a href="https://forums.warriorsandwizards.com" class="nav-link">Forums</a>
  </div>
  <form class="nav-search" id="nav-search" autocomplete="off">
    <input type="search" name="q" placeholder="Player name" aria-label="Find a player" maxlength="16">
    <div class="suggest"></div>
  </form>
</nav>`;
    document.body.insertAdjacentHTML('afterbegin', '<div class="embers"></div>' + nav);
    document.body.insertAdjacentHTML('beforeend', `<div class="foot">The Portal is the public record of <a href="https://warriorsandwizards.com">Warriors &amp; Wizards</a>.
      The game is in open beta. Profiles update within a minute of the game saving. <a href="https://forums.warriorsandwizards.com">Forums</a> &middot; <a href="https://play.warriorsandwizards.com">Play in the browser</a></div>`);
    search($('#nav-search'));
  }

  // A search box with suggestions from /public/search. Enter goes to the exact name typed.
  function search(form) {
    const input = $('input', form), box = $('.suggest', form);
    let timer = 0, active = -1, names = [];
    const show = list => {
      names = list; active = -1;
      box.innerHTML = list.map(n => `<a href="${url.player(n)}">${esc(n)}</a>`).join('');
      box.style.display = list.length ? 'block' : 'none';
    };
    input.addEventListener('input', () => {
      clearTimeout(timer);
      const q = input.value.trim();
      if (q.length < 1) { show([]); return; }
      timer = setTimeout(async () => { try { show(await api('/search?q=' + encodeURIComponent(q))); } catch (e) { show([]); } }, 150);
    });
    input.addEventListener('keydown', e => {
      if (!names.length) return;
      if (e.key === 'ArrowDown') { active = (active + 1) % names.length; e.preventDefault(); }
      else if (e.key === 'ArrowUp') { active = (active - 1 + names.length) % names.length; e.preventDefault(); }
      else return;
      [...box.children].forEach((a, i) => a.classList.toggle('is-active', i === active));
    });
    input.addEventListener('blur', () => setTimeout(() => show([]), 150));
    form.addEventListener('submit', e => {
      e.preventDefault();
      const name = active >= 0 ? names[active] : input.value.trim();
      if (name) location.href = url.player(name);
    });
  }

  function fail(el, err) { el.innerHTML = `<div class="notice error">${esc(err.message || err)}</div>`; }

  const starsHtml = n => `<span class="stars">&#9733; ${n}<span class="max">/${maxStars()}</span></span>`;
  const classIcon = (type, cls) => {
    const c = data.classByType.get(type);
    return c && c.icon ? `<img class="${cls || 'class-icon-sm'}" src="${iconPath(c.icon)}" alt="${esc(c.name)}" title="${esc(c.name)}">` : '';
  };
  const className = type => (data.classByType.get(type) || {}).name || ('Class ' + hex(type));
  function slotHtml(type, slotName) {
    if (type == null || type < 0) return `<span class="slot empty" title="${esc(slotName || 'Empty')}"></span>`;
    const it = data.byType.get(type);
    if (!it) return `<span class="slot" title="Unknown item ${hex(type)}"></span>`;
    return `<a class="slot" href="${url.item(type)}" title="${esc(it.name)}${it.tier != null ? ' (T' + it.tier + ')' : ''}">${it.icon ? `<img src="${iconPath(it.icon)}" alt="${esc(it.name)}">` : ''}</a>`;
  }
  function statsHtml(ch) {
    const cls = data.classByType.get(ch.class);
    let maxed = 0;
    const chips = STATS.map(k => {
      const max = cls && cls.stats[STAT_KEYS[k]] ? cls.stats[STAT_KEYS[k]].max : 0;
      const isMax = max > 0 && ch[k] >= max;
      if (isMax) maxed++;
      return `<span class="stat ${isMax ? 'maxed' : ''}" title="${STAT_KEYS[k]} ${ch[k]} / ${max}"><b>${STAT_KEYS[k]}</b>${ch[k]}</span>`;
    }).join('');
    return { chips, maxed };
  }

  // ---- pages --------------------------------------------------------------------------------------------------------------
  const pages = {};

  pages.home = async () => {
    search($('#big-search'));
    const onlineEl = $('#online'), fameEl = $('#top-fame'), guildEl = $('#top-guilds'), charEl = $('#top-chars');
    try {
      const o = await api('/online');
      data.starGoals = o.starGoals || [];
      onlineEl.innerHTML = `<span class="online-pill">${num(o.online)} online</span> <span class="dim small">game version ${esc(o.version || '?')}</span>`;
    } catch (e) { onlineEl.innerHTML = '<span class="dim">The game servers are not answering right now.</span>'; }
    await loadData();
    const board = async (el, kind, render) => {
      try {
        const rows = (await api('/leaderboard?kind=' + kind)).slice(0, 10);
        el.innerHTML = rows.length ? `<table>${rows.map(render).join('')}</table>` : '<p class="dim">Nobody yet.</p>';
      } catch (e) { fail(el, e); }
    };
    board(fameEl, 'fame', r => `<tr><td class="rank">${r.rank}</td><td><a href="${url.player(r.name)}">${esc(r.name)}</a></td><td>${starsHtml(r.stars ?? 0)}</td><td class="num">${num(r.value)}</td></tr>`);
    board(charEl, 'chars', r => `<tr><td class="rank">${r.rank}</td><td>${classIcon(r.class)} <a href="${url.player(r.name)}">${esc(r.name)}</a></td><td class="dim">${esc(className(r.class))} ${r.level}</td><td class="num">${num(r.value)}</td></tr>`);
    board(guildEl, 'guilds', r => `<tr><td class="rank">${r.rank}</td><td><a href="${url.guild(r.name)}">${esc(r.name)}</a></td><td class="dim">${r.members} member${r.members === 1 ? '' : 's'}</td><td class="num">${num(r.value)}</td></tr>`);
  };

  pages.player = async () => {
    const name = arg('name'), root = $('#profile');
    document.title = (name || 'Player') + ' - The Portal';
    if (!name) { root.innerHTML = '<div class="notice">No player name given. Use the search box.</div>'; return; }
    try {
      const [p] = await Promise.all([api('/player?name=' + encodeURIComponent(name)), loadData(), api('/online').then(o => { data.starGoals = o.starGoals || []; }).catch(() => {})]);
      document.title = p.name + ' - The Portal';
      const chars = p.characters || [];
      const totalMaxed = chars.reduce((n, c) => n + statsHtml(c).maxed, 0);
      root.innerHTML = `
<div class="panel profile-head">
  <div>
    <h1>${esc(p.name)} ${p.rank >= 80 ? `<span class="tier small ember">${esc(p.rankName)}</span>` : ''}</h1>
    <dl>
      <dt>Characters</dt><dd>${chars.length}</dd>
      <dt>Rank</dt><dd>${starsHtml(p.stars)}</dd>
      <dt>Fame</dt><dd>${num(p.fame)} <span class="dim">(${num(p.totalFame)} total)</span></dd>
      <dt>Best character</dt><dd>${num(p.bestCharFame)} fame</dd>
      <dt>Guild</dt><dd>${p.guild ? `<a href="${url.guild(p.guild)}">${esc(p.guild)}</a> <span class="dim">${esc(guildRankName(p.guildRank))}</span>` : '<span class="dim">none</span>'}</dd>
      <dt>Created</dt><dd>${esc(p.created || '?')}</dd>
      <dt>Last seen</dt><dd><span class="online-dot ${p.online ? 'on' : ''}"></span>${p.online ? `online now${p.world ? ' in ' + esc(p.world) : ''}` : esc(p.lastSeen || 'never')}</dd>
    </dl>
  </div>
</div>
<div class="panel">
  <h2>Characters <span class="dim small">${chars.length ? totalMaxed + ' maxed stats in total' : ''}</span></h2>
  ${chars.length ? `<table><thead><tr><th></th><th>Class</th><th class="num">Level</th><th class="num">Fame</th><th class="num">Exp</th><th>Equipment</th><th>Stats</th><th></th></tr></thead><tbody>
    ${chars.map(c => { const s = statsHtml(c); const cls = data.classByType.get(c.class); const slots = cls ? cls.slotNames : [];
      return `<tr><td>${classIcon(c.class)}</td><td><a href="${url.klass(c.class)}">${esc(className(c.class))}</a>${c.backpack ? ' <span class="dim small" title="Has a backpack">&#127890;</span>' : ''}</td>
      <td class="num">${c.level}</td><td class="num">${num(c.fame)}</td><td class="num">${num(c.exp)}</td>
      <td class="gear">${c.equipment.map((t, i) => slotHtml(t, slots[i])).join('')}</td><td>${s.chips}</td><td class="gold">${s.maxed}/8</td></tr>`; }).join('')}
  </tbody></table>` : '<p class="dim">No living characters.</p>'}
</div>
<div class="panel">
  <h2>Class records</h2>
  ${p.classStats && p.classStats.length ? `<table><thead><tr><th></th><th>Class</th><th class="num">Best level</th><th class="num">Best fame</th><th>Stars</th></tr></thead><tbody>
    ${p.classStats.map(cs => `<tr><td>${classIcon(cs.class)}</td><td><a href="${url.klass(cs.class)}">${esc(className(cs.class))}</a></td><td class="num">${cs.bestLevel}</td><td class="num">${num(cs.bestFame)}</td>
      <td class="stars">${'&#9733;'.repeat(data.starGoals.filter(g => cs.bestFame >= g).length)}<span class="max">${'&#9734;'.repeat(Math.max(0, data.starGoals.length - data.starGoals.filter(g => cs.bestFame >= g).length))}</span></td></tr>`).join('')}
  </tbody></table>
  <p class="dim small">A star for every fame goal reached on a class: ${data.starGoals.map(num).join(', ') || '?'}.</p>` : '<p class="dim">No class records yet.</p>'}
</div>`;
    } catch (e) {
      root.innerHTML = /not found/i.test(e.message) ? `<div class="notice">No player called <b>${esc(name)}</b> exists.</div>` : `<div class="notice error">${esc(e.message)}</div>`;
    }
  };

  function guildRankName(r) { return ({ 0: 'Initiate', 10: 'Member', 20: 'Officer', 30: 'Leader', 40: 'Founder' })[r] || (r >= 40 ? 'Founder' : r >= 30 ? 'Leader' : r >= 20 ? 'Officer' : r >= 10 ? 'Member' : 'Initiate'); }

  pages.guild = async () => {
    const name = arg('name'), root = $('#guild');
    document.title = (name || 'Guild') + ' - The Portal';
    if (!name) { root.innerHTML = '<div class="notice">No guild name given.</div>'; return; }
    try {
      const [g] = await Promise.all([api('/guild?name=' + encodeURIComponent(name)), loadData(), api('/online').then(o => { data.starGoals = o.starGoals || []; }).catch(() => {})]);
      document.title = g.name + ' - The Portal';
      root.innerHTML = `
<div class="panel profile-head"><div>
  <h1>${esc(g.name)}</h1>
  <dl>
    <dt>Members</dt><dd>${g.members.length}</dd>
    <dt>Level</dt><dd>${g.level}</dd>
    <dt>Fame</dt><dd>${num(g.fame)} <span class="dim">(${num(g.totalFame)} total)</span></dd>
    <dt>Founded</dt><dd>${esc(g.created || '?')}</dd>
  </dl>
</div></div>
<div class="panel"><h2>Members</h2>
  ${g.members.length ? `<table><thead><tr><th>Name</th><th>Guild rank</th><th>Stars</th><th class="num">Fame</th></tr></thead><tbody>
  ${g.members.map(m => `<tr><td><a href="${url.player(m.name)}">${esc(m.name)}</a></td><td class="dim">${esc(guildRankName(m.guildRank))}</td><td>${starsHtml(m.stars)}</td><td class="num">${num(m.fame)}</td></tr>`).join('')}
  </tbody></table>` : '<p class="dim">No members.</p>'}
</div>`;
    } catch (e) {
      root.innerHTML = /not found/i.test(e.message) ? `<div class="notice">No guild called <b>${esc(name)}</b> exists.</div>` : `<div class="notice error">${esc(e.message)}</div>`;
    }
  };

  pages.top = async () => {
    const kind = ['fame', 'chars', 'level', 'guilds'].includes(arg('kind')) ? arg('kind') : 'fame';
    const titles = { fame: 'Top players by fame', chars: 'Top characters by fame', level: 'Top characters by level', guilds: 'Top guilds by fame' };
    document.title = titles[kind] + ' - The Portal';
    $('#tabs').innerHTML = Object.keys(titles).map(k => `<a href="${url.top(k)}" class="${k === kind ? 'is-current' : ''}">${titles[k].replace('Top ', '')}</a>`).join('');
    $('#title').textContent = titles[kind];
    const root = $('#board');
    try {
      const [rows] = await Promise.all([api('/leaderboard?kind=' + kind), loadData(), api('/online').then(o => { data.starGoals = o.starGoals || []; }).catch(() => {})]);
      if (!rows.length) { root.innerHTML = '<p class="dim">Nobody yet.</p>'; return; }
      const head = kind === 'guilds' ? '<th>Guild</th><th class="num">Members</th><th class="num">Total fame</th>'
        : kind === 'fame' ? '<th>Player</th><th>Stars</th><th class="num">Total fame</th>'
        : `<th>Player</th><th>Class</th><th class="num">${kind === 'chars' ? 'Fame' : 'Level'}</th>`;
      root.innerHTML = `<table><thead><tr><th class="rank">#</th>${head}</tr></thead><tbody>${rows.map(r => {
        if (kind === 'guilds') return `<tr><td class="rank">${r.rank}</td><td><a href="${url.guild(r.name)}">${esc(r.name)}</a></td><td class="num">${r.members}</td><td class="num">${num(r.value)}</td></tr>`;
        if (kind === 'fame') return `<tr><td class="rank">${r.rank}</td><td><a href="${url.player(r.name)}">${esc(r.name)}</a></td><td>${starsHtml(r.stars ?? 0)}</td><td class="num">${num(r.value)}</td></tr>`;
        return `<tr><td class="rank">${r.rank}</td><td><a href="${url.player(r.name)}">${esc(r.name)}</a></td><td>${classIcon(r.class)} ${esc(className(r.class))}${kind === 'chars' ? ` <span class="dim">lvl ${r.level}</span>` : ''}</td><td class="num">${num(r.value)}</td></tr>`;
      }).join('')}</tbody></table>`;
    } catch (e) { fail(root, e); }
  };

  const boostsHtml = it => it.boosts.length ? it.boosts.map(b => `<span class="${b.amount >= 0 ? 'green' : 'red'}">${b.amount >= 0 ? '+' : ''}${b.amount} ${esc(b.stat)}</span>`).join(', ') : '';
  const damageHtml = it => it.damage ? `${it.damage[0]}&ndash;${it.damage[1]} damage${it.numProjectiles > 1 ? ' &times; ' + it.numProjectiles : ''}${it.rateOfFire && it.rateOfFire !== 1 ? `, ${Math.round(it.rateOfFire * 100)}% rate of fire` : ''}` : '';

  pages.items = async () => {
    await loadData();
    const type = arg('type') ? parseInt(arg('type'), 16) : NaN;
    const root = $('#wiki');
    if (!isNaN(type)) {
      const it = data.byType.get(type);
      if (!it) { root.innerHTML = `<div class="notice">No item with the id ${esc(arg('type'))}.</div>`; return; }
      document.title = it.name + ' - The Portal';
      const usedBy = data.classes.filter(c => c.slots.includes(it.slot));
      root.innerHTML = `<p><a href="${url.items()}">&larr; All items</a></p>
<div class="panel profile-head">
  ${it.icon ? `<img class="icon" style="width:96px;height:96px" src="${iconPath(it.icon)}" alt="">` : ''}
  <div>
    <h1>${esc(it.name)} ${it.tier != null ? `<span class="tier tier-${it.tier}">T${it.tier}</span>` : ''}</h1>
    <p>${esc(it.description)}</p>
    <dl>
      <dt>Slot</dt><dd>${esc(it.slotName)}</dd>
      ${it.damage ? `<dt>Damage</dt><dd>${damageHtml(it)}</dd>` : ''}
      ${it.boosts.length ? `<dt>Stat bonus</dt><dd>${boostsHtml(it)}</dd>` : ''}
      ${it.consumable ? '<dt>Type</dt><dd>Consumable</dd>' : ''}
      ${it.soulbound ? '<dt>Soulbound</dt><dd>Yes</dd>' : ''}
      <dt>Used by</dt><dd>${usedBy.length ? usedBy.map(c => `<a href="${url.klass(c.type)}">${esc(c.name)}</a>`).join(', ') : (it.slot === 0 || it.consumable ? 'Every class' : '<span class="dim">no class</span>')}</dd>
      <dt>Id</dt><dd class="dim">0x${hex(it.type)}</dd>
    </dl>
  </div>
</div>`;
      return;
    }
    document.title = 'Items - The Portal';
    const slots = [...new Set(data.items.map(i => i.slotName))];
    let slot = 'All';
    const render = () => {
      const list = data.items.filter(i => slot === 'All' || i.slotName === slot);
      $('#cards').innerHTML = list.map(it => `<a class="card" href="${url.item(it.type)}">
        ${it.icon ? `<img class="icon" src="${iconPath(it.icon)}" alt="">` : '<span class="icon"></span>'}
        <div><div class="name">${esc(it.name)}</div><div class="meta">${it.tier != null ? `<span class="tier tier-${it.tier}">T${it.tier}</span> ` : ''}${esc(it.slotName)}<br>
        ${damageHtml(it) || boostsHtml(it) || (it.consumable ? 'Consumable' : '')}</div></div></a>`).join('');
    };
    root.innerHTML = `<h1>Items</h1><div class="filters" id="filters"></div><div class="cards" id="cards"></div>`;
    $('#filters').innerHTML = ['All', ...slots].map(s => `<button class="chip ${s === slot ? 'is-on' : ''}" data-slot="${esc(s)}">${esc(s)}</button>`).join('');
    $('#filters').addEventListener('click', e => {
      const b = e.target.closest('.chip'); if (!b) return;
      slot = b.dataset.slot; [...$('#filters').children].forEach(c => c.classList.toggle('is-on', c === b)); render();
    });
    render();
  };

  pages.classes = async () => {
    await loadData();
    const type = arg('type') ? parseInt(arg('type'), 16) : NaN;
    const root = $('#wiki');
    const statRow = c => Object.keys(c.stats).map(k => `<tr><td>${k}</td><td class="num">${c.stats[k].start}</td><td class="num gold">${c.stats[k].max}</td></tr>`).join('');
    if (!isNaN(type)) {
      const c = data.classByType.get(type);
      if (!c) { root.innerHTML = '<div class="notice">No such class.</div>'; return; }
      document.title = c.name + ' - The Portal';
      const gear = data.items.filter(i => c.slots.includes(i.slot)).sort((a, b) => a.slot - b.slot || (a.tier ?? -1) - (b.tier ?? -1));
      root.innerHTML = `<p><a href="${url.classes()}">&larr; All classes</a></p>
<div class="panel profile-head">
  ${c.icon ? `<img class="class-icon" style="width:128px;height:128px" src="${iconPath(c.icon)}" alt="">` : ''}
  <div><h1>${esc(c.name)}</h1><p>${esc(c.description)}</p>
    <p class="dim">Equipment: ${c.slotNames.join(', ')}</p>
    <p class="dim">Starts with: ${c.startingEquipment.map(t => t >= 0 ? slotHtml(t) : '').join('')}</p></div>
</div>
<div class="grid-2">
  <div class="panel"><h2>Stats</h2><table><thead><tr><th>Stat</th><th class="num">Start</th><th class="num">Max</th></tr></thead><tbody>${statRow(c)}</tbody></table></div>
  <div class="panel"><h2>Gear this class can use</h2>
    <div class="cards">${gear.map(it => `<a class="card" href="${url.item(it.type)}">${it.icon ? `<img class="icon" src="${iconPath(it.icon)}" alt="">` : ''}<div><div class="name">${esc(it.name)}</div><div class="meta">${it.tier != null ? `<span class="tier tier-${it.tier}">T${it.tier}</span> ` : ''}${esc(it.slotName)}</div></div></a>`).join('') || '<p class="dim">Nothing yet.</p>'}</div>
  </div>
</div>`;
      return;
    }
    document.title = 'Classes - The Portal';
    root.innerHTML = `<h1>Classes</h1><div class="grid-2">${data.classes.map(c => `<a class="panel card" href="${url.klass(c.type)}" style="margin:0">
      ${c.icon ? `<img class="class-icon" src="${iconPath(c.icon)}" alt="">` : ''}
      <div><div class="name" style="font-size:12px">${esc(c.name)}</div><div class="meta">${esc(c.description)}</div>
      <div class="meta" style="margin-top:6px">${c.slotNames.join(' &middot; ')}</div></div></a>`).join('')}</div>`;
  };

  pages.graveyard = async () => { /* static page: the game does not record deaths yet */ };

  // Release history (2026-09-22): the game's patch notes, newest first, grouped by month, with the version each update shipped in.
  pages.releases = async () => {
    const d = await api('/releases');
    const cur = $('#release-current');
    cur.innerHTML = d.version ? `The game is on version <b>${esc(d.version)}</b>. Every update, newest first.` : 'Every update, newest first.';
    const list = d.releases || [];
    if (!list.length) { $('#releases').innerHTML = '<div class="panel dim">Nothing written down yet.</div>'; return; }
    const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December'];
    const monthOf = date => { const m = /^(\d{4})-(\d{2})/.exec(date || ''); return m ? `${MONTHS[+m[2] - 1]} ${m[1]}` : 'Undated'; };
    const idOf = label => 'm-' + label.toLowerCase().replace(/[^a-z0-9]+/g, '-');
    let html = '', current = null;
    const months = [];
    for (const r of list) {
      const month = monthOf(r.date);
      if (month !== current) {
        if (current !== null) html += '</section>';
        current = month;
        months.push(month);
        html += `<section id="${idOf(month)}"><h2>${esc(month)}</h2>`;
      }
      const version = r.version ? `<span class="nav-play" style="padding:2px 8px;margin-right:10px">v${esc(r.version)}</span>` : '';
      html += `<div class="panel"><h3 style="margin-top:0">${version}${esc(r.title)} <span class="dim small">${esc(r.date)}</span></h3>` +
              `<ul>${(r.lines || []).map(l => `<li>${esc(l)}</li>`).join('')}</ul></div>`;
    }
    if (current !== null) html += '</section>';
    $('#releases').innerHTML = html;
    $('#release-months').innerHTML = months.length > 1 ? months.map(m => `<a href="#${idOf(m)}">${esc(m)}</a>`).join(' &middot; ') : '';
  };

  document.addEventListener('DOMContentLoaded', () => {
    const page = document.body.dataset.page;
    chrome(page === 'top' && arg('kind') === 'guilds' ? 'guilds' : page);
    if (pages[page]) pages[page]().catch(e => { const m = $('main'); if (m) fail(m, e); });
  });
})();
