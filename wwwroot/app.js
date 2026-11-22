'use strict';
// Interface do Analisador LoL. JavaScript puro, sem framework e sem animações, para pesar o mínimo possível.

const S = {
  champs: {}, items: {}, runes: {}, trees: {}, spells: {}, champList: [],
  settings: null, state: null, patch: '', version: '',
  page: 'profile', wasLive: false,
  tier: null, tierPos: 'TOP', tierSort: 'score',
  query: '', champSel: null, champPos: null, champMode: 'classic',
  tierMode: 'classic', modeTier: {}, augments: {},
  modal: null, runeIdx: 0,
  builds: {}, loading: {},
};

const POS = ['TOP', 'JUNGLE', 'MID', 'ADC', 'SUPPORT'];
const POS_NAME = { TOP: 'Topo', JUNGLE: 'Selva', MID: 'Meio', ADC: 'Atirador', SUPPORT: 'Suporte' };
const POS_SHORT = { TOP: 'TOP', JUNGLE: 'SELVA', MID: 'MEIO', ADC: 'ATIR', SUPPORT: 'SUP' };
const SHARDS = {
  5008: 'Força adaptável', 5005: 'Velocidade de ataque', 5007: 'Aceleração de habilidade', 5010: 'Velocidade de movimento',
  5001: 'Vida (escala)', 5011: 'Vida', 5013: 'Tenacidade e resist. a lentidão', 5002: 'Armadura', 5003: 'Resistência mágica',
};
const TIERS = [['all', 'Todos os elos'], ['gold_plus', 'Ouro+'], ['platinum_plus', 'Platina+'], ['emerald_plus', 'Esmeralda+'],
  ['diamond_plus', 'Diamante+'], ['master_plus', 'Mestre+'], ['challenger', 'Desafiante']];
const REGIONS = [['global', 'Mundial'], ['br', 'Brasil'], ['na', 'América do Norte'], ['euw', 'Europa Oeste'], ['kr', 'Coreia']];
const RANK_PT = { IRON: 'Ferro', BRONZE: 'Bronze', SILVER: 'Prata', GOLD: 'Ouro', PLATINUM: 'Platina', EMERALD: 'Esmeralda',
  DIAMOND: 'Diamante', MASTER: 'Mestre', GRANDMASTER: 'Grão-Mestre', CHALLENGER: 'Desafiante', 'SEM ELO': 'Sem elo' };
const PHASE_PT = { Desconectado: 'Cliente fechado', None: 'Menu principal', Lobby: 'No saguão', Matchmaking: 'Procurando partida',
  ReadyCheck: 'Partida encontrada', ChampSelect: 'Seleção de campeões', GameStart: 'Carregando partida', InProgress: 'Em partida',
  Reconnect: 'Reconectar', WaitingForStats: 'Fim de jogo', PreEndOfGame: 'Fim de jogo', EndOfGame: 'Fim de jogo' };
const MODES = ['classic', 'aram', 'mayhem', 'arena', 'urf', 'nexus'];
const MODE_NAME = { classic: "Summoner's Rift", aram: 'ARAM', mayhem: 'ARAM com aprimoramentos', arena: 'Arena', urf: 'URF', nexus: 'Nexus Blitz', unsupported: 'Modo sem estatísticas' };
const MODE_SLUG = { aram: 'aram', mayhem: 'aram-mayhem', arena: 'arena', urf: 'urf', nexus: 'nexus-blitz' };
const RARITY = { 8: ['Prismático', 'prism'], 4: ['Ouro', 'gold'], 1: ['Prata', 'silver'] };
const DRAKES = { Fire: ['Infernal', '#ff6a3d'], Water: ['Oceano', '#3fb6e8'], Earth: ['Montanha', '#b98a52'], Air: ['Nuvens', '#d8ecf6'],
  Hextech: ['Hextec', '#34e0e8'], Chemtech: ['Quimtec', '#8fd14f'], Elder: ['Ancião', '#b78cff'] };

const ICON = {
  chain: '<svg class="i" viewBox="0 0 24 24"><path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/></svg>',
  tower: '<svg class="i" viewBox="0 0 24 24"><path d="M6 21h12M8 21V11h8v10M6 11h12V5h-2.5v2.5h-2V5h-3v2.5h-2V5H6z"/></svg>',
  inhib: '<svg class="i" viewBox="0 0 24 24"><path d="M12 3 20 12 12 21 4 12z"/></svg>',
  baron: '<svg class="i" viewBox="0 0 24 24"><path d="M12 3c-4 0-7 3-7 7 0 3 2 5 3 6v4h8v-4c1-1 3-3 3-6 0-4-3-7-7-7z"/><path d="M10 11h.01M14 11h.01"/></svg>',
  herald: '<svg class="i" viewBox="0 0 24 24"><path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/></svg>',
  grubs: '<svg class="i" viewBox="0 0 24 24"><circle cx="12" cy="13" r="5"/><path d="M12 4v4M5 8l3 2M19 8l-3 2M4 15h3M17 15h3"/></svg>',
  drake: '<svg class="i" viewBox="0 0 24 24"><path d="M12 3c2 3 5 4.5 5 8.5a5 5 0 0 1-10 0c0-2 1-3.5 2-4.5 0 2 1 3 2 3 0-3 0-5 1-7z"/></svg>',
  swords: '<svg class="i" viewBox="0 0 24 24"><path d="M14.5 17.5 3 6V3h3l11.5 11.5M13 19l6-6M16 16l4 4M19 21l2-2M9.5 14.5 3 21M3 18l3 3"/></svg>',
};

// ------------------------------------------------------------------ comunicação com o C#
let seq = 0;
const pending = new Map();
function call(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++seq;
    pending.set(id, { resolve, reject });
    window.chrome.webview.postMessage({ id, method, params });
  });
}
window.chrome.webview.addEventListener('message', (e) => {
  const m = e.data;
  if (m.event === 'state') {
    S.state = m.data;
    renderStatus();
    autoTab();
    if (S.page === 'live' || S.page === 'profile') renderPage();
    return;
  }
  if (m.event === 'toast') { toast(m.text); return; }
  const p = pending.get(m.id);
  if (!p) return;
  pending.delete(m.id);
  m.error !== undefined ? p.reject(new Error(m.error)) : p.resolve(m.result);
});

// ------------------------------------------------------------------ utilidades
const $ = (sel) => document.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const pct = (v, d = 1) => (v * 100).toFixed(d).replace('.', ',') + '%';
const num = (v) => Number(v || 0).toLocaleString('pt-BR');
const signed = (v) => (v > 0 ? '+' : v < 0 ? '−' : '±') + num(Math.abs(v));
const sgnCls = (v) => (v > 0 ? 'good' : v < 0 ? 'bad' : 'muted');
const wrCls = (v) => (v >= 0.515 ? 'good' : v <= 0.485 ? 'bad' : '');
const norm = (s) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
const cname = (id) => S.champs[id]?.name ?? '—';
const mmss = (t) => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}`;
const tierName = (k) => TIERS.find((t) => t[0] === k)?.[1] ?? k;
const regionName = (k) => REGIONS.find((t) => t[0] === k)?.[1] ?? k;

function champImg(id, cls = '') {
  const c = S.champs[id];
  if (!c) return `<div class="ci ${cls}"></div>`;
  return `<img class="ci ${cls}" src="${c.icon}" alt="" data-tt="c:${id}" loading="lazy" decoding="async">`;
}
function itemImg(id, cls = '') {
  const it = S.items[id];
  if (!it) return id ? `<div class="ii ${cls}" title="Item ${id}"></div>` : `<div class="ii ${cls} empty-slot"></div>`;
  return `<img class="ii ${cls}" src="${it.icon}" alt="" data-tt="i:${id}" loading="lazy" decoding="async">`;
}
function itemStep(step, cls = '') {
  return `<div class="item ${step.status}">${itemImg(step.itemId, cls)}</div>`;
}
function runeImg(id, cls = '') {
  const r = S.runes[id];
  return r ? `<img class="ri ${cls}" src="${r.icon}" alt="" data-tt="r:${id}" loading="lazy">` : '';
}
function treeImg(id) {
  const t = S.trees[id];
  return t ? `<img class="ri tree" src="${t.icon}" alt="" data-tt="t:${id}">` : '';
}
function spellImg(id, cls = 'sm') {
  const s = S.spells[id];
  return s ? `<img class="ii ${cls}" src="${s.icon}" alt="" data-tt="s:${id}">` : '';
}
function tierBadge(t) { return t ? `<span class="tier t${t}">Tier ${t}</span>` : ''; }

/** Elo. Para outros jogadores o cliente da Riot não informa as derrotas (vem sempre 0): mostra só as vitórias. */
function rankText(r, short) {
  if (!r) return '';
  if (r.tier === 'SEM ELO') return '<span class="muted">Sem elo</span>';
  const base = `<span>${RANK_PT[r.tier] ?? r.tier} ${r.division}${short ? '' : ` · ${r.lp} PDL`}</span>`;
  if (!r.losses) return r.wins ? `${base} <span class="muted" title="O cliente da Riot não informa as derrotas de outros jogadores">· ${num(r.wins)} V</span>` : base;
  const games = r.wins + r.losses;
  const wr = r.wins / games;
  return `${base} <span class="${wrCls(wr)}">· ${pct(wr, 0)} (${games})</span>`;
}

// ---------- grupos (duo, trio…): cor por grupo, distinta entre os times
const PARTY_NAME = { 2: 'Duo', 3: 'Trio', 4: 'Quarteto', 5: 'Grupo de 5' };
const partyColor = (p, enemy) => ((p.group - 1 + (enemy ? 1 : 0)) % 3) + 1;
function partyTag(p, enemy) {
  if (!p) return '';
  const title = p.exact ? 'Estão no mesmo grupo (informação do cliente do LoL)' : `Provável grupo: jogaram juntos em ${p.games} das últimas 20 partidas`;
  return `<span class="party g${partyColor(p, enemy)} ${p.exact ? '' : 'guess'}" title="${title}">${ICON.chain}${PARTY_NAME[p.size] ?? 'Grupo'}${p.exact ? '' : '?'}</span>`;
}
const partyAttr = (p, enemy) => (p ? ` data-party style="--pc:var(--p${partyColor(p, enemy)})"` : '');
function partyGroups(list, enemy) {
  const groups = {};
  for (const p of list) if (p.party && p.championId) (groups[p.party.group] ??= { tag: p.party, ids: [] }).ids.push(p.championId);
  return Object.values(groups).map((g) => `<div class="group-pill">${partyTag(g.tag, enemy)}
    <div class="faces">${g.ids.map((id) => champImg(id, 'sm')).join('')}</div>
    <span>${esc(g.ids.map(cname).join(' + '))}</span><span class="small ${enemy ? 'enemy' : 'ally'}">${enemy ? 'inimigo' : 'seu time'}</span></div>`).join('');
}

function dmgBar(d, label) {
  if (!d) return '';
  return `<div style="margin-top:12px">
    <div class="row small" style="justify-content:space-between;gap:8px">${label ? `<b>${label}</b>` : ''}
      <span><span style="color:var(--phys)">Físico ${d.physical}%</span> · <span style="color:var(--magic)">Mágico ${d.magic}%</span>${d.true ? ` · <span style="color:var(--truedmg)">Verdadeiro ${d.true}%</span>` : ''}</span></div>
    <div class="bar" style="margin-top:5px"><i style="width:${d.physical}%;background:var(--phys)"></i><i style="width:${d.magic}%;background:var(--magic)"></i><i style="flex:1;background:var(--truedmg)"></i></div></div>`;
}

// ---------- dica ao parar o mouse (itens, aprimoramentos, runas, feitiços, campeões)
const ROLE_PT = { Fighter: 'Lutador', Tank: 'Tanque', Mage: 'Mago', Assassin: 'Assassino', Marksman: 'Atirador', Support: 'Suporte' };
const AUG_RARITY = { prata: ['Prata', 'silver'], ouro: ['Ouro', 'gold'], prismatico: ['Prismático', 'prism'], evento: ['Especial', 'silver'] };
/** Conteúdo da dica. As descrições (desc) já vêm do C# como HTML seguro (texto escapado, só marcações simples). */
function tipHtml(kind, id) {
  if (kind === 'i') {
    const it = S.items[id];
    if (!it) return '';
    return `<div class="tt-h"><img src="${it.icon}" alt=""><div><b>${esc(it.name)}</b><div class="tt-sub"><span class="coin"></span>${num(it.gold)}</div></div></div>
      ${it.desc ? `<div class="tt-d">${it.desc}</div>` : it.plaintext ? `<div class="tt-d">${esc(it.plaintext)}</div>` : ''}`;
  }
  if (kind === 'a') {
    const a = S.augments[id];
    if (!a) return '';
    const [rn, rc] = AUG_RARITY[a.rarity] ?? ['', 'silver'];
    return `<div class="tt-h">${a.icon ? `<img src="${a.icon}" alt="">` : ''}<div><b>${esc(a.name)}</b><div class="tt-sub r-${rc}">Aprimoramento ${rn.toLowerCase()}</div></div></div>
      <div class="tt-d">${a.desc || '<span class="muted">Sem descrição disponível para este aprimoramento.</span>'}</div>`;
  }
  if (kind === 'r') {
    const r = S.runes[id];
    return r ? `<div class="tt-h"><img src="${r.icon}" alt=""><div><b>${esc(r.name)}</b><div class="tt-sub">${esc(S.trees[r.treeId]?.name ?? '')}</div></div></div>${r.desc ? `<div class="tt-d">${r.desc}</div>` : ''}` : '';
  }
  if (kind === 't') { const t = S.trees[id]; return t ? `<div class="tt-h"><img src="${t.icon}" alt=""><b>${esc(t.name)}</b></div>` : ''; }
  if (kind === 's') {
    const s = S.spells[id];
    return s ? `<div class="tt-h"><img src="${s.icon}" alt=""><div><b>${esc(s.name)}</b>${s.cooldown ? `<div class="tt-sub">Recarga: ${esc(s.cooldown)} s</div>` : ''}</div></div>${s.desc ? `<div class="tt-d">${s.desc}</div>` : ''}` : '';
  }
  if (kind === 'c') {
    const c = S.champs[id];
    return c ? `<div class="tt-h"><img src="${c.icon}" alt=""><div><b>${esc(c.name)}</b><div class="tt-sub">${esc(c.title)}</div></div></div>
      <div class="tt-d muted">${c.tags.map((t) => ROLE_PT[t] ?? t).join(' · ')}</div>` : '';
  }
  return '';
}
let tipTimer, tipFor = null, tipX = 0, tipY = 0;
function placeTip() {
  const t = $('#tip');
  const w = t.offsetWidth, h = t.offsetHeight, m = 14;
  let x = tipX + m, y = tipY + m;
  if (x + w > innerWidth - 6) x = Math.max(6, tipX - w - m);
  if (y + h > innerHeight - 6) y = Math.max(6, tipY - h - m);
  t.style.transform = `translate(${x}px, ${y}px)`;
}
function hideTip() { clearTimeout(tipTimer); tipFor = null; const t = $('#tip'); if (t) t.hidden = true; }
document.addEventListener('mouseover', (e) => {
  const el = e.target.closest?.('[data-tt]');
  if (el === tipFor) return;
  hideTip();
  if (!el) return;
  tipFor = el;
  tipTimer = setTimeout(() => {
    if (tipFor !== el || !el.isConnected) return;
    const [kind, id] = el.dataset.tt.split(':');
    const html = tipHtml(kind, Number(id));
    if (!html) return;
    const t = $('#tip');
    t.innerHTML = html;
    t.hidden = false;
    placeTip();
  }, 250);
});
document.addEventListener('mousemove', (e) => { tipX = e.clientX; tipY = e.clientY; if (tipFor && !$('#tip').hidden) placeTip(); }, { passive: true });
document.documentElement.addEventListener('mouseleave', hideTip);
document.addEventListener('scroll', hideTip, true);
document.addEventListener('mousedown', hideTip);

let toastTimer;
function toast(msg) {
  const t = $('#toast');
  t.textContent = msg;
  t.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => (t.hidden = true), 4500);
}

const buildKey = (champ, pos, mode) => (mode && mode !== 'classic' ? `${champ}_${mode}` : `${champ}_${pos}`);
async function getBuild(champ, pos, mode = 'classic') {
  const key = buildKey(champ, pos, mode);
  if (S.builds[key]) return S.builds[key];
  if (S.loading[key]) return null;
  S.loading[key] = true;
  try {
    const b = await call('build', { championId: champ, position: pos, mode });
    if (b) S.builds[key] = b;
    return b;
  } catch (e) {
    toast('Erro ao buscar build: ' + e.message);
    return null;
  } finally {
    delete S.loading[key];
  }
}

// ------------------------------------------------------------------ inicialização
async function init() {
  const r = await call('init');
  if (r.error) {
    $('#main').innerHTML = `<div class="empty"><b>Não foi possível carregar os dados do jogo</b>${esc(r.error)}<br><br>Verifique a conexão com a internet e reabra o programa.</div>`;
    return;
  }
  for (const c of r.static.champions) S.champs[c.id] = c;
  for (const i of r.static.items) S.items[i.id] = i;
  for (const x of r.static.runes) S.runes[x.id] = x;
  for (const x of r.static.trees) S.trees[x.id] = x;
  for (const x of r.static.spells) S.spells[x.id] = x;
  for (const x of r.static.augments ?? []) S.augments[x.id] = x;
  S.champList = r.static.champions;
  S.settings = r.settings;
  S.patch = r.patch;
  S.version = r.version;
  S.state = r.state;
  renderStatus();
  autoTab();
  renderPage();
}

document.getElementById('nav').addEventListener('click', (e) => {
  const b = e.target.closest('button[data-page]');
  if (b) go(b.dataset.page);
});
/** Seleção ou partida começou: vai para "Ao vivo". Acabou: volta para "Perfil" (se estava em "Ao vivo"). */
function autoTab() {
  const live = !!(S.state?.champSelect || S.state?.game);
  if (live && !S.wasLive) { S.wasLive = true; if (S.page !== 'live') go('live'); }
  else if (!live && S.wasLive) { S.wasLive = false; S.csPrev = null; if (S.page === 'live') go('profile'); }
}

function go(page) {
  S.page = page;
  document.querySelectorAll('#nav button').forEach((b) => b.classList.toggle('active', b.dataset.page === page));
  $('#main').scrollTop = 0;
  renderPage();
}

function renderStatus() {
  const st = S.state;
  const c = st?.client;
  const key = S.settings?.overlayHotkey;
  const html = `<div class="st" title="${esc(c?.connected ? PHASE_PT[c.phase] ?? c.phase : 'Cliente do LoL fechado')}"><span class="dot ${c?.connected ? 'on' : ''}"></span></div>
    <div class="small">${c?.connected ? esc(PHASE_PT[c.phase] ?? c.phase) : 'LoL fechado'}</div>
    ${st?.mode && st.mode !== 'classic' ? `<div class="small gold">${esc(MODE_NAME[st.mode] ?? st.mode)}</div>` : ''}
    <div class="small muted">${esc(st?.patch || S.patch)}</div>
    ${key ? `<kbd title="Mostra/esconde o app no jogo">${esc(key)}</kbd>` : ''}`;
  const el = $('#status');
  if (el._last !== html) { el.innerHTML = html; el._last = html; }
}

function renderPage() {
  const main = $('#main');
  const html = S.page === 'profile' ? renderProfilePage() : S.page === 'live' ? renderLive() : S.page === 'tier' ? renderTier() : S.page === 'champs' ? renderChamps() : renderSettings();
  if (main._last !== html) { main.innerHTML = html; main._last = html; if (tipFor && !tipFor.isConnected) hideTip(); }
  if (S.page === 'champs') { const q = $('#q'); if (q && document.activeElement !== q && S.focusSearch) { q.focus(); S.focusSearch = false; } }
}

// ------------------------------------------------------------------ AO VIVO
function renderLive() {
  const st = S.state;
  if (!S.settings) return '<div class="empty">Carregando…</div>';
  if (!st) return '<div class="empty">Procurando o cliente do League of Legends…</div>';
  if (st.champSelect) return renderChampSelect(st.champSelect);
  if (st.game) return renderGame(st.game);
  const c = st.client;
  if (!c.connected)
    return `<div class="empty"><b>Abra o League of Legends</b>O programa se conecta sozinho ao cliente assim que ele abrir.<br>Ele fica na bandeja do Windows, perto do relógio.</div>`;
  if (['InProgress', 'GameStart', 'Reconnect'].includes(c.phase))
    return `<div class="empty"><b>Partida carregando…</b>As informações aparecem assim que o jogo começar.</div>`;
  return `<div class="empty"><b>Nenhuma partida em andamento</b>${esc(PHASE_PT[c.phase] ?? c.phase)}. Quando a seleção de campeões começar, esta tela abre sozinha com as sugestões.
    <div style="margin-top:16px"><label class="switch"><input type="checkbox" data-set="autoAccept" ${S.settings.autoAccept ? 'checked' : ''}> Aceitar partidas automaticamente</label></div></div>`;
}

function renderProfilePage() {
  const st = S.state;
  if (!S.settings) return '<div class="empty">Carregando…</div>';
  if (st?.profile) return renderProfile(st.profile, st.client);
  if (!st?.client?.connected)
    return `<div class="empty"><b>Abra o League of Legends</b>O seu perfil aparece aqui assim que o cliente abrir (depois ele fica guardado, mesmo com o cliente fechado).</div>`;
  return `<div class="empty"><b>Conectado${st.client.summoner ? ' como ' + esc(st.client.summoner) : ''}</b>Carregando o seu perfil…</div>`;
}

// ------------------------------------------------------------------ PERFIL (tela inicial)
const QUEUE_NAME = { 420: 'Ranqueada Solo/Duo', 440: 'Ranqueada Flex', 400: 'Normal (escolha)', 430: 'Normal (às cegas)', 480: 'Escolha rápida',
  490: 'Escolha rápida', 450: 'ARAM', 2400: 'ARAM com aprimoramentos', 1700: 'Arena', 1710: 'Arena', 1750: 'Arena 3x6', 900: 'URF', 1900: 'URF',
  1020: 'Todos por Um', 1300: 'Nexus Blitz', 0: 'Personalizada' };
const GAMEMODE_NAME = { CLASSIC: 'Normal', ARAM: 'ARAM', KIWI: 'ARAM com aprimoramentos', CHERRY: 'Arena', URF: 'URF', ARURF: 'URF', NEXUSBLITZ: 'Nexus Blitz', ONEFORALL: 'Todos por Um', PRACTICETOOL: 'Treino' };
const queueName = (m) => QUEUE_NAME[m.queueId] ?? GAMEMODE_NAME[m.gameMode] ?? m.gameMode;
const RANK_COLOR = { IRON: '#8d8079', BRONZE: '#b9785a', SILVER: '#a9b4c0', GOLD: '#e3b95f', PLATINUM: '#4fc3b2', EMERALD: '#36c46e',
  DIAMOND: '#6f9cf5', MASTER: '#c07cf0', GRANDMASTER: '#f0646f', CHALLENGER: '#f6d68f' };
function ago(ms) {
  const m = Math.max(1, Math.round((Date.now() - ms) / 60000));
  if (m < 60) return `há ${m} min`;
  const h = Math.round(m / 60);
  if (h < 24) return `há ${h} h`;
  const d = Math.round(h / 24);
  return d === 1 ? 'ontem' : `há ${d} dias`;
}
const kdaRatio = (k, d, a) => (d ? ((k + a) / d).toFixed(2).replace('.', ',') : 'Perfeito');

function rankCard(r, label) {
  if (!r || !r.tier) return `<div class="rank-card"><div class="small muted">${label}</div><b class="muted">Sem elo</b></div>`;
  const games = r.wins + r.losses;
  const wr = games ? r.wins / games : 0;
  return `<div class="rank-card" style="--rc:${RANK_COLOR[r.tier] ?? 'var(--gold)'}">
    <div class="small muted">${label}</div>
    <div class="rk">${RANK_PT[r.tier] ?? r.tier} ${r.division}</div>
    <div class="small"><b>${r.lp} PDL</b> · ${r.wins}V ${r.losses}D · <span class="${wrCls(wr)}">${pct(wr, 0)}</span></div>
    ${r.highestTier ? `<div class="small muted">Pico: ${RANK_PT[r.highestTier] ?? r.highestTier} ${r.highestDivision}</div>` : ''}</div>`;
}

function renderProfile(p, c) {
  const solo = p.ranks.find((r) => r.queue === 'RANKED_SOLO_5x5');
  const flex = p.ranks.find((r) => r.queue === 'RANKED_FLEX_SR');
  const games = p.matches.filter((m) => !m.remake);
  const wins = games.filter((m) => m.win).length;
  const k = games.reduce((s, m) => s + m.kills, 0), d = games.reduce((s, m) => s + m.deaths, 0), a = games.reduce((s, m) => s + m.assists, 0);
  const byChamp = {};
  for (const m of games) { const x = (byChamp[m.championId] ??= { id: m.championId, n: 0, w: 0, k: 0, d: 0, a: 0 }); x.n++; x.w += m.win ? 1 : 0; x.k += m.kills; x.d += m.deaths; x.a += m.assists; }
  const top = Object.values(byChamp).sort((x, y) => y.n - x.n || y.w - x.w).slice(0, 4);
  const byQueue = {};
  for (const m of games) { const q = (byQueue[queueName(m)] ??= { n: 0, w: 0 }); q.n++; q.w += m.win ? 1 : 0; }

  const rows = p.matches.map((m) => {
    const arena = m.placement > 0;
    const result = m.remake ? ['Refeita', 'remake'] : arena ? [`${m.placement}º lugar`, m.placement <= 4 ? 'win' : 'loss'] : m.win ? ['Vitória', 'win'] : ['Derrota', 'loss'];
    return `<div class="mrow ${result[1]}">
      <div class="mq"><b class="${result[1] === 'win' ? 'good' : result[1] === 'loss' ? 'bad' : 'muted'}">${result[0]}</b><div class="small muted one">${esc(queueName(m))}</div><div class="small muted">${mmss(m.duration)} · ${ago(m.created)}</div></div>
      <div class="click" data-act="champ" data-c="${m.championId}">${champImg(m.championId, 'c34')}</div>
      <div class="mk tnum"><b>${m.kills}/${m.deaths}/${m.assists}</b><div class="small muted">${kdaRatio(m.kills, m.deaths, m.assists)} KDA</div></div>
      <div class="mk tnum small">${arena ? '' : `${m.cs} CS<br>`}<span class="coin"></span>${num(m.gold)}</div>
      <div class="items">${m.items.map((id) => itemImg(id, 'i20')).join('')}</div>
      <div class="maug">${m.augments.map((id) => S.augments[id]?.icon ? `<img src="${S.augments[id].icon}" alt="" data-tt="a:${id}">` : '').join('')}</div>
    </div>`;
  }).join('');

  return `<div class="fit">
  <div class="home-grid">
    <div class="col">
      <div class="card profile-card">
        <div class="pf">
          <div class="pf-icon"><img src="${p.icon}" alt=""><span class="lvl">${p.level}</span></div>
          <div class="grow"><div class="pf-name">${esc(p.name)} <span class="muted">#${esc(p.tag)}</span></div>
            <div class="small muted">Nível ${p.level} · ${p.levelPct}% para o próximo</div>
            <div class="bar" style="margin-top:5px;height:5px"><i style="width:${p.levelPct}%;background:var(--gold)"></i></div></div>
        </div>
        <div class="row small" style="margin-top:10px;justify-content:space-between"><span><span class="dot ${c.connected ? 'on' : ''}"></span>${c.connected ? esc(PHASE_PT[c.phase] ?? c.phase)
          : `Cliente fechado · dados de ${new Date(p.updatedAt).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}`}</span>
          <label class="switch"><input type="checkbox" data-set="autoAccept" ${S.settings.autoAccept ? 'checked' : ''}> Aceitar partidas</label></div>
      </div>
      <div class="card"><h2>Elo</h2><div class="ranks">${rankCard(solo, 'Solo/Duo')}${rankCard(flex, 'Flex')}</div></div>
      <div class="card"><h2>Últimas ${games.length} partidas</h2>
        <div class="sum">
          <div><div class="big ${wrCls(games.length ? wins / games.length : 0)}">${games.length ? pct(wins / games.length, 0) : '—'}</div><div class="small muted">${wins}V ${games.length - wins}D</div></div>
          <div><div class="big">${kdaRatio(k, d, a)}</div><div class="small muted">KDA médio</div></div>
          <div><div class="big">${games.length ? (k / games.length).toFixed(1).replace('.', ',') : 0}/${games.length ? (d / games.length).toFixed(1).replace('.', ',') : 0}/${games.length ? (a / games.length).toFixed(1).replace('.', ',') : 0}</div><div class="small muted">média por partida</div></div>
        </div>
        <div class="tops">${top.map((x) => `<div class="top click" data-act="champ" data-c="${x.id}">${champImg(x.id, 'c30')}<div><b class="small">${esc(cname(x.id))}</b><div class="small muted">${x.n} jogo${x.n > 1 ? 's' : ''} · <span class="${wrCls(x.w / x.n)}">${pct(x.w / x.n, 0)}</span></div></div></div>`).join('')}</div>
        <div class="small muted one" style="margin-top:6px">${Object.entries(byQueue).sort((x, y) => y[1].n - x[1].n).map(([q, v]) => `${esc(q)}: ${v.w}V ${v.n - v.w}D`).join(' · ')}</div>
      </div>
      <div class="card"><h2>Maiores maestrias</h2><div class="masts">${p.mastery.map((m) => `<div class="mast click" data-act="champ" data-c="${m.championId}">${champImg(m.championId, 'c34')}<b class="small">${esc(cname(m.championId))}</b><span class="small muted">${num(Math.round(m.points / 1000))} mil · nv ${m.level}</span></div>`).join('')}</div></div>
    </div>
    <div class="col"><div class="card grow-card"><h2>Partidas recentes</h2><div class="scroll">${rows || '<div class="muted">Sem partidas recentes.</div>'}</div></div></div>
  </div></div>`;
}

// ---------- seleção de campeões (tudo cabe na janela: colunas de altura fixa)
function slotHtml(s, opp, enemy) {
  const cls = [s.isMe ? 'me' : '', opp ? 'opp' : '', !s.locked && s.championId ? 'hover' : '', s.championId ? 'click' : ''].join(' ');
  const noLanes = S.state?.mode && S.state.mode !== 'classic';
  const pos = s.position ? `${POS_SHORT[s.position]}${s.positionConfidence != null ? `<br><span class="muted">${Math.round(s.positionConfidence * 100)}%</span>` : ''}` : '?';
  const click = s.championId ? ` data-act="champ" data-c="${s.championId}" data-p="${s.position ?? ''}"` : '';
  return `<div class="slot ${cls}"${click}${partyAttr(s.party, enemy)}>
    ${noLanes ? '' : `<div class="pos">${pos}</div>`}
    ${s.championId ? champImg(s.championId) : '<div class="ph"></div>'}
    <div class="grow"><div class="nm">${s.championId ? esc(cname(s.championId)) : '<span class="muted">Escolhendo…</span>'}${s.isMe ? '<span class="tag me">você</span>' : ''}${partyTag(s.party, enemy)}</div>
      <div class="small muted one">${[s.name ? esc(s.name) : '', rankText(s.rank, true)].filter(Boolean).join(' · ')}</div></div></div>`;
}

/** Sugestão: campeão e motivos à esquerda, números em colunas à direita (ocupa a largura toda). */
function sugRow(s, i, opts) {
  const reasons = s.reasons.map((r) => `<span class="chip ${/Desvantagem|Fraco|Sem estat/.test(r) ? 'r' : /domina/.test(r) ? 'y' : 'g'}">${esc(r)}</span>`).join('') || '<span class="muted small">Escolha estável no meta</span>';
  const col = (v, label, cls = '') => `<div class="sc"><b class="${cls}">${v}</b><span>${label}</span></div>`;
  const cols = opts.arena
    ? col(s.avgPlace ? s.avgPlace.toFixed(1).replace('.', ',') + 'º' : '—', 'posição média') + col(pct(s.winRate, 0), 'top 4', wrCls(s.winRate)) + col(pct(s.pickRate), 'escolha')
    : col(pct(s.winRate), 'vitórias', wrCls(s.winRate))
      + (opts.oppName ? col(s.matchupWinRate != null ? pct(s.matchupWinRate) : '—', 'vs ' + esc(opts.oppName), s.matchupWinRate != null ? wrCls(s.matchupWinRate) : 'muted') : '')
      + col(pct(s.pickRate), 'escolha');
  return `<div class="sug2 ${opts.preview === s.championId ? 'sel' : ''} ${s.source === 'seu' ? 'mine' : ''}" data-act="preview" data-c="${s.championId}">
    <span class="n">${i + 1}</span>${champImg(s.championId)}
    <div class="grow"><div class="l1"><b>${esc(cname(s.championId))}</b> ${tierBadge(s.tier)}
      ${s.source === 'seu' ? '<span class="tag me">seu</span>' : s.source === 'banco' ? '<span class="tag">banco</span>' : ''}${!s.owned && !opts.random ? '<span class="tag">não tem</span>' : ''}</div>
      <div class="l2">${reasons}</div></div>
    <div class="nums">${cols}</div>
    ${s.source === 'banco' ? `<button class="btn pri sm" data-act="bench" data-c="${s.championId}">Pegar</button>` : ''}
  </div>`;
}

function csHead(cs, extra) {
  return `<div class="cs-head">
    ${cs.secondsLeft ? `<div class="timer tnum">${cs.secondsLeft}s</div>` : ''}
    <div class="grow">${cs.mode && cs.mode !== 'classic' ? `<div class="kicker">${esc(MODE_NAME[cs.mode] ?? cs.mode)}</div>` : ''}<h1>${esc(cs.stage || 'Seleção de campeões')}</h1>${extra.sub ?? ''}</div>
    ${extra.right ?? ''}
    <div class="toggles"><div class="small muted">Com o campeão definido</div>
      ${extra.runes !== false ? `<label class="switch"><input type="checkbox" data-set="autoRunes" ${S.settings.autoRunes ? 'checked' : ''}> Runas e feitiços</label>` : ''}
      <label class="switch"><input type="checkbox" data-set="autoItemSet" ${S.settings.autoItemSet ? 'checked' : ''}> Build na loja</label></div>
  </div>`;
}

/** Campeão da coluna da build: o que você clicou na lista; senão o seu; senão a 1ª sugestão. */
function csPreviewChamp(cs) {
  if (S.csPrev && S.csPrev.mine !== cs.myChampionId) S.csPrev = null; // você trocou de campeão: volta para o seu
  return S.csPrev?.c ?? (cs.myChampionId || cs.suggestions[0]?.championId || 0);
}

/** Coluna da build na seleção (sempre preenchida: seu campeão ou a prévia de uma sugestão). */
function csBuild(cs, pos, mode) {
  const champ = csPreviewChamp(cs);
  if (!champ) return '<div class="card grow-card"><div class="muted">A build aparece assim que houver sugestões ou o seu campeão for escolhido.</div></div>';
  const mine = champ === cs.myChampionId;
  const key = buildKey(champ, pos, mode);
  const b = mine && cs.myBuild ? cs.myBuild : S.builds[key];
  const idx = cs.suggestions.findIndex((x) => x.championId === champ);
  const title = mine ? (cs.myLocked ? 'Seu campeão' : 'Seu campeão <span class="nocaps muted">(ainda não confirmado)</span>')
    : `Prévia${idx >= 0 ? ` <span class="nocaps muted">· sugestão nº ${idx + 1}</span>` : ''}`;
  const back = !mine && cs.myChampionId ? `<button class="btn sm" data-act="preview" data-c="0" style="margin-left:auto">Voltar ao seu</button>` : '';
  let body;
  if (b) body = buildCompact(b, { spells: mode !== 'arena', opp: cs.laneOpponent?.championId });
  else if (b === false) body = `<div class="muted">Sem estatísticas de ${esc(cname(champ))} para esta rota.</div>`;
  else {
    if (!S.loading[key]) getBuild(champ, pos, mode).then((r) => { if (!r) S.builds[key] = false; if (S.page === 'live') renderPage(); });
    body = `<div class="muted">Carregando a build de ${esc(cname(champ))}…</div>`;
  }
  return `<div class="card grow-card build-card ${mine ? 'mine' : ''}"><h2>${title}${back}</h2><div class="scroll">${body}</div></div>`;
}

function bansCard(cs, myPos) {
  if (!cs.banSuggestions.length) return '';
  return `<div class="card ${cs.myTurnToBan ? 'hl' : ''}"><h2>Sugestões de ban ${cs.myTurnToBan ? '<span class="gold nocaps">— sua vez</span>' : ''}</h2>
    <div class="counters wide">${cs.banSuggestions.map((b) => `<div class="counter click" data-act="champ" data-c="${b.championId}" data-p="${myPos}" title="${esc(b.reasons.join('\n'))}">${champImg(b.championId, 'c34')}<span>${esc(cname(b.championId))}</span>
      <span class="small ${b.matchupWinRate != null ? 'bad' : 'muted'}">${b.matchupWinRate != null ? 'você ' + pct(b.matchupWinRate, 0) : pct(b.winRate, 0)}</span></div>`).join('')}</div></div>`;
}

function renderModeSelect(cs) {
  const mode = cs.mode;
  const random = mode === 'aram' || mode === 'mayhem';
  const mates = mode === 'arena' ? cs.allies.filter((a) => !a.isMe && a.championId) : [];
  const title = random ? 'Seu campeão e o banco' : mates.length ? `Melhores com ${esc(mates.map((m) => cname(m.championId)).join(' e '))}` : `Melhores no ${esc(MODE_NAME[mode])}`;
  const pos = mode.toUpperCase();
  const preview = csPreviewChamp(cs);
  const sugs = cs.suggestions.slice(0, random ? 8 : 12).map((s, i) => sugRow(s, i, { arena: mode === 'arena', random, pos, mode, preview })).join('');
  return `<div class="fit">
  ${csHead(cs, {
    sub: random && cs.rerollsRemaining ? `<div class="sub">${cs.rerollsRemaining} rerrolagem(ns) disponível(is)</div>` : '',
    right: mates.length ? `<div class="opp-card ally">${mates.map((m) => champImg(m.championId, 'c34')).join('')}<div><div class="small muted">${mates.length > 1 ? 'Seu trio' : 'Sua dupla'}</div><b>${esc(mates.map((m) => cname(m.championId)).join(' + '))}</b></div></div>` : '',
    runes: mode !== 'arena',
  })}
  ${cs.notice ? `<div class="banner slim">${esc(cs.notice)}</div>` : ''}
  <div class="cs-grid">
    <div class="col teams">
      <div class="card team-card grow-card t-ally"><h2><span class="ally">${mode === 'arena' ? arenaTxt(cs.allies.length, 0).mine : 'Seu time'}</span></h2><div class="slots">${cs.allies.map((s) => slotHtml(s, false, false)).join('')}</div>${dmgBar(cs.allyDamage, '')}</div>
      ${cs.enemies.some((e) => e.championId) ? `<div class="card team-card grow-card t-enemy"><h2><span class="enemy">Inimigo</span></h2><div class="slots">${cs.enemies.map((s) => slotHtml(s, false, true)).join('')}</div></div>` : ''}</div>
    <div class="col"><div class="card grow-card ${cs.myTurnToPick ? 'hl' : ''}"><h2>${title}<span class="nocaps muted small" style="margin-left:auto">clique para ver a build</span></h2><div class="sug-list">${sugs || '<div class="muted">Sem sugestões ainda.</div>'}</div></div></div>
    <div class="col">${csBuild(cs, pos, mode)}</div>
  </div></div>`;
}

/**
 * Oponente de rota. Na ranqueada o cliente não diz a rota dos inimigos: o app mostra a chance de cada um
 * estar na sua rota e você confirma com um clique (as sugestões passam a usar só ele).
 */
function oppCard(cs, opp, oppName) {
  const picked = cs.enemies.filter((e) => e.championId);
  if (!opp && !picked.length) return '';
  const cands = cs.opponentCandidates ?? [];
  const chance = (cell) => cands.find((c) => c.cellId === cell)?.chance;
  const top = cands[0]?.chance ?? 1;
  const sure = cs.opponentManual || top >= 0.95;
  const label = cs.opponentManual ? 'Oponente de rota <span class="good">· você escolheu</span>'
    : sure ? 'Oponente de rota' : `Oponente provável <span class="warn">· ${pct(top, 0)}</span>`;
  const others = picked.filter((e) => e.cellId !== opp?.cellId).map((e) => {
    const c = chance(e.cellId);
    return `<button class="opp-alt" data-act="opp" data-v="${e.cellId}" title="Marcar ${esc(cname(e.championId))} como meu oponente de rota">${champImg(e.championId, 'c22')}${c != null && !sure ? `<span>${pct(c, 0)}</span>` : ''}</button>`;
  }).join('');
  return `<div class="opp-card ${sure ? '' : 'unsure'}">${opp ? champImg(opp.championId, 'c34') : ''}<div>
      <div class="small muted">${opp ? label : 'Quem é o seu oponente?'}</div>
      ${opp ? `<b>${esc(oppName)}</b>${cs.myMatchup ? ` <span class="${cs.myMatchup.winRate >= 0.5 ? 'good' : 'bad'}">· você ${pct(cs.myMatchup.winRate)}</span>` : ''}` : ''}</div>
    ${others ? `<div class="opp-alts"><span class="small muted">${sure ? 'Trocar' : 'É outro?'}</span>${others}${cs.opponentManual ? '<button class="opp-alt txt" data-act="opp" data-v="-1" title="Voltar a deduzir pelo meta">auto</button>' : ''}</div>` : ''}
  </div>`;
}

function renderChampSelect(cs) {
  if (cs.mode && cs.mode !== 'classic') return renderModeSelect(cs);
  const opp = cs.laneOpponent;
  const oppName = opp ? cname(opp.championId) : '';
  const posButtons = POS.map((p) => `<button class="${cs.myPosition === p ? 'on' : ''}" data-act="pos" data-v="${p}">${POS_NAME[p]}</button>`).join('');
  const myPos = cs.myPosition ?? '';
  const preview = csPreviewChamp(cs);
  // Coluna "vs oponente" só quando há confronto conhecido para alguma sugestão
  const hasMu = cs.suggestions.some((x) => x.matchupWinRate != null);
  const sugs = cs.suggestions.slice(0, 10).map((s, i) => sugRow(s, i, { pos: myPos, mode: 'classic', preview, oppName: hasMu ? oppName : '' })).join('');
  const counters = opp && cs.laneCounters.length ? `<div class="card"><h2>Counters de ${esc(oppName)}</h2>
    <div class="counters wide">${cs.laneCounters.slice(0, 7).map((m) => `<div class="counter click" data-act="preview" data-c="${m.championId}">${champImg(m.championId, 'c34')}
      <span>${esc(cname(m.championId))}</span><span class="small ${wrCls(m.winRate)}">${pct(m.winRate, 0)}</span></div>`).join('')}</div></div>` : '';
  const groups = partyGroups(cs.allies, false) + partyGroups(cs.enemies, true);
  return `<div class="fit">
  ${csHead(cs, {
    sub: `<div class="sub">Sua rota (${esc(cs.positionSource)}): <span class="seg sm">${posButtons}</span></div>`,
    right: oppCard(cs, opp, oppName),
  })}
  ${cs.notice ? `<div class="banner slim">${esc(cs.notice)}</div>` : ''}
  ${groups ? `<div class="groups slim">${groups}</div>` : ''}
  <div class="cs-grid">
    <div class="col teams">
      <div class="card team-card grow-card t-ally"><h2><span class="ally">Seu time</span></h2><div class="slots">${cs.allies.map((s) => slotHtml(s, false, false)).join('')}</div>${dmgBar(cs.allyDamage, '')}</div>
      <div class="card team-card grow-card t-enemy"><h2><span class="enemy">Inimigo</span></h2><div class="slots">${cs.enemies.map((s) => slotHtml(s, opp && s.cellId === opp.cellId, true)).join('')}</div>${dmgBar(cs.enemyDamage, '')}</div>
    </div>
    <div class="col">
      ${cs.myTurnToBan ? bansCard(cs, myPos) : ''}
      <div class="card grow-card ${cs.myTurnToPick ? 'hl' : ''}"><h2>Melhores ${opp ? 'contra ' + esc(oppName) : 'para ' + esc(POS_NAME[cs.myPosition] ?? 'sua rota')}<span class="nocaps muted small" style="margin-left:auto">clique para ver a build</span></h2>
        <div class="sug-list">${sugs || '<div class="muted">Sem sugestões ainda.</div>'}</div></div>
    </div>
    <div class="col side-col">
      ${csBuild(cs, myPos, 'classic')}
      ${counters}
      ${!cs.myTurnToBan ? bansCard(cs, myPos) : ''}
    </div>
  </div></div>`;
}

// ------------------------------------------------------------------ BUILD
/** Itens prismáticos (Arena): os melhores pela taxa de Top 4 (suavizada por amostra), com posição média. */
function prismaticHtml(b, owned = [], n = 6, compact = false) {
  if (!b.prismatic?.length) return '';
  const mean = b.prismatic.reduce((s, x) => s + x.winRate * x.play, 0) / Math.max(1, b.prismatic.reduce((s, x) => s + x.play, 0));
  const best = b.prismatic.filter((x) => x.pickRate >= 0.005 && S.items[x.ids[0]])
    .map((x) => ({ ...x, adj: (x.winRate * x.play + mean * 150) / (x.play + 150) })).sort((p, q) => q.adj - p.adj).slice(0, n);
  return `<h3>Prismáticos recomendados</h3><div class="prisms ${compact ? 'one' : ''}">${best.map((x, i) => {
    const it = S.items[x.ids[0]];
    const have = owned.includes(x.ids[0]);
    return `<div class="prism ${have ? 'have' : ''}" data-tt="i:${x.ids[0]}">
      <div class="item ${have ? 'comprado' : ''}">${itemImg(x.ids[0], 'i28')}</div>
      <div class="grow"><div class="an">${i + 1}. ${esc(it.name)}${have ? ' <span class="tag me">tem</span>' : ''}</div>
      <div class="small muted">${x.avgPlace ? `pos. ${x.avgPlace.toFixed(1).replace('.', ',')} · ` : ''}<span class="${wrCls(x.winRate)}">${pct(x.winRate, 0)}</span> top 4 · ${pct(x.pickRate, 0)}</div></div></div>`;
  }).join('')}</div>`;
}

function augmentsHtml(b, perGroup = 5, layout = '') {
  if (!b.augments?.length) return '';
  const cols = [...b.augments].sort((x, y) => y.rarity - x.rarity).map((g) => {
    const [name, cls] = RARITY[g.rarity] ?? ['Outros', 'silver'];
    const mean = g.augments.reduce((s, a) => s + a.winRate * a.play, 0) / Math.max(1, g.augments.reduce((s, a) => s + a.play, 0));
    const best = g.augments.filter((a) => a.pickRate >= 0.005 && S.augments[a.id])
      .map((a) => ({ ...a, adj: (a.winRate * a.play + mean * 150) / (a.play + 150) })).sort((x, y) => y.adj - x.adj).slice(0, perGroup);
    return `<div class="aug-col"><div class="aug-title ${cls}">${name}</div>${best.map((a) => {
      const info = S.augments[a.id];
      return `<div class="aug" data-tt="a:${a.id}">${info?.icon ? `<img src="${info.icon}" alt="" loading="lazy">` : '<span class="ph2"></span>'}
        <div class="grow"><div class="an">${esc(info?.name ?? '#' + a.id)}</div>
        <div class="small muted">${a.avgPlace ? `pos. ${a.avgPlace.toFixed(1).replace('.', ',')} · ` : ''}<span class="${wrCls(a.winRate)}">${pct(a.winRate, 0)}</span> · ${pct(a.pickRate, 0)}</div></div></div>`;
    }).join('') || '<div class="muted small">Sem dados</div>'}</div>`;
  }).join('');
  return `<h3>Aprimoramentos recomendados</h3><div class="augs ${layout}">${cols}</div>`;
}

const runesRow = (page) => page ? `<div class="runes tight">
  <div class="rtree">${treeImg(page.primaryStyle)}${page.primary.map((id, i) => runeImg(id, i === 0 ? 'key' : 'r24')).join('')}</div>
  <div class="rtree">${treeImg(page.subStyle)}${page.secondary.map((id) => runeImg(id, 'r24')).join('')}</div>
  <div class="small muted">${page.shards.map((id) => esc(SHARDS[id] ?? id)).join(' · ')}</div></div>` : '';

/** Resumo da build para a seleção de campeões (cabe numa coluna, sem rolar). */
function buildCompact(b, opts = {}) {
  const mode = b.mode ?? 'classic';
  const isArena = mode === 'arena';
  const late = b.late.filter((l) => !b.core[0]?.ids.includes(l.ids[0]) && !(S.items[l.ids[0]]?.into?.length)).slice(0, 6);
  const page = b.runes[Math.min(S.runeIdx, b.runes.length - 1)] ?? b.runes[0];
  const skill = b.skills[0];
  const known = (b.counters ?? []).filter((m) => m.play >= 100);
  const strong = [...known].sort((x, y) => y.winRate - x.winRate).slice(0, 5);
  const weak = [...known].sort((x, y) => x.winRate - y.winRate).slice(0, 5);
  const vs = opts.opp ? (b.counters ?? []).find((m) => m.championId === opts.opp) : null;
  const muRow = (list, cls, label) => list.length ? `<div class="bc-row"><span class="lbl ${cls}">${label}</span><div class="mus">${list.map((m) => `<div class="mu click" data-act="champ" data-c="${m.championId}" data-p="${b.position}" title="${esc(cname(m.championId))}: ${pct(m.winRate)} em ${num(m.play)} jogos">${champImg(m.championId, 'c30')}<span class="${wrCls(m.winRate)}">${pct(m.winRate, 0)}</span></div>`).join('')}</div></div>` : '';
  return `<div class="bc-head">${champImg(b.championId, 'c46')}<div class="grow">
      <div><b style="font-size:16px">${esc(cname(b.championId))}</b> <span class="muted">${mode === 'classic' ? POS_NAME[b.position] : esc(MODE_NAME[mode])}</span> ${tierBadge(b.tier)}</div>
      <div class="small muted">${isArena && b.avgPlace ? `pos. média <b>${b.avgPlace.toFixed(2).replace('.', ',')}</b> · 1º ${pct(b.firstRate ?? 0, 0)} · top 4 ${pct(b.winRate, 0)}` : `<b class="${wrCls(b.winRate)}">${pct(b.winRate)}</b> vitórias`} · ${num(b.play)} jogos${vs ? ` · <span class="${wrCls(vs.winRate)}">vs ${esc(cname(vs.championId))} ${pct(vs.winRate)}</span>` : ''}</div></div>
      <button class="btn sm" data-act="champ" data-c="${b.championId}" data-p="${b.position}" data-m="${mode}">Detalhes</button></div>
    ${page ? `<div class="bc-sec">${runesRow(page)}</div>` : ''}
    <div class="bc-row"><span class="lbl">Núcleo</span><div class="items">${(b.core[0]?.ids ?? []).map((id, i) => (i ? '<span class="arrow">›</span>' : '') + itemImg(id)).join('')}</div>
      <span class="small muted">${b.core[0] ? pct(b.core[0].winRate, 0) : ''}</span></div>
    ${b.starters[0] ? `<div class="bc-row"><span class="lbl">Início</span><div class="items">${b.starters[0].ids.map((id) => itemImg(id, 'i30')).join('')}</div>
      ${b.boots.length ? `<span class="lbl" style="margin-left:14px">Botas</span><div class="items">${b.boots.slice(0, 2).map((x) => itemImg(x.ids[0], 'i30')).join('')}</div>` : ''}</div>` : ''}
    <div class="bc-row"><span class="lbl">Finais</span><div class="items">${late.map((l) => itemImg(l.ids[0], 'i30')).join('')}</div></div>
    <div class="bc-row"><span class="lbl">Habilid.</span>${skill?.priority?.length ? `<b class="gold">${skill.priority.join(' › ')}</b>` : '<span class="muted">—</span>'}
      ${opts.spells !== false && b.spells[0] ? `<span class="lbl" style="margin-left:auto;width:auto">Feitiços</span><div class="items">${b.spells[0].ids.map((id) => spellImg(id, 'i30')).join('')}</div>` : ''}</div>
    ${muRow(strong, 'good', 'Forte vs')}${muRow(weak, 'bad', 'Fraco vs')}
    ${isArena ? prismaticHtml(b, [], 4, true) : ''}
    ${b.augments?.length ? augmentsHtml(b, 3, 'stack') : ''}
    <div class="actions tight">
      ${b.runes?.length ? `<button class="btn pri sm" data-act="runes" data-c="${b.championId}" data-p="${b.position}">Importar runas</button>` : ''}
      <button class="btn sm" data-act="itemset" data-c="${b.championId}" data-p="${b.position}">Build na loja</button>
      ${opts.spells && b.spells?.length ? `<button class="btn sm" data-act="spells" data-c="${b.championId}" data-p="${b.position}">Feitiços</button>` : ''}
    </div>`;
}

/** Build completa: janela de detalhes e página Campeões (estas podem rolar). */
function renderBuild(b, opts = {}) {
  const c = S.champs[b.championId];
  const mode = b.mode ?? 'classic';
  const isArena = mode === 'arena';
  const page = b.runes[Math.min(S.runeIdx, b.runes.length - 1)] ?? b.runes[0];
  const sets = (arr, n) => arr.slice(0, n).map((s) => `<div class="bset"><div class="items">${s.ids.map((id, i) => (i ? '<span class="arrow">›</span>' : '') + itemImg(id)).join('')}</div>
    <div class="meta"><b class="${wrCls(s.winRate)}">${pct(s.winRate)}</b> vitórias<br>${pct(s.pickRate)} escolha</div></div>`).join('');
  const strong = b.counters.filter((m) => m.play >= 100).sort((a, z) => z.winRate - a.winRate);
  const counterRow = (list) => `<div class="counters">${list.map((m) => `<div class="counter">${champImg(m.championId, 'sm')}<span>${esc(cname(m.championId))}</span><span class="small ${wrCls(m.winRate)}">${pct(m.winRate)}</span></div>`).join('')}</div>`;
  const skill = b.skills[0];
  return `
  <div class="bhead">${champImg(b.championId, 'lg')}
    <div class="grow"><h1>${esc(c?.name)} <span class="muted" style="font-size:14px;font-weight:500">${mode === 'classic' ? POS_NAME[b.position] : esc(MODE_NAME[mode])}</span> ${tierBadge(b.tier)}</h1>
      <div class="stats">
        ${isArena && b.avgPlace ? `<div class="stat"><small>Posição média</small><b>${b.avgPlace.toFixed(2).replace('.', ',')}</b></div>
          <div class="stat"><small>1º lugar</small><b>${pct(b.firstRate ?? 0)}</b></div>
          <div class="stat"><small>Top 4</small><b class="${wrCls(b.winRate)}">${pct(b.winRate)}</b></div>`
        : `<div class="stat"><small>Vitórias</small><b class="${wrCls(b.winRate)}">${pct(b.winRate)}</b></div>`}
        <div class="stat"><small>Escolha</small><b>${pct(b.pickRate)}</b></div>
        ${b.banRate ? `<div class="stat"><small>Banimento</small><b>${pct(b.banRate)}</b></div>` : ''}
        <div class="stat"><small>Partidas</small><b>${num(b.play)}</b></div>
        <div class="stat"><small>Patch</small><b>${esc(b.patch)}</b></div>
      </div></div></div>
  ${page ? `<h3>Runas ${b.runes.length > 1 ? `<span class="tabs" style="display:inline-flex;margin:0 0 0 8px">${b.runes.slice(0, 3).map((r, i) => `<button class="${r === page ? 'on' : ''}" data-act="runeidx" data-v="${i}">${esc(S.runes[r.primary[0]]?.name ?? 'Opção ' + (i + 1))} · ${pct(r.pickRate, 0)}</button>`).join('')}</span>` : ''}</h3>
  <div class="runes">
    <div class="rtree">${treeImg(page.primaryStyle)}${page.primary.map((id, i) => runeImg(id, i === 0 ? 'key' : '')).join('')}</div>
    <div class="rtree">${treeImg(page.subStyle)}${page.secondary.map((id) => runeImg(id)).join('')}</div>
    <div class="shards">${page.shards.map((id) => `<span>• ${esc(SHARDS[id] ?? id)}</span>`).join('')}</div>
    <div class="small muted">${pct(page.winRate)} vitórias<br>${num(page.play)} jogos</div>
  </div>` : ''}
  ${augmentsHtml(b)}
  ${prismaticHtml(b)}
  <div class="cols2" style="margin-top:6px">
    <div><h3>Itens principais</h3>${sets(b.core, 3)}
      <h3>Opções para os últimos itens</h3>
      <div class="items">${b.late.filter((l) => !b.core[0]?.ids.includes(l.ids[0]) && !(S.items[l.ids[0]]?.into?.length)).slice(0, 8).map((l) => `<div>${itemImg(l.ids[0])}</div>`).join('')}</div></div>
    <div>${b.starters.length ? `<h3>Itens iniciais</h3>${sets(b.starters, 2)}` : ''}
      <h3>Botas</h3><div class="items">${b.boots.slice(0, 3).map((x) => `<div>${itemImg(x.ids[0])}</div>`).join('')}</div>
      ${b.spells.length ? `<h3>Feitiços</h3><div class="items">${b.spells.slice(0, 2).map((x) => `<div class="row" style="gap:4px">${x.ids.map((id) => spellImg(id, '')).join('')}<span class="small muted" style="margin:0 10px 0 4px">${pct(x.pickRate, 0)}</span></div>`).join('')}</div>` : ''}</div>
  </div>
  ${b.synergies?.length ? `<h3>Melhores parceiros</h3><div class="counters">${b.synergies.filter((x) => x.play >= 60)
      .map((x) => ({ ...x, adj: (x.winRate * x.play + b.winRate * 200) / (x.play + 200) })).sort((p, q) => q.adj - p.adj).slice(0, 10)
      .map((x) => `<div class="counter">${champImg(x.championId, 'sm')}<span>${esc(cname(x.championId))}</span><span class="small ${wrCls(x.winRate)}">${x.avgPlace ? x.avgPlace.toFixed(1).replace('.', ',') + 'º' : pct(x.winRate)}</span></div>`).join('')}</div>` : ''}
  ${skill ? `<h3>Habilidades ${skill.priority.length ? `<span class="gold">${skill.priority.join(' › ')}</span>` : ''} <span class="small muted">${pct(skill.winRate)} vitórias</span></h3>${skillGrid(skill.order, opts.skillNow)}` : ''}
  ${strong.length ? `<div class="cols2" style="margin-top:10px">
    <div><h3 class="good">Forte contra</h3>${counterRow(strong.slice(0, 6))}</div>
    <div><h3 class="bad">Fraco contra</h3>${counterRow(strong.slice(-6).reverse())}</div></div>` : ''}
  ${opts.actions ? `<div class="actions">
    ${b.runes?.length ? `<button class="btn pri" data-act="runes" data-c="${b.championId}" data-p="${b.position}">Importar runas</button>` : ''}
    <button class="btn" data-act="itemset" data-c="${b.championId}" data-p="${b.position}">Enviar build para a loja do jogo</button>
    ${opts.spellsAllowed ? `<button class="btn" data-act="spells" data-c="${b.championId}" data-p="${b.position}">Aplicar feitiços</button>` : ''}
    <button class="btn" data-act="opgg" data-c="${b.championId}" data-p="${b.position}" data-m="${mode}">Abrir no op.gg</button></div>` : ''}`;
}

function skillGrid(order, nowIndex, cls = '') {
  const rows = ['Q', 'W', 'E', 'R'].map((k) => `<tr><td class="k">${k}</td>${order.map((o, i) => `<td class="${o === k ? 'on' : ''} ${nowIndex === i && o === k ? 'now' : ''}">${o === k ? i + 1 : ''}</td>`).join('')}</tr>`).join('');
  return `<table class="skills ${cls}">${rows}</table>`;
}

// ------------------------------------------------------------------ PARTIDA (tudo cabe na janela)
function objectivesHtml(o, mode = 'classic') {
  if (mode === 'arena') return '';
  const z = (n) => (n ? '' : ' zero');
  if (mode !== 'classic')
    return `<span class="obj${z(o.towers)}" title="Torres destruídas">${ICON.tower}${o.towers}</span><span class="obj${z(o.inhibitors)}" title="Inibidores destruídos">${ICON.inhib}${o.inhibitors}</span>`;
  const drakes = o.dragons.length
    ? o.dragons.map((t) => `<span class="drake" style="background:${DRAKES[t]?.[1] ?? '#888'}" title="Dragão ${DRAKES[t]?.[0] ?? t}"></span>`).join('') : '0';
  return `<span class="obj${z(o.towers)}" title="Torres destruídas">${ICON.tower}${o.towers}</span>
    <span class="obj${z(o.dragons.length)}" title="Dragões">${ICON.drake}${drakes}</span>
    <span class="obj${z(o.grubs)}" title="Larvas do Vazio">${ICON.grubs}${o.grubs}</span>
    <span class="obj${z(o.heralds)}" title="Arauto do Vale">${ICON.herald}${o.heralds}</span>
    <span class="obj${z(o.barons)}" title="Barão Na'Shor">${ICON.baron}${o.barons}</span>
    <span class="obj${z(o.inhibitors)}" title="Inibidores destruídos">${ICON.inhib}${o.inhibitors}</span>`;
}

const teamWord = (n, plural) => (n >= 3 ? (plural ? 'trios' : 'trio') : plural ? 'duplas' : 'dupla');
/** Textos da Arena com o gênero certo: "Seu trio" / "Sua dupla", "dos outros 5 trios" / "das outras 7 duplas". */
const arenaTxt = (size, others) => size >= 3
  ? { mine: 'Seu trio', avg: `Média dos outros ${others} trios`, above: 'acima da média dos trios', below: 'abaixo da média dos trios' }
  : { mine: 'Sua dupla', avg: `Média das outras ${others} duplas`, above: 'acima da média das duplas', below: 'abaixo da média das duplas' };
function scoreboard(g) {
  const arena = g.mode === 'arena';
  const size = g.arenaTeamSize || Math.max(2, g.allies.length);
  const duos = arena ? Math.max(1, Math.round(g.enemies.length / size)) : 1;
  const at = arenaTxt(size, duos);
  const enemyGold = arena ? Math.round(g.enemyGold / duos) : g.enemyGold;
  const diff = g.allyGold - enemyGold;
  const total = g.allyGold + enemyGold;
  const allyPct = total ? (g.allyGold / total) * 100 : 50;
  const cs = (list) => list.reduce((s, p) => s + p.cs, 0);
  const ao = g.allyObjectives, eo = g.enemyObjectives;
  const modeLbl = g.mode && g.mode !== 'classic' ? MODE_NAME[g.mode] : POS_NAME[g.myPosition] ?? '';
  return `<div class="scoreboard slim">
    <div class="sb-team">
      <div class="lbl ally">${arena ? at.mine : 'Seu time'}</div>
      <div class="row" style="gap:10px"><div class="gold-total tnum"><span class="coin lg"></span>${num(g.allyGold)}</div>
        <div class="kills tnum">${ao.kills} abates${arena ? '' : ` · ${num(cs(g.allies))} CS`}</div></div>
      <div class="objs">${objectivesHtml(ao, g.mode)}</div>
    </div>
    <div class="sb-mid">
      <div class="clock">${mmss(g.gameTime)} · ${esc(cname(g.me.championId))} · ${esc(modeLbl)}</div>
      <div class="diff tnum ${sgnCls(diff)}">${signed(diff)}</div>
      <div class="tug"><i style="left:0;width:${allyPct}%;background:var(--ally)"></i><i style="left:${allyPct}%;right:0;background:var(--enemy)"></i></div>
      <div class="cap">${arena ? (diff >= 0 ? at.above : at.below) : diff >= 0 ? 'vantagem do seu time' : 'vantagem do inimigo'}</div>
    </div>
    <div class="sb-team r">
      <div class="lbl enemy">${arena ? at.avg : 'Inimigo'}</div>
      <div class="row" style="gap:10px;justify-content:flex-end"><div class="kills tnum">${arena ? `${Math.round(eo.kills / duos)} abates` : `${eo.kills} abates · ${num(cs(g.enemies))} CS`}</div>
        <div class="gold-total tnum"><span class="coin lg"></span>${num(enemyGold)}</div></div>
      <div class="objs">${objectivesHtml(eo, g.mode)}</div>
    </div>
  </div>`;
}

/** Jogador numa linha compacta (lado direito espelhado). */
function playerSide(p, side, enemy) {
  if (!p) return `<div class="pl ${side}"><span class="muted">—</span></div>`;
  const items = p.items.filter((x) => S.items[x]?.tags?.includes('Trinket') !== true);
  const click = `data-act="champ" data-c="${p.championId}" data-p="${p.position ?? ''}"`;
  const arena = S.state?.game?.mode === 'arena';
  return `<div class="pl ${side}"${partyAttr(p.party, enemy)}>
    <div class="av click ${p.isDead ? 'dead' : ''}" ${click}>${champImg(p.championId, 'c34')}<span class="lv">${p.level}</span>${p.isDead ? `<span class="rsp">${Math.ceil(p.respawn)}</span>` : ''}</div>
    <div class="who grow">
      <div class="nm"><span class="click" ${click}>${esc(cname(p.championId))}</span>${p.isMe ? '<span class="tag me">você</span>' : ''}${partyTag(p.party, enemy)}</div>
      <div class="sm2">${esc(p.riotId.split('#')[0])}${p.rank ? ' · ' + rankText(p.rank, true) : ''}</div>
    </div>
    <div class="kda tnum"><b>${p.kills}/${p.deaths}/${p.assists}</b>${arena ? '' : `<span>${p.cs} CS</span>`}</div>
    <div class="items">${[0, 1, 2, 3, 4, 5].map((i) => itemImg(items[i] ?? 0, 'i20')).join('')}</div>
    <div class="pgold tnum"><span class="coin"></span>${num(p.itemGold)}</div>
  </div>`;
}

function lanesBoard(g) {
  const rows = POS.map((pos) => ({ pos, a: g.allies.find((p) => p.position === pos), e: g.enemies.find((p) => p.position === pos) })).filter((r) => r.a || r.e);
  const extraA = g.allies.filter((p) => !POS.includes(p.position)), extraE = g.enemies.filter((p) => !POS.includes(p.position));
  for (let i = 0; i < Math.max(extraA.length, extraE.length); i++) rows.push({ pos: null, a: extraA[i], e: extraE[i] });
  const max = Math.max(1500, ...rows.map((r) => Math.abs((r.a?.itemGold ?? 0) - (r.e?.itemGold ?? 0))));
  return `<div class="card lanes-board">${rows.map((r) => {
    const d = (r.a?.itemGold ?? 0) - (r.e?.itemGold ?? 0);
    const w = Math.min(50, (Math.abs(d) / max) * 50);
    const dcs = (r.a?.cs ?? 0) - (r.e?.cs ?? 0);
    const dlv = (r.a?.level ?? 0) - (r.e?.level ?? 0);
    return `<div class="lane ${r.a?.isMe ? 'mine' : ''}">
      ${playerSide(r.a, 'l', false)}
      <div class="ldiff"><div class="small muted b">${r.pos ? POS_SHORT[r.pos] : ''}</div>
        <div class="v tnum ${sgnCls(d)}">${signed(d)}</div>
        <div class="bar2"><i style="${d >= 0 ? `left:50%;width:${w}%;background:var(--ally)` : `right:50%;width:${w}%;background:var(--enemy)`}"></i></div>
        <div class="ex tnum">CS <span class="${sgnCls(dcs)}">${signed(dcs)}</span> · nv <span class="${sgnCls(dlv)}">${signed(dlv)}</span></div></div>
      ${playerSide(r.e, 'r', true)}
    </div>`;
  }).join('')}</div>`;
}

function teamsBoard(g) {
  const sorted = (l) => [...l].sort((a, b) => b.itemGold - a.itemGold);
  const col = (list, enemy, title) => `<div class="card lanes-board"><h2><span class="${enemy ? 'enemy' : 'ally'}">${title}</span></h2>
    ${sorted(list).map((p) => `<div class="lane one ${p.isMe ? 'mine' : ''}">${playerSide(p, 'l', enemy)}</div>`).join('')}</div>`;
  return `<div class="cols2e">${col(g.allies, false, 'Seu time')}${col(g.enemies, true, 'Inimigo')}</div>`;
}

/** Arena: adversários em blocos pequenos (a partida não informa os times deles). */
function arenaFoes(g) {
  return `<div class="card grow-card"><h2><span class="enemy">Adversários (${g.enemies.length})</span></h2><div class="scroll"><div class="foes">${[...g.enemies].sort((a, b) => b.itemGold - a.itemGold).map((p) =>
    `<div class="foe click ${p.isDead ? 'dead' : ''}" data-act="champ" data-c="${p.championId}" title="${esc(p.riotId.split('#')[0])}"${partyAttr(p.party, true)}>${champImg(p.championId, 'c30')}
      <div class="grow"><div class="an">${esc(cname(p.championId))}</div><div class="small muted tnum">${p.kills}/${p.deaths}/${p.assists} · <span class="coin"></span>${num(p.itemGold)}</div></div></div>`).join('')}</div></div></div>`;
}

/** Próxima compra, build principal, finais e habilidades. */
function buildPanel(g) {
  const me = g.me;
  const next = g.nextItem ? S.items[g.nextItem] : null;
  const order = g.build?.skills?.[0]?.order;
  const opp = g.laneOpponent;
  const arena = g.mode === 'arena';
  return `<div class="card hl grow-card"><h2>Sua build ${opp && g.myMatchup ? `<span class="muted nocaps">· vs ${esc(cname(opp.championId))} <b class="${wrCls(g.myMatchup.winRate)}">${pct(g.myMatchup.winRate)}</b></span>` : ''}</h2>
    <div class="scroll">
    ${next ? `<div class="next">${itemImg(g.nextItem, 'i44 lg')}<div class="grow"><div class="small muted">Próxima compra</div><div class="nx">${esc(next.name)}</div><div class="small muted"><span class="coin"></span>${num(next.gold)}</div></div>
      ${g.nextSkill ? `<div class="nsk"><div class="small muted">Habilidade</div><div class="big gold">${g.nextSkill}</div></div>` : ''}</div>` : '<div class="muted small">Build principal completa.</div>'}
    <div class="bc-row"><span class="lbl">Núcleo</span><div class="items">${g.corePath.map((s, i) => (i ? '<span class="arrow">›</span>' : '') + itemStep(s, 'i30')).join('')}${g.boots ? '<span class="arrow">+</span>' + itemStep(g.boots, 'i30') : ''}</div>
      <span class="small muted">${g.sameAsShop ? '<span class="good" title="Os mesmos itens do conjunto AL na loja do jogo">= loja do jogo</span>' : g.build ? '<span class="warn" title="Nenhuma build do app foi enviada para a loja nesta partida">sem build na loja</span>' : ''}</span></div>
    <div class="bc-row"><span class="lbl">Finais</span><div class="items">${g.lateOptions.map((s) => itemStep(s, 'i26')).join('')}</div></div>
    ${arena && g.build ? prismaticHtml(g.build, me.items, 6) : ''}
    ${!arena && order ? `<div class="bc-row"><span class="lbl">Habilid.</span>${skillGrid(order, g.nextSkillIndex ?? null, 'mini')}</div>` : ''}
    ${!arena && g.build?.runes?.[0] ? runesRow(g.build.runes[0]) : ''}
    ${!g.build ? '<div class="muted small">Sem dados de build para este campeão/modo.</div>' : ''}
    </div></div>`;
}

/** Itens situacionais e tipo de dano, compactos. */
function infoPanel(g) {
  const t = g.threats;
  const sit = g.situational.length ? `<div class="sits">${g.situational.slice(0, 4).map((s) => `<div class="sit">${itemImg(s.itemId, 'i30')}<div class="grow"><div class="an">${esc(S.items[s.itemId]?.name ?? s.itemId)}${g.sameAsShop ? (s.inShop ? ' <span class="tag good" title="Também está no conjunto AL da loja">na loja</span>' : ' <span class="tag" title="Não está no conjunto da loja: o time inimigo só apareceu depois que a build foi enviada (procure pelo nome na loja)">fora da loja</span>') : ''}</div><div class="small muted one">${esc(s.reason)}</div></div></div>`).join('')}</div>`
    : '<div class="muted small">Nenhuma ameaça específica: siga a build.</div>';
  const chips = (label, ids, cls) => ids.length ? `<span class="small muted">${label}:</span> ${ids.slice(0, 3).map((id) => `<span class="chip ${cls}">${esc(cname(id))}</span>`).join('')} ` : '';
  return `<div class="card"><h2>Situacionais</h2>${sit}
    ${dmgBar({ physical: t.physicalPct, magic: t.magicPct, true: t.truePct }, '<span class="enemy">Dano inimigo</span>')}
    ${dmgBar(g.allyDamage, '<span class="ally">Seu dano</span>')}
    <div class="one" style="margin-top:6px">${chips('Cura', t.healers, 'r')}${chips('Tanques', t.tanks, 'y')}${chips('Invisíveis', t.stealth, '')}</div></div>`;
}

function renderGame(g) {
  const me = g.me;
  if (!me) return '<div class="empty">Lendo a partida…</div>';
  const groups = partyGroups(g.allies, false) + partyGroups(g.enemies, true);
  const groupsRow = groups ? `<div class="groups slim">${groups}</div>` : '';
  if (g.mode === 'arena') {
    const mine = arenaTxt(g.arenaTeamSize || g.allies.length, 0).mine;
    return `<div class="fit">${scoreboard(g)}
      ${g.arenaTeamKnown ? '' : '<div class="banner slim">Não deu para saber o seu time: o app não viu a seleção de campeões. Deixe-o aberto (pode ser na bandeja) antes da próxima.</div>'}
      <div class="game-grid arena">
        <div class="col">${buildPanel(g)}</div>
        <div class="col"><div class="card grow-card"><div class="scroll">${g.build ? augmentsHtml(g.build, 4, 'stack') : '<div class="muted">Sem aprimoramentos.</div>'}</div></div></div>
        <div class="col"><div class="card lanes-board"><h2><span class="ally">${mine}</span></h2>${g.allies.map((p) => `<div class="lane one ${p.isMe ? 'mine' : ''}">${playerSide(p, 'l', false)}</div>`).join('')}</div>
          ${arenaFoes(g)}${infoPanel(g)}</div>
      </div></div>`;
  }
  const board = g.mode && g.mode !== 'classic' ? teamsBoard(g) : lanesBoard(g);
  return `<div class="fit">${scoreboard(g)}${groupsRow}${board}
    <div class="game-grid"><div class="col">${buildPanel(g)}</div><div class="col">${infoPanel(g)}</div></div></div>`;
}

// ------------------------------------------------------------------ TIER LIST
function modeTabs(current, act) {
  return `<div class="tabs mode-tabs">${MODES.map((m) => `<button class="${current === m ? 'on' : ''}" data-act="${act}" data-v="${m}">${esc(MODE_NAME[m])}</button>`).join('')}</div>`;
}

function renderModeTier() {
  const mode = S.tierMode;
  const list = S.modeTier[mode];
  if (!list) {
    call('modeList', { mode }).then((t) => { S.modeTier[mode] = t || []; if (S.page === 'tier') renderPage(); }).catch((e) => toast(e.message));
    return `<h1>Tier list</h1>${modeTabs(mode, 'tmode')}<div class="empty">Carregando…</div>`;
  }
  const arena = mode === 'arena';
  const rows = list.filter((e) => e.pickRate >= 0.002).sort((a, b) => (a.tier || 9) - (b.tier || 9) || (a.rank || 999) - (b.rank || 999));
  return `<h1>Tier list</h1><div class="sub" style="margin:4px 0 14px">${esc(MODE_NAME[mode])} · patch ${esc(S.patch)} · ${esc(regionName(S.settings.region))}</div>
  ${modeTabs(mode, 'tmode')}
  <div class="card" style="padding:6px 8px"><table class="tl"><tr><th>#</th><th>Campeão</th><th>Tier</th>${arena ? '<th>Posição média</th><th>1º lugar</th><th>Top 4</th>' : '<th>Vitórias</th>'}<th>Escolha</th><th>Partidas</th></tr>
  ${rows.map((e, i) => `<tr data-act="openchamp" data-c="${e.championId}" data-m="${mode}"><td class="muted">${i + 1}</td>
    <td><div class="row" style="gap:9px">${champImg(e.championId, 'sm')}<b>${esc(cname(e.championId))}</b></div></td>
    <td>${tierBadge(e.tier)}</td>
    ${arena ? `<td>${e.avgPlace ? e.avgPlace.toFixed(2).replace('.', ',') : '—'}</td><td>${pct(e.firstRate ?? 0)}</td><td class="${wrCls(e.winRate)}">${pct(e.winRate)}</td>` : `<td class="${wrCls(e.winRate)}">${pct(e.winRate)}</td>`}
    <td>${pct(e.pickRate)}</td><td class="muted">${num(e.play)}</td></tr>`).join('')}
  </table></div>`;
}

function renderTier() {
  if (S.tierMode !== 'classic') return renderModeTier();
  if (!S.tier) {
    call('tierList').then((t) => { S.tier = t || []; if (S.page === 'tier') renderPage(); }).catch((e) => toast(e.message));
    return '<div class="empty">Carregando tier list…</div>';
  }
  const sorters = {
    score: (a, b) => (a.tier || 9) - (b.tier || 9) || (a.rank || 999) - (b.rank || 999),
    wr: (a, b) => b.winRate - a.winRate, pr: (a, b) => b.pickRate - a.pickRate, br: (a, b) => b.banRate - a.banRate,
  };
  const rows = S.tier.filter((e) => e.position === S.tierPos && e.pickRate >= 0.002).sort(sorters[S.tierSort]);
  const th = (k, label) => `<th class="${S.tierSort === k ? 'on' : ''}" data-act="tsort" data-v="${k}">${label}</th>`;
  return `<h1>Tier list</h1><div class="sub" style="margin:4px 0 14px">Patch ${esc(S.patch)} · ${esc(tierName(S.settings.tier))} · ${esc(regionName(S.settings.region))}</div>
  ${modeTabs('classic', 'tmode')}
  <div class="tabs">${POS.map((p) => `<button class="${S.tierPos === p ? 'on' : ''}" data-act="tpos" data-v="${p}">${POS_NAME[p]}</button>`).join('')}</div>
  <div class="card" style="padding:6px 8px"><table class="tl"><tr><th>#</th><th>Campeão</th>${th('score', 'Tier')}${th('wr', 'Vitórias')}${th('pr', 'Escolha')}${th('br', 'Banimento')}<th>Partidas</th></tr>
  ${rows.map((e, i) => `<tr data-act="openchamp" data-c="${e.championId}" data-p="${e.position}"><td class="muted">${i + 1}</td>
    <td><div class="row" style="gap:9px">${champImg(e.championId, 'sm')}<b>${esc(cname(e.championId))}</b></div></td>
    <td>${tierBadge(e.tier)}</td><td class="${wrCls(e.winRate)}">${pct(e.winRate)}</td><td>${pct(e.pickRate)}</td><td>${pct(e.banRate)}</td><td class="muted">${num(e.play)}</td></tr>`).join('')}
  </table></div>`;
}

// ------------------------------------------------------------------ DETALHE DO CAMPEÃO (página e janela)
function champDetail(champ, posWanted, posAct, onLoad, mode = 'classic', modeAct = '') {
  const mtabs = modeAct ? `<div class="tabs mode-tabs" style="margin-bottom:6px">${MODES.map((m) => `<button class="${mode === m ? 'on' : ''}" data-act="${modeAct}" data-v="${m}">${esc(MODE_NAME[m])}</button>`).join('')}</div>` : '';
  if (mode !== 'classic') {
    const key = buildKey(champ, null, mode);
    const b = S.builds[key];
    if (!b && !S.loading[key]) getBuild(champ, null, mode).then(onLoad);
    return { tabs: mtabs, pos: null, body: b ? renderBuild(b, { actions: true, spellsAllowed: !!S.state?.champSelect && mode !== 'arena' }) : '<div class="muted">Carregando build…</div>' };
  }
  if (!S.tier) { call('tierList').then((t) => { S.tier = t || []; onLoad(); }); return '<div class="muted">Carregando…</div>'; }
  const positions = S.tier.filter((e) => e.championId === champ).sort((a, b) => b.roleRate - a.roleRate);
  const pos = positions.some((e) => e.position === posWanted) ? posWanted : positions[0]?.position ?? posWanted ?? null;
  const tabs = positions.map((e) => `<button class="${pos === e.position ? 'on' : ''}" data-act="${posAct}" data-v="${e.position}">${POS_NAME[e.position]} · ${pct(e.roleRate, 0)}</button>`).join('');
  const b = pos ? S.builds[`${champ}_${pos}`] : null;
  if (pos && !b && !S.loading[`${champ}_${pos}`]) getBuild(champ, pos).then(onLoad);
  const inSelect = !!S.state?.champSelect;
  return { tabs: mtabs + (tabs ? `<div class="tabs" style="margin:0">${tabs}</div>` : ''), pos, body: b ? renderBuild(b, { actions: true, spellsAllowed: inSelect }) : pos ? '<div class="muted">Carregando build…</div>' : '<div class="muted">Sem dados deste campeão no elo escolhido.</div>' };
}

// Janela por cima da tela (fica fora do <main> para não ser redesenhada a cada atualização da seleção)
function openChamp(champ, pos, mode) {
  const m = mode || (S.state?.mode && S.state.mode !== 'unsupported' ? S.state.mode : 'classic');
  S.modal = { champ, pos: m === 'classic' ? pos || null : null, mode: m };
  S.runeIdx = 0;
  renderModal();
}
function closeModal() { S.modal = null; renderModal(); }
function renderModal() {
  const el = $('#modal');
  if (!S.modal) { el.hidden = true; el.innerHTML = ''; return; }
  const d = champDetail(S.modal.champ, S.modal.pos, 'mpos', () => S.modal && renderModal(), S.modal.mode ?? 'classic', 'mmode');
  const keepScroll = el.querySelector('.modal')?.scrollTop ?? 0;
  el.innerHTML = `<div class="modal" role="dialog">
    <div class="row" style="justify-content:space-between;align-items:flex-start;margin-bottom:10px"><div>${d.tabs ?? ''}</div>
      <button class="btn" data-act="closemodal" title="Fechar (Esc)">Fechar ✕</button></div>
    ${d.body ?? d}</div>`;
  el.hidden = false;
  el.querySelector('.modal').scrollTop = keepScroll;
}
document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && S.modal) closeModal(); });
document.getElementById('modal').addEventListener('click', (e) => { if (e.target.id === 'modal') closeModal(); });

// ------------------------------------------------------------------ CAMPEÕES
function renderChamps() {
  const q = norm(S.query);
  const list = S.champList.filter((c) => !q || norm(c.name).includes(q) || norm(c.key).includes(q));
  if (S.champSel) {
    const d = champDetail(S.champSel, S.champPos, 'cpos', () => S.page === 'champs' && renderPage(), S.champMode, 'cmode');
    return `<h1>Campeões</h1><div class="card" style="margin-top:14px"><div class="row" style="justify-content:space-between;align-items:flex-start"><div>${d.tabs ?? ''}</div><button class="btn" data-act="closechamp">Voltar à lista</button></div>
      <div style="margin-top:10px">${d.body ?? d}</div></div>`;
  }
  return `<h1>Campeões</h1><div class="sub" style="margin:4px 0 14px">Builds, runas, habilidades e confrontos do patch ${esc(S.patch)}.</div>
    <input id="q" class="search" placeholder="Buscar campeão…" value="${esc(S.query)}" autocomplete="off">
    <div class="grid">${list.map((c) => `<button data-act="openchamp" data-c="${c.id}">${champImg(c.id, 'md')}<span>${esc(c.name)}</span></button>`).join('')}</div>`;
}

document.addEventListener('input', (e) => {
  if (e.target.id === 'q') {
    S.query = e.target.value;
    const pos = e.target.selectionStart;
    renderPage();
    const q = $('#q');
    q.focus();
    q.setSelectionRange(pos, pos);
  }
});

// ------------------------------------------------------------------ CONFIGURAÇÕES
function renderSettings() {
  const s = S.settings;
  const opt = (list, v) => list.map(([k, l]) => `<option value="${k}" ${k === v ? 'selected' : ''}>${l}</option>`).join('');
  const sw = (key, label) => `<label class="switch"><input type="checkbox" data-set="${key}" ${s[key] ? 'checked' : ''}> ${label}</label>`;
  return `<h1>Configurações</h1><div class="sub" style="margin:4px 0 16px">Salvas automaticamente.</div>
  <div class="card"><h2>Dados do meta</h2><div class="form">
    <label>Elo das estatísticas<small>Quanto mais alto, mais fiel ao meta competitivo (e menos partidas).</small></label>
    <select data-set="tier">${opt(TIERS, s.tier)}</select>
    <label>Região<small>Mundial reúne todas as regiões.</small></label>
    <select data-set="region">${opt(REGIONS, s.region)}</select>
    <label>Atualizar agora<small>O meta é baixado no máximo a cada 6 horas.</small></label>
    <div><button class="btn" data-act="clearcache">Baixar dados novos</button></div>
  </div></div>
  <div class="card"><h2>Sempre disponível</h2><div class="form">
    <label>Iniciar com o Windows<small>Abre sozinho, escondido na bandeja (perto do relógio), pronto para o atalho.</small></label><div>${sw('startWithWindows', 'Ativado')}</div>
    <label>Atalho da sobreposição<small>Mostra o app por cima do jogo e esconde de novo. Só vale com o jogo, o cliente do LoL ou o app em primeiro plano, para não atrapalhar o “recortar” dos outros programas. O jogo precisa estar em modo “Sem bordas” ou “Janela”.</small></label>
    <select data-set="overlayHotkey">${opt([['Ctrl+X', 'Ctrl + X'], ['Ctrl+Shift+X', 'Ctrl + Shift + X'], ['Alt+X', 'Alt + X'], ['F10', 'F10'], ['', 'Desligado']], s.overlayHotkey ?? 'Ctrl+X')}</select>
    <label>Abrir sozinho na seleção de campeões<small>Se estiver escondido, a janela volta quando a seleção começa.</small></label><div>${sw('autoShow', 'Ativado')}</div>
    <label>Esconder ao começar a partida<small>Consumo praticamente zero durante o jogo; use o atalho para ver.</small></label><div>${sw('minimizeOnGame', 'Ativado')}</div>
  </div>
  <div class="small muted" style="margin-top:12px">O X da janela só esconde o app (ele continua na bandeja). Para fechar de vez: clique com o botão direito no ícone da bandeja → Sair.</div></div>
  <div class="card"><h2>Cliente do LoL</h2><div class="form">
    <label>Aceitar partidas automaticamente<small>Quando a fila encontrar uma partida, o programa aceita por você em até 2 segundos, mesmo escondido.</small></label><div>${sw('autoAccept', 'Ativado')}</div>
    <label>Enviar build para a loja automaticamente<small>Ao confirmar o campeão, cria o conjunto “AL …” na loja do jogo, com itens situacionais contra o time inimigo. É atualizado até o fim da seleção.</small></label><div>${sw('autoItemSet', 'Ativado')}</div>
    <label>Importar runas e feitiços automaticamente<small>Ao confirmar o campeão, aplica as runas e os feitiços de invocador mais usados (o Flash fica na tecla escolhida abaixo). Usa uma página própria “AL …”; se não houver espaço livre, substitui a página atual na primeira vez.</small></label><div>${sw('autoRunes', 'Ativado')}</div>
    <label>Flash na tecla</label><select data-set="flashKey">${opt([['D', 'D'], ['F', 'F']], s.flashKey)}</select>
  </div></div>
  <div class="card"><h2>Sugestões de campeão</h2><div class="form">
    <label>Somente campeões que você tem</label><div>${sw('ownedOnly', 'Ativado')}</div>
    <label>Peso da sua maestria<small>0 ignora a maestria; 2 favorece bastante os campeões que você mais joga.</small></label>
    <div class="row"><input type="range" min="0" max="2" step="0.25" value="${s.masteryWeight}" data-set="masteryWeight"> <span>${String(s.masteryWeight).replace('.', ',')}</span></div>
  </div></div>
  <div class="card"><h2>Desempenho</h2><div class="form">
    <label>Prioridade baixa<small>O programa (e o navegador interno) cede a CPU para o jogo.</small></label><div>${sw('lowPriority', 'Ativado')}</div>
    <label>Esconder na bandeja ao minimizar<small>Escondido, o programa congela a tela e para de ler a partida.</small></label><div>${sw('minimizeToTray', 'Ativado')}</div>
  </div></div>
  <div class="card credits"><b>Analisador LoL v${esc(S.version)}</b> — criado por <b class="gold">Guilherme Silvestrini</b> · <a href="#" data-act="github">github.com/devsilvver</a></div>
  <div class="small muted">Dados: Riot Data Dragon e op.gg. Não é afiliado à Riot Games. Usa apenas as APIs locais oficiais do cliente e do jogo.</div>`;
}

async function saveSettings(patch) {
  const ns = { ...S.settings, ...patch };
  const metaChanged = ns.tier !== S.settings.tier || ns.region !== S.settings.region;
  S.settings = await call('setSettings', { settings: ns });
  if (metaChanged) { S.tier = null; S.modeTier = {}; S.builds = {}; }
  renderStatus();
  renderPage();
}

document.addEventListener('change', (e) => {
  const k = e.target.dataset?.set;
  if (!k) return;
  let v = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
  if (k === 'masteryWeight') v = Number(v);
  saveSettings({ [k]: v });
});

// ------------------------------------------------------------------ ações
document.addEventListener('click', async (e) => {
  const el = e.target.closest('[data-act]');
  if (!el) return;
  const { act, v } = el.dataset;
  const c = Number(el.dataset.c), p = el.dataset.p;
  switch (act) {
    case 'pos': await call('setPosition', { position: v }); break;
    case 'opp': await call('setOpponent', { cellId: Number(v) }); break;
    case 'champ': openChamp(c, p, el.dataset.m); break;
    case 'preview': S.csPrev = c ? { c, mine: S.state?.champSelect?.myChampionId ?? 0 } : null; renderPage(); break;
    case 'bench': toast(await call('benchSwap', { championId: c })); break;
    case 'mmode': S.modal.mode = v; S.runeIdx = 0; renderModal(); break;
    case 'cmode': S.champMode = v; S.runeIdx = 0; renderPage(); break;
    case 'tmode': S.tierMode = v; renderPage(); break;
    case 'mpos': S.modal.pos = v; S.runeIdx = 0; renderModal(); break;
    case 'closemodal': closeModal(); break;
    case 'runeidx': S.runeIdx = Number(v); if (el.closest('#modal')) renderModal(); else renderPage(); break;
    case 'runes': toast(await call('importRunes', { championId: c, position: p, index: S.runeIdx })); break;
    case 'itemset': toast(await call('importItemSet', { championId: c, position: p })); break;
    case 'spells': toast(await call('applySpells', { championId: c, position: p, index: 0 })); break;
    case 'opgg': {
      const key = (S.champs[c]?.key ?? '').toLowerCase();
      const m = el.dataset.m;
      call('openExternal', { url: m && m !== 'classic' && MODE_SLUG[m] ? `https://op.gg/pt/lol/modes/${MODE_SLUG[m]}/${key}/build` : `https://op.gg/pt/lol/champions/${key}/build/${p.toLowerCase()}` });
      break;
    }
    case 'github': call('openExternal', { url: 'https://github.com/devsilvver' }); break;
    case 'tpos': S.tierPos = v; renderPage(); break;
    case 'tsort': S.tierSort = v; renderPage(); break;
    case 'openchamp': S.champSel = c; S.champPos = p || null; S.champMode = el.dataset.m || (p ? 'classic' : S.champMode); S.runeIdx = 0; go('champs'); break;
    case 'closechamp': S.champSel = null; S.champPos = null; S.focusSearch = true; renderPage(); break;
    case 'cpos': S.champPos = v; S.runeIdx = 0; renderPage(); break;
    case 'clearcache': await call('clearCache'); S.tier = null; S.modeTier = {}; S.builds = {}; toast('Dados do meta serão baixados de novo.'); renderPage(); break;
  }
});

init().catch((e) => { $('#main').innerHTML = `<div class="empty"><b>Erro ao iniciar</b>${esc(e.message)}</div>`; });
