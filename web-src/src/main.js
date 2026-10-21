// MESOL card page: the player's own lore card to fill in (the editor), or another player's card to read (the viewer).
// Everything is decided on the server (Server/Page/PageDesk.cs): the page asks, shows the answer, and never holds a
// field its viewer may not see. Keyboard only: OB:MP 1.0 gives pages no mouse. See bridge.js for how it talks to the
// game.
//
// Traits (Lazy, Romantic...) are numbers from 5 to 100: typed once, or rolled with 5d20 by RpRoll on the server (R),
// which shows the dice on its own small board over this page. One reroll, then the trait is fixed for good.
//
// Nearby lists the players within 20 m as this player may see them. Enter opens their card; = offers an introduction
// (or accepts theirs), F a friendship; - declines; Delete takes an introduction or friendship back; B blocks. An offer
// changes nothing until the other player accepts (Server/Relations/Social.cs).

import { connect } from './bridge.js';

const $ = (id) => document.getElementById(id);
const log = (...a) => console.info('[MESOL]', ...a);
const THEME_KEY = 'mesol.theme';
const ASK_TIMEOUT = 6000;   // ms before a request is sent again, once
const VIS_DELAY = 450;      // ms of quiet after Space before the visibility goes to the server
const CONFIRM_MS = 2500;    // second Delete (or Escape over unsaved text) within this long
const ROLL_REVEAL = 4200;   // ms the dice fly on RpRoll's small board before the number shows here
const TRAIT_MIN = 5;
const TRAIT_MAX = 100;

const PARTS = ['identity', 'heritage', 'life', 'look', 'story', 'status', 'traits', 'nearby'];
const PART_NAMES = {
  identity: 'Identity', heritage: 'Background', life: 'Life', look: 'Look', story: 'Story', status: 'Status', traits: 'Traits',
  nearby: 'Nearby',
};
const NEARBY_REFRESH = 3000; // ms between Nearby lists while the section is open
const RELATION_NAMES = {
  stranger: 'A stranger to you', known: 'You know each other', friend: 'Friends', blocked: 'Blocked',
};
const LEVELS = ['public', 'known', 'friends', 'private'];
const LEVEL_NAMES = { public: 'Public', known: 'Known', friends: 'Friends', private: 'Private' };
const LEVEL_HELP = {
  public: 'Everyone on the server sees this.',
  known: 'Players you introduced yourself to see this.',
  friends: 'Only your friends see this.',
  private: 'Only you see this.',
};
const PREVIEW = [
  { level: 'public', name: 'A stranger' },
  { level: 'known', name: 'Someone you met' },
  { level: 'friends', name: 'A friend' },
];
const KIND_NAMES = {
  race: 'Race', subtype: 'Kind', heritage: 'Heritage', homeland: 'Homeland', province: 'Province', region: 'Region',
  settlement: 'Settlement', culture: 'Culture', house: 'House', tribe: 'Tribe', birthsign: 'Birthsign', class: 'Class',
  faith: 'Faith', deity: 'Deity', faction: 'Faction', rank: 'Rank', title: 'Title', upbringing: 'Upbringing',
  standing: 'Standing', occupation: 'Trade',
};
const TYPING_UIS = ['text', 'long', 'pick', 'number'];

const ICONS = {
  public: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.8"/></svg>',
  known: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10" cy="8" r="3.5"/><path d="M3.5 20c.6-4 3.2-6 6.5-6 1.9 0 3.4.6 4.5 1.7"/><path d="M14.5 18.3l2.2 2.2 4.3-4.6"/></svg>',
  friends: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="8.5" cy="8.5" r="3"/><circle cx="16.2" cy="9" r="2.6"/><path d="M2.5 19.5c.5-3.6 2.8-5.5 6-5.5s5.5 1.9 6 5.5"/><path d="M15.5 14.2c2.9-.3 5.3 1.3 6 5.3"/></svg>',
  private: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="5" y="10.5" width="14" height="10" rx="1.5"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/></svg>',
  hint: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/></svg>',
  list: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 6h11M9 12h11M9 18h11"/><circle cx="4.5" cy="6" r="1"/><circle cx="4.5" cy="12" r="1"/><circle cx="4.5" cy="18" r="1"/></svg>',
  // a d20 seen from above: the mark of a trait you can roll
  die: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2.5l8.2 4.75v9.5L12 21.5l-8.2-4.75v-9.5z"/><path d="M12 2.5l-4.6 8.7h9.2zM7.4 11.2l-3.6 5.55M16.6 11.2l3.6 5.55M7.4 11.2L12 21.5l4.6-10.3"/></svg>',
};

const S = {
  open: false,
  mode: 'edit',        // edit (own card) or view (another player's)
  ui: 'browse',        // edit mode: browse, text, long, number, pick, preview
  loaded: false,
  fields: [],
  card: {},
  raceHint: null,
  readOnly: false,
  used: 0,
  maxCard: 16000,
  tab: 0,
  row: 0,
  pick: null,          // { field, options, shown, index, loading, message }
  previewIndex: 0,
  preview: null,
  viewWho: '',
  viewData: null,
  status: { text: '', kind: 'info' },
  pushId: 0,
  confirm: null,       // { what, field, at }
  visTimers: {},
  rolling: null,       // the trait id whose dice are on RpRoll's board
  typing: false,
  nearby: [],          // [{ id, header, meters, relation, hasCard, incoming, outgoing }]
  nearbyLoaded: false,
  viewFrom: null,      // 'nearby' when the viewer was opened from Nearby: Esc goes back there
  viewRelation: '',
  viewId: null,
};
window.__mesol = S;  // test/browser.mjs reads the state from here

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fold = (s) => String(s ?? '').normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
const fieldsOf = (part) => S.fields.filter((f) => f.part === part);
const fieldById = (id) => S.fields.find((f) => f.id === id);
const valueOf = (f) => S.card[f.id] || { empty: true, vis: f.vis };
const current = () => fieldsOf(PARTS[S.tab])[S.row];
const onNearby = () => PARTS[S.tab] === 'nearby';
const person = () => S.nearby[S.row];
const lines = (text) => esc(text).replace(/\n/g, '<br>');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** A word for how strongly a trait drives the character (the server's CardFields.TraitWord). */
function traitWord(n) {
  if (n <= 20) return 'faintly';
  if (n <= 40) return 'somewhat';
  if (n <= 60) return 'notably';
  if (n <= 80) return 'strongly';
  return 'overwhelmingly';
}

/** none, rolled (once, a reroll left), fixed */
function traitState(v) {
  if (v.number == null) return 'none';
  return v.fixed ? 'fixed' : 'rolled';
}

// ---------------------------------------------------------------------------------------------------------------
// requests

let nextId = 1;
const waiting = new Map();

function ask(request, retries = 1) {
  return new Promise((resolve) => {
    const id = nextId++;
    const timer = setTimeout(() => {
      waiting.delete(id);
      if (retries > 0) resolve(ask(request, retries - 1));
      else resolve({ ok: false, message: 'No answer from the server. Try again.' });
    }, ASK_TIMEOUT);
    waiting.set(id, { resolve, timer });
    bridge.ask(id, JSON.stringify(request));
  });
}

function onAnswer(id, json) {
  const w = waiting.get(id);
  if (!w) return;
  clearTimeout(w.timer);
  waiting.delete(id);
  try {
    w.resolve(JSON.parse(json));
  } catch (e) {
    console.error('[MESOL] bad answer', e);
    w.resolve({ ok: false, message: 'The server sent something this page does not understand.' });
  }
}

// ---------------------------------------------------------------------------------------------------------------
// state from the game

function onState(st) {
  bridge.seen(st.seq);
  if (st.pushId > S.pushId && st.push) {
    S.pushId = st.pushId;
    handlePush(st.push);
  }
  setOpen(!!st.open);
}

function handlePush(json) {
  let p;
  try {
    p = JSON.parse(json);
  } catch {
    return;
  }
  if (p.op === 'view') {
    showViewer(p, null);
  } else {
    S.mode = 'edit';
    if (S.ui === 'preview' || S.ui === 'pick') S.ui = 'browse';
    load();
  }
  document.body.dataset.mode = S.mode;
  render();
}

function setOpen(open) {
  if (open === S.open) return;
  S.open = open;
  document.body.classList.toggle('closed', !open);
  if (open) refocus();
  syncTyping();
}

function refocus() {
  if (S.mode === 'edit' && (S.ui === 'text' || S.ui === 'long' || S.ui === 'number')) $('edit')?.focus();
  else if (S.mode === 'edit' && S.ui === 'pick') $('filter')?.focus();
  else if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
}

/** Tells the game whether a box has the keyboard, so End (and RpRoll's Home) move the caret instead. */
function syncTyping() {
  const typing = S.open && S.mode === 'edit' && TYPING_UIS.includes(S.ui);
  if (typing !== S.typing) {
    S.typing = typing;
    bridge.typing(typing);
  }
}

// ---------------------------------------------------------------------------------------------------------------
// loading and editing

async function load() {
  setStatus(S.loaded ? '' : 'Opening your card...', 'busy');
  const a = await ask({ op: 'load' });
  if (!a.ok) {
    setStatus(a.message, 'error');
    return;
  }
  const keepId = S.loaded ? current()?.id : null;
  Object.assign(S, {
    fields: a.fields, card: a.card, raceHint: a.raceHint, readOnly: a.readOnly, used: a.used, maxCard: a.maxCard, loaded: true,
  });
  if (keepId) moveTo(keepId);
  if (S.row >= fieldsOf(PARTS[S.tab]).length) S.row = 0;
  setStatus(a.readOnly ? 'Your card file could not be read, so it cannot be changed right now. Staff can check the server log.' : '', a.readOnly ? 'error' : 'info');
  render();
}

async function change(request, quiet = false) {
  if (!quiet) setStatus('Saving...', 'busy');
  const a = await ask(request);
  applyAnswer(a);
  return a;
}

function applyAnswer(a) {
  if (a.card) {
    S.card = a.card;
    if (typeof a.used === 'number') S.used = a.used;
  }
  setStatus(a.message || (a.ok ? '' : 'That did not work.'), a.ok ? 'done' : 'error');
  render();
}

function moveTo(id) {
  const f = fieldById(id);
  if (!f) return;
  S.tab = PARTS.indexOf(f.part);
  S.row = fieldsOf(f.part).indexOf(f);
}

function setUi(ui) {
  S.ui = ui;
  syncTyping();
}

// ---------------------------------------------------------------------------------------------------------------
// browse mode actions

function startEdit() {
  const f = current();
  if (!f) return;
  if (S.readOnly) return setStatus('Your card cannot be changed right now.', 'error');
  if (f.type === 'catalog' || f.type === 'choice') return openPicker(f);
  if (f.type === 'trait') {
    const state = traitState(valueOf(f));
    if (state === 'rolled') return keepTrait(f);
    if (state === 'fixed') return setStatus(`${f.label} is set for good.`, 'warn');
  }
  setStatus('');
  setUi(f.type === 'longText' ? 'long' : f.type === 'trait' ? 'number' : 'text');
  S.confirm = null;
  renderDetail();
  const input = $('edit');
  input.value = f.type === 'trait' ? '' : (valueOf(f).text || '');
  input.focus();
  input.setSelectionRange(input.value.length, input.value.length);
  countChars();
  renderKeys();
}

async function saveText() {
  const f = current();
  const input = $('edit');
  const text = input.value.trim();
  if (S.ui === 'number') {
    if (!text) return backToBrowse('');
    input.disabled = true;
    const a = await change({ op: 'set', field: f.id, text });
    input.disabled = false;
    if (a.ok) backToBrowse(a.message);
    else input.focus();
    return;
  }
  const before = (valueOf(f).text || '').trim();
  if (text === before) return backToBrowse('');
  input.disabled = true;
  const a = text.length === 0 ? await change({ op: 'clear', field: f.id }) : await change({ op: 'set', field: f.id, text });
  input.disabled = false;
  if (a.ok) backToBrowse(a.message);
  else input.focus();
}

function cancelText() {
  const f = current();
  const input = $('edit');
  const was = S.ui === 'number' ? '' : (valueOf(f).text || '').trim();
  const dirty = input.value.trim() !== was;
  if (dirty && S.ui !== 'number' && !(S.confirm?.what === 'discard' && Date.now() - S.confirm.at < CONFIRM_MS)) {
    S.confirm = { what: 'discard', at: Date.now() };
    return setStatus(S.ui === 'long'
      ? 'Press Esc again to throw away what you wrote, or Ctrl+Enter to save it.'
      : 'Press Esc again to throw away the change, or Enter to save it.', 'warn');
  }
  backToBrowse('');
}

function backToBrowse(message) {
  setUi('browse');
  S.pick = null;
  S.confirm = null;
  if (document.activeElement && document.activeElement !== document.body) document.activeElement.blur();
  if (message !== undefined) setStatus(message, message ? 'done' : 'info');
  render();
}

function cycleVisibility() {
  const f = current();
  if (!f) return;
  if (S.readOnly) return setStatus('Your card cannot be changed right now.', 'error');
  if (f.locked) return setStatus(`${f.label} is always ${LEVEL_NAMES[f.vis]}.`, 'warn');
  const v = valueOf(f);
  const next = LEVELS[(LEVELS.indexOf(v.vis) + 1) % LEVELS.length];
  S.card[f.id] = { ...v, vis: next };
  setStatus(`${f.label}: ${LEVEL_HELP[next]}`, 'info');
  renderList();
  renderDetail();
  clearTimeout(S.visTimers[f.id]);
  S.visTimers[f.id] = setTimeout(() => change({ op: 'vis', field: f.id, vis: next }, true), VIS_DELAY);
}

function clearField() {
  const f = current();
  if (!f || valueOf(f).empty) return;
  if (S.readOnly) return setStatus('Your card cannot be changed right now.', 'error');
  if (f.type === 'trait') return setStatus(`${f.label} is set for good. Only staff can change a trait.`, 'warn');
  if (S.confirm?.what === 'clear' && S.confirm.field === f.id && Date.now() - S.confirm.at < CONFIRM_MS) {
    S.confirm = null;
    change({ op: 'clear', field: f.id });
    return;
  }
  S.confirm = { what: 'clear', field: f.id, at: Date.now() };
  setStatus(`Press Delete again to clear ${f.label}.`, 'warn');
}

function moveRow(delta) {
  const n = onNearby() ? Math.max(1, S.nearby.length) : fieldsOf(PARTS[S.tab]).length;
  S.row = (S.row + delta + n) % n;
  S.confirm = null;
  setStatus('');
  render();
}

function moveTab(delta) {
  S.tab = (S.tab + delta + PARTS.length) % PARTS.length;
  S.row = 0;
  S.confirm = null;
  setStatus('');
  if (onNearby()) loadNearby();
  render();
}

// ---------------------------------------------------------------------------------------------------------------
// Nearby: other players, their cards, offers

async function loadNearby() {
  const a = await ask({ op: 'nearby' });
  if (!a.ok) {
    if (onNearby()) setStatus(a.message, 'error');
    return;
  }
  const keep = person()?.id;
  S.nearby = a.players || [];
  S.nearbyLoaded = true;
  const at = S.nearby.findIndex((p) => p.id === keep);
  S.row = at >= 0 ? at : Math.min(S.row, Math.max(0, S.nearby.length - 1));
  if (onNearby()) render();
}

setInterval(() => {
  if (S.open && S.mode === 'edit' && S.ui === 'browse' && onNearby()) loadNearby();
}, NEARBY_REFRESH);

async function social(op, p, quiet = false) {
  if (!p) return;
  if (!quiet) setStatus('...', 'busy');
  const a = await ask({ op, player: p.id });
  setStatus(a.message || '', a.ok ? 'done' : 'error');
  S.confirm = null;
  await loadNearby();
}

async function openTheirCard(p) {
  if (!p) return;
  const a = await ask({ op: 'view', player: p.id });
  if (!a.ok) return setStatus(a.message, 'error');
  showViewer(a, 'nearby');
}

function confirmTwice(what, p, text) {
  if (S.confirm?.what === what && S.confirm.field === p.id && Date.now() - S.confirm.at < CONFIRM_MS) {
    S.confirm = null;
    return true;
  }
  S.confirm = { what, field: p.id, at: Date.now() };
  setStatus(text, 'warn');
  return false;
}

function nearbyKey(e, k) {
  const p = person();
  switch (k) {
    case 'ArrowDown': moveRow(1); return true;
    case 'ArrowUp': moveRow(-1); return true;
    case 'ArrowRight': moveTab(1); return true;
    case 'ArrowLeft': moveTab(-1); return true;
    case 'Tab': moveTab(e.shiftKey ? -1 : 1); return true;
    case 'Escape': bridge.close(); return true;
    case 'Enter': openTheirCard(p); return true;
    case '=': case '+':
      if (!p) return true;
      if (p.incoming === 'introduce') social('accept', p);
      else social('introduce', p);
      return true;
    case 'f': case 'F':
      if (!p) return true;
      if (p.incoming === 'friend') social('accept', p);
      else social('befriend', p);
      return true;
    case '-':
      if (p?.incoming) social('decline', p);
      return true;
    case 'Delete': case 'Backspace':
      if (!p || p.relation === 'stranger' || p.relation === 'blocked') return true;
      if (p.relation === 'friend') {
        if (confirmTwice('unfriend', p, `Press Delete again to end your friendship with ${p.header}.`)) social('unfriend', p);
      } else if (confirmTwice('forget', p, `Press Delete again to be strangers with ${p.header} again.`)) social('forget', p);
      return true;
    case 'b': case 'B':
      if (!p) return true;
      if (p.relation === 'blocked') social('unblock', p);
      else if (confirmTwice('block', p, `Press B again to block ${p.header}: they will see only your public fields.`)) social('block', p);
      return true;
    default: return commonKey(k);
  }
}

function showViewer(p, from) {
  S.mode = 'view';
  S.viewFrom = from;
  S.viewId = p.id ?? null;
  S.viewWho = p.who || 'Someone';
  S.viewRelation = p.relation || '';
  S.viewData = p.view || null;
  document.body.dataset.mode = S.mode;
  renderViewer();
  syncTyping();
}

/** An offer panel message (offer, clear, note) also reaches the card: refresh Nearby so its marks are right. */
function onPanel(json) {
  if (S.open && onNearby()) loadNearby();
}

// ---------------------------------------------------------------------------------------------------------------
// traits: R rolls 5d20 on the server (RpRoll shows the dice), one reroll; Enter types a value or keeps a roll

async function rollTrait() {
  const f = current();
  if (!f || f.type !== 'trait') return;
  if (S.readOnly) return setStatus('Your card cannot be changed right now.', 'error');
  if (S.rolling) return;
  const state = traitState(valueOf(f));
  if (state === 'fixed') return setStatus(`${f.label} is set for good: no more rolls.`, 'warn');
  S.rolling = f.id;
  setStatus(`${state === 'rolled' ? 'Rerolling' : 'Rolling'} 5d20 for ${f.label}...`, 'busy');
  renderDetail();
  const asked = performance.now();
  const a = await ask({ op: 'roll', field: f.id });
  // the number shows once the dice have landed on RpRoll's board
  if (a.ok && a.roll) await sleep(Math.max(0, ROLL_REVEAL - (performance.now() - asked)));
  S.rolling = null;
  applyAnswer(a);
}

async function keepTrait(f) {
  await change({ op: 'keep', field: f.id });
}

// ---------------------------------------------------------------------------------------------------------------
// the picker: catalog fields (from the server, already filtered for this card) and fixed choices

async function openPicker(f) {
  const needs = f.needs ? fieldById(f.needs) : null;
  if (needs && f.needs !== 'affiliation' && valueOf(needs).empty) {
    return setStatus(`Pick your ${needs.label.toLowerCase()} first.`, 'warn');
  }
  setStatus('');
  setUi('pick');
  S.pick = { field: f, options: [], shown: [], index: 0, loading: f.type === 'catalog', message: '' };
  renderDetail();
  renderKeys();
  $('filter').focus();
  if (f.type === 'choice') {
    S.pick.options = (f.choices || []).map((c) => ({ id: c.id, name: c.name }));
  } else {
    const a = await ask({ op: 'options', field: f.id });
    if (S.ui !== 'pick' || S.pick?.field !== f) return;
    S.pick.loading = false;
    S.pick.options = a.options || [];
    S.pick.message = a.ok ? (a.message || '') : (a.message || 'The list could not be loaded.');
  }
  // start on what the card has now, or on the game's race
  const v = valueOf(f);
  const want = f.type === 'choice' ? v.text : (v.catalogId || (f.id === 'race' ? S.raceHint?.id : null));
  filterOptions();
  const at = S.pick.shown.findIndex((o) => o.id === want);
  if (at >= 0) S.pick.index = at;
  renderOptions();
}

function filterOptions() {
  const q = fold($('filter')?.value || '').trim();
  const opts = S.pick.options;
  if (!q) {
    S.pick.shown = opts;
  } else {
    const hay = (o) => fold(`${o.name} ${KIND_NAMES[o.kind] || ''} ${o.race || ''} ${o.group || ''}`);
    const starts = opts.filter((o) => fold(o.name).startsWith(q));
    const contains = opts.filter((o) => !fold(o.name).startsWith(q) && hay(o).includes(q));
    S.pick.shown = [...starts, ...contains];
  }
  S.pick.index = Math.min(S.pick.index, Math.max(0, S.pick.shown.length - 1));
}

function movePick(delta) {
  const n = S.pick.shown.length;
  if (!n) return;
  S.pick.index = Math.max(0, Math.min(n - 1, S.pick.index + delta));
  renderOptions();
}

async function pickSelected() {
  const p = S.pick;
  const o = p?.shown[p.index];
  if (!o) return;
  const a = p.field.type === 'choice'
    ? await change({ op: 'set', field: p.field.id, text: o.id })
    : await change({ op: 'pick', field: p.field.id, id: o.id });
  if (a.ok) backToBrowse(a.message);
}

// ---------------------------------------------------------------------------------------------------------------
// preview: the card as others see it, cut by the server

async function showPreview(index) {
  setUi('preview');
  S.previewIndex = (index + PREVIEW.length) % PREVIEW.length;
  S.preview = null;
  renderDetail();
  renderKeys();
  const level = PREVIEW[S.previewIndex].level;
  const a = await ask({ op: 'preview', level });
  if (S.ui !== 'preview' || PREVIEW[S.previewIndex].level !== level) return;
  S.preview = a.ok ? a.view : { error: a.message };
  renderDetail();
}

// ---------------------------------------------------------------------------------------------------------------
// keys

document.addEventListener('keydown', (e) => {
  if (!S.open) return;
  const k = e.key;
  const handled = S.mode === 'view' ? viewerKey(e, k) : editorKey(e, k);
  if (handled) {
    e.preventDefault();
    e.stopPropagation();
  }
}, true);

function commonKey(k) {
  if (k === 'v' || k === 'V') return setTheme(document.body.dataset.theme === 'dark' ? 'light' : 'dark'), true;
  if (k === 'k' || k === 'K') return bridge.chat(), true;
  return false;
}

function viewerKey(e, k) {
  const body = $('viewer-body');
  if (k === 'Escape') {
    if (S.viewFrom === 'nearby') {
      // back to the Nearby list, where the viewer came from
      S.mode = 'edit';
      S.viewFrom = null;
      render();
      return true;
    }
    return bridge.close(), true;
  }
  if (k === 'ArrowDown') return body.scrollBy(0, 60), true;
  if (k === 'ArrowUp') return body.scrollBy(0, -60), true;
  if (k === 'PageDown') return body.scrollBy(0, body.clientHeight * 0.8), true;
  if (k === 'PageUp') return body.scrollBy(0, -body.clientHeight * 0.8), true;
  return commonKey(k);
}

function editorKey(e, k) {
  if (S.ui === 'text' || S.ui === 'long' || S.ui === 'number') {
    if (k === 'Escape') return cancelText(), true;
    if (k === 'Tab') return true; // stay in the box
    if (k === 'Enter' && (S.ui !== 'long' || e.ctrlKey)) return saveText(), true;
    if (S.ui === 'number' && k.length === 1 && !/[0-9]/.test(k)) return true; // digits only
    return false; // typing
  }
  if (S.ui === 'pick') {
    if (k === 'Escape') return backToBrowse(''), true;
    if (k === 'ArrowDown') return movePick(1), true;
    if (k === 'ArrowUp') return movePick(-1), true;
    if (k === 'PageDown') return movePick(8), true;
    if (k === 'PageUp') return movePick(-8), true;
    if (k === 'Enter') return pickSelected(), true;
    if (k === 'Tab') return true;
    return false; // typing in the search box
  }
  if (S.ui === 'preview') {
    if (k === 'Escape') return backToBrowse(), true;
    if (k === 'p' || k === 'P' || k === 'ArrowRight' || k === ' ') return showPreview(S.previewIndex + 1), true;
    if (k === 'ArrowLeft') return showPreview(S.previewIndex - 1), true;
    if (k === 'ArrowDown') return $('detail').scrollBy(0, 60), true;
    if (k === 'ArrowUp') return $('detail').scrollBy(0, -60), true;
    return commonKey(k);
  }
  if (!S.loaded) {
    if (k === 'Escape') return bridge.close(), true;
    return commonKey(k);
  }
  if (S.rolling) return k !== 'Escape' || (bridge.close(), true); // the dice are in the air
  if (onNearby()) return nearbyKey(e, k);
  switch (k) {
    case 'ArrowDown': moveRow(1); return true;
    case 'ArrowUp': moveRow(-1); return true;
    case 'ArrowRight': moveTab(1); return true;
    case 'ArrowLeft': moveTab(-1); return true;
    case 'Tab': moveTab(e.shiftKey ? -1 : 1); return true;
    case 'Enter': startEdit(); return true;
    case ' ': cycleVisibility(); return true;
    case 'Delete': case 'Backspace': clearField(); return true;
    case 'r': case 'R':
      if (current()?.type !== 'trait') return false;
      rollTrait();
      return true;
    case 'p': case 'P': showPreview(0); return true;
    case 'Escape': bridge.close(); return true;
    default: return commonKey(k);
  }
}

// ---------------------------------------------------------------------------------------------------------------
// drawing

function render() {
  document.body.dataset.mode = S.mode;
  document.body.dataset.ui = S.ui;
  document.body.dataset.part = PARTS[S.tab];
  renderHead();
  renderTabs();
  renderList();
  if (S.ui === 'browse' || S.ui === 'preview') renderDetail();
  renderKeys();
  renderStatus();
}

function renderHead() {
  const v = (id) => (S.card[id] && !S.card[id].empty ? S.card[id].shown : '');
  const name = [v('firstName'), v('lastName')].filter(Boolean).join(' ');
  const title = v('title');
  $('who').textContent = name ? (title ? `${name}, ${title}` : name) : (S.loaded ? 'A nameless traveller, so far' : '');
  const pct = S.maxCard ? Math.min(100, (S.used / S.maxCard) * 100) : 0;
  $('used').style.width = `${pct}%`;
  $('used-text').textContent = S.loaded ? `${S.used} of ${S.maxCard} characters used` : '';
}

function renderTabs() {
  $('tabs').innerHTML = PARTS.map((p, i) => {
    const filled = p === 'nearby' ? S.nearby.filter((n) => n.incoming).length : fieldsOf(p).filter((f) => !valueOf(f).empty).length;
    const mark = p === 'traits' ? `<i class="tab-die">${ICONS.die}</i>` : p === 'nearby' ? `<i class="tab-die">${ICONS.friends}</i>` : '';
    return `<span class="tab${i === S.tab ? ' on' : ''}">${mark}${PART_NAMES[p]}${filled ? `<small>${filled}</small>` : ''}</span>`;
  }).join('');
}

function visTag(f, vis) {
  return `<span class="vis vis-${vis}${f.locked ? ' locked' : ''}">${ICONS[vis]}<span>${LEVEL_NAMES[vis]}</span></span>`;
}

function traitCell(v) {
  if (v.number == null) return '<span class="unset">not set</span>';
  const state = traitState(v);
  const mark = state === 'rolled' ? '<em class="tag reroll">1 reroll</em>' : '';
  return `<span class="trait-value"><b>${v.number}</b><span class="bar"><span style="width:${v.number}%"></span></span>`
    + `<span class="word">${traitWord(v.number)}</span>${mark}</span>`;
}

function renderNearbyList() {
  const list = $('fields');
  if (!S.nearbyLoaded) {
    list.innerHTML = '<li class="group-head">Looking around...</li>';
    return;
  }
  if (!S.nearby.length) {
    list.innerHTML = '<li class="nobody">Nobody within 20 m. Walk up to someone, and they will show here.</li>';
    return;
  }
  list.innerHTML = S.nearby.map((p, i) => {
    const tag = p.incoming ? `<em class="tag game">${p.incoming === 'friend' ? 'Asks to be friends' : 'Wants to meet you'}</em>`
      : p.outgoing ? '<em class="tag">Waiting for an answer</em>'
        : p.relation !== 'stranger' ? `<em class="tag rel-${p.relation}">${RELATION_NAMES[p.relation]}</em>` : '';
    return `<li class="field person${i === S.row ? ' cursor' : ''}">`
      + `<span class="label"><i class="die">${ICONS[p.relation === 'friend' ? 'friends' : p.relation === 'known' ? 'known' : 'public']}</i>${esc(p.header)}</span>`
      + `<span class="value">${tag}</span><span class="vis">${p.meters} m</span></li>`;
  }).join('');
  list.querySelector('.cursor')?.scrollIntoView({ block: 'nearest' });
}

function renderNearbyDetail(d) {
  const p = person();
  if (!S.nearbyLoaded) {
    d.innerHTML = '<h2>Nearby</h2><p class="kind">Looking around...</p>';
    return;
  }
  if (!p) {
    d.innerHTML = '<h2>Nearby</h2><p class="kind">Players within 20 m of you</p>'
      + '<p class="none">Nobody is close enough. When someone comes near, you can read their card here, introduce yourself, or ask to be friends.</p>';
    return;
  }
  const rel = p.relation;
  let what;
  if (rel === 'blocked') what = 'You blocked this player, or they blocked you. You see only what they show everyone.';
  else if (rel === 'friend') what = 'You are friends: you see what they share with friends, and they see yours.';
  else if (rel === 'known') what = 'You know each other: you see what they share with people they know.';
  else what = 'A stranger: you see only what they show everyone.';
  const keys = [];
  keys.push(`<span><kbd>Enter</kbd>Read their card${p.hasCard ? '' : ' (they have none yet)'}</span>`);
  if (p.incoming === 'introduce') keys.push('<span><kbd>=</kbd>Accept their introduction</span><span><kbd>-</kbd>Decline</span>');
  else if (rel === 'stranger' && !p.outgoing) keys.push('<span><kbd>=</kbd>Introduce yourself. They have to agree.</span>');
  if (p.incoming === 'friend') keys.push('<span><kbd>F</kbd>Accept their friendship</span><span><kbd>-</kbd>Decline</span>');
  else if (rel !== 'friend' && rel !== 'blocked' && !p.outgoing) keys.push('<span><kbd>F</kbd>Ask to be friends</span>');
  if (rel === 'friend') keys.push('<span><kbd>Del</kbd>End the friendship</span>');
  else if (rel === 'known') keys.push('<span><kbd>Del</kbd>Be strangers again</span>');
  keys.push(`<span><kbd>B</kbd>${rel === 'blocked' ? 'Unblock' : 'Block'}</span>`);
  d.innerHTML = `<h2>${esc(p.header)}</h2><p class="kind">${p.meters} m away</p>`
    + `<div class="who-sees"><span class="pill on">${ICONS[rel === 'friend' ? 'friends' : rel === 'known' ? 'known' : 'public']}${RELATION_NAMES[rel]}</span></div>`
    + `<p class="vis-help">${what}</p>`
    + (p.outgoing ? `<p class="game-hint">${ICONS.hint}You made them an offer. It changes nothing until they accept.</p>` : '')
    + `<div class="choices">${keys.join('')}</div>`
    + '<p class="hint">In the game you can also stand in front of someone: = introduces you (or accepts an offer), - declines, Shift+End opens their card.</p>';
}

function renderList() {
  if (onNearby()) return renderNearbyList();
  const list = $('fields');
  const fields = fieldsOf(PARTS[S.tab]);
  let group = null;
  list.innerHTML = fields.map((f, i) => {
    const v = valueOf(f);
    let head = '';
    if (f.group && f.group !== group) {
      group = f.group;
      head = `<li class="group-head">${esc(group)}</li>`;
    }
    let value;
    if (f.type === 'trait') value = traitCell(v);
    else value = v.empty ? '<span class="unset">not set</span>' : esc(v.shown.replace(/\s+/g, ' '));
    const mark = f.type === 'trait' ? `<i class="die">${ICONS.die}</i>`
      : f.type === 'catalog' ? `<i class="from-list" title="from the lore list">${ICONS.list}</i>` : '';
    return `${head}<li class="field${i === S.row ? ' cursor' : ''}${v.empty ? ' unset-row' : ''}${f.type === 'trait' ? ' trait' : ''}" data-id="${esc(f.id)}">`
      + `<span class="label">${mark}${esc(f.label)}</span>`
      + `<span class="value">${value}</span>${visTag(f, v.vis)}</li>`;
  }).join('');
  list.querySelector('.cursor')?.scrollIntoView({ block: 'nearest' });
}

function kindLine(f) {
  if (f.type === 'catalog') return 'Picked from the lore list';
  if (f.type === 'choice') return `One of ${f.choices.length} choices`;
  if (f.type === 'longText') return `Your own words, up to ${f.max} characters, on as many lines as you like`;
  if (f.type === 'trait') return `${esc(f.group)} trait, ${TRAIT_MIN} to ${TRAIT_MAX}. It changes nothing in the game.`;
  return `Your own words, up to ${f.max} characters`;
}

function whoSees(f, vis) {
  const pills = LEVELS.map((l) => `<span class="pill${l === vis ? ' on' : ''}">${ICONS[l]}${LEVEL_NAMES[l]}</span>`).join('');
  const note = f.locked ? `<small>${esc(f.label)} is always ${LEVEL_NAMES[f.vis]}</small>` : '';
  return `<div class="who-sees"><span class="caption">Who sees it</span><div class="pills">${pills}</div>${note}</div>`
    + `<p class="vis-help">${LEVEL_HELP[vis]}</p>`;
}

function currentValue(f, v) {
  if (v.empty) {
    const verb = f.type === 'catalog' ? 'pick from the lore list' : f.type === 'choice' ? 'choose' : 'write it';
    return `<p class="none">Not set yet. Press <kbd>Enter</kbd> to ${verb}.</p>`;
  }
  if (f.type === 'catalog') {
    return `<p class="pick-name">${esc(v.shown)}</p>${v.summary ? `<p class="summary">${esc(v.summary)}</p>` : ''}`;
  }
  if (f.type === 'longText') return `<div class="longtext">${lines(v.shown)}</div>`;
  return `<p class="text">${esc(v.shown)}</p>`;
}

function traitDetail(f, v) {
  const state = traitState(v);
  const rolling = S.rolling === f.id;
  let big;
  if (rolling) big = `<div class="trait-big rolling"><i class="die spin">${ICONS.die}</i><span>The dice are rolling...</span></div>`;
  else if (state === 'none') big = '<div class="trait-big empty"><span>Not set yet</span></div>';
  else {
    big = `<div class="trait-big"><b>${v.number}</b><div><span class="word">${traitWord(v.number)} ${esc(f.label.toLowerCase())}</span>`
      + `<span class="bar"><span style="width:${v.number}%"></span></span></div></div>`;
  }
  const history = (v.rolls || []).map((r, i) => `<li>${i === 0 ? 'Rolled' : 'Rerolled'}: ${r.dice.join(' + ')} = <b>${r.total}</b></li>`).join('');
  let choices;
  if (rolling) choices = '';
  else if (state === 'none') {
    choices = '<div class="choices"><span><kbd>R</kbd>Roll for stats: 5d20 on the dice board</span><span><kbd>Enter</kbd>Type a value yourself</span></div>'
      + '<p class="warn-note">Whichever you choose, it stays. A roll can be rerolled once.</p>';
  } else if (state === 'rolled') {
    choices = '<div class="choices"><span><kbd>Enter</kbd>Keep it</span><span><kbd>R</kbd>Reroll, once. The new number stays for good</span></div>';
  } else {
    choices = `<p class="fixed-note">${ICONS.private}Set for good${v.rolls?.length ? '' : ' (typed in)'}. Only staff can change it.</p>`;
  }
  return `${big}${history ? `<ul class="rolls">${history}</ul>` : ''}${choices}`;
}

/** The tray at the bottom of the right page on a trait: RpRoll's small board lands here; between rolls it shows the last one. */
function renderTray() {
  const tray = $('dice-tray');
  const f = current();
  const show = S.loaded && S.mode === 'edit' && S.ui === 'browse' && f?.type === 'trait';
  tray.hidden = !show;
  if (!show) return;
  const v = valueOf(f);
  const last = (v.rolls || [])[v.rolls?.length - 1];
  tray.classList.toggle('rolling', S.rolling === f.id);
  const state = traitState(v);
  $('tray-state').textContent = state === 'rolled' ? 'One reroll left' : state === 'fixed' ? 'Set for good' : '';
  $('tray-text').innerHTML = last
    ? `<i class="die">${ICONS.die}</i>${last.dice.join(' + ')} = <b>${last.total}</b>`
    : state === 'fixed' ? `<i class="die">${ICONS.die}</i>Typed in: <b>${v.number}</b>`
      : `<i class="die">${ICONS.die}</i>Press <kbd>R</kbd> to roll 5d20`;
}

function renderDetail() {
  renderTray();
  const d = $('detail');
  if (!S.loaded) {
    d.innerHTML = '';
    return;
  }
  if (S.ui === 'preview') return renderPreview(d);
  if (onNearby()) return renderNearbyDetail(d);
  const f = current();
  if (!f) {
    d.innerHTML = '';
    return;
  }
  const v = valueOf(f);
  const head = `<h2>${f.type === 'trait' ? `<i class="die">${ICONS.die}</i>` : ''}${esc(f.label)}</h2><p class="kind">${kindLine(f)}</p>`;
  if (S.ui === 'text' || S.ui === 'long' || S.ui === 'number') {
    let box;
    if (S.ui === 'long') box = `<textarea id="edit" maxlength="${f.max}" spellcheck="false"></textarea>`;
    else if (S.ui === 'number') box = `<input id="edit" class="number" type="text" inputmode="numeric" maxlength="3" spellcheck="false" autocomplete="off" placeholder="${TRAIT_MIN} to ${TRAIT_MAX}">`;
    else box = `<input id="edit" type="text" maxlength="${f.max}" spellcheck="false" autocomplete="off">`;
    const counter = S.ui === 'number'
      ? '<p class="warn-note">Once you press Enter, the value stays for good.</p>'
      : `<p class="counter"><span id="count">0</span> / ${f.max}</p>`;
    d.innerHTML = `${head}${box}${counter}<p class="hint">${esc(f.hint)}</p>`;
    $('edit').addEventListener('input', countChars);
    return;
  }
  if (S.ui === 'pick') {
    d.innerHTML = `${head}<input id="filter" type="text" placeholder="Type to search" spellcheck="false" autocomplete="off">`
      + '<ol id="options" class="options"></ol><div id="option-detail" class="option-detail"></div>';
    $('filter').addEventListener('input', () => {
      S.pick.index = 0;
      filterOptions();
      renderOptions();
    });
    renderOptions();
    return;
  }
  if (f.type === 'trait') {
    d.innerHTML = `${head}${whoSees(f, v.vis)}<div class="current">${traitDetail(f, v)}</div><p class="hint">${esc(f.hint)}</p>`;
    return;
  }
  let raceHint = '';
  if (f.id === 'race' && v.empty && S.raceHint) {
    raceHint = `<p class="game-hint">${ICONS.hint}Your character looks like a <b>${esc(S.raceHint.name)}</b> in the game. Press <kbd>Enter</kbd> to pick.</p>`;
  }
  const needsField = f.needs && f.needs !== 'affiliation' ? fieldById(f.needs) : null;
  const needs = needsField && valueOf(needsField).empty
    ? `<p class="game-hint">${ICONS.hint}Pick your ${esc(needsField.label.toLowerCase())} first: it decides what fits here.</p>` : '';
  d.innerHTML = `${head}${whoSees(f, v.vis)}<div class="current">${currentValue(f, v)}</div>${raceHint}${needs}<p class="hint">${esc(f.hint)}</p>`;
}

function renderOptions() {
  const p = S.pick;
  const list = $('options');
  const info = $('option-detail');
  if (!list || !p) return;
  if (p.loading) {
    list.innerHTML = '<li class="note">Loading the lore list...</li>';
    info.innerHTML = '';
    return;
  }
  if (!p.shown.length) {
    list.innerHTML = `<li class="note">${esc(p.message || 'Nothing matches. Try fewer letters.')}</li>`;
    info.innerHTML = '';
    return;
  }
  const chosen = p.field.type === 'choice' ? valueOf(p.field).text : valueOf(p.field).catalogId;
  let group = null;
  list.innerHTML = p.shown.map((o, i) => {
    let head = '';
    if (o.group && o.group !== group) {
      group = o.group;
      head = `<li class="group-head">${esc(group)}</li>`;
    }
    const tags = [
      o.origin ? '<em class="tag origin">Origin</em>' : '',
      o.historical ? '<em class="tag old">Heritage</em>' : '',
      p.field.id === 'race' && S.raceHint?.id === o.id ? '<em class="tag game">In the game</em>' : '',
    ].join('');
    const kind = o.kind && p.field.type === 'catalog' && KIND_NAMES[o.kind] && KIND_NAMES[o.kind] !== p.field.label && o.kind !== 'province'
      ? `<small>${KIND_NAMES[o.kind]}${o.race ? ` &middot; ${esc(o.race)}` : ''}</small>` : '';
    return `${head}<li class="option${i === p.index ? ' cursor' : ''}${o.id === chosen ? ' chosen' : ''}"><span>${esc(o.name)}${kind}</span>${tags}</li>`;
  }).join('');
  list.querySelector('.cursor')?.scrollIntoView({ block: 'nearest' });
  const o = p.shown[p.index];
  info.innerHTML = o && o.summary
    ? `<b>${esc(o.name)}</b><p>${esc(o.summary)}</p>${o.origin ? '<p class="note">One of the Origins Oblivion Remastered offers when you create a character.</p>' : ''}`
      + (o.historical ? '<p class="note">From before 3E 433: fits as your family\'s heritage, not as something current.</p>' : '')
    : '';
}

function renderView(view) {
  if (!view) return '<p class="none">Nothing to show.</p>';
  if (view.error) return `<p class="none">${esc(view.error)}</p>`;
  const groups = PARTS.map((part) => {
    const items = view.fields.filter((f) => f.part === part);
    if (!items.length) return '';
    return `<h3>${PART_NAMES[part]}</h3><dl>${items.map((f) => `<dt>${esc(f.label)}</dt><dd${f.long ? ' class="long"' : ''}>${lines(f.text)}</dd>`).join('')}</dl>`;
  }).join('');
  return `<p class="card-header">${esc(view.header)}</p>${groups || '<p class="none">Nothing more to see.</p>'}`;
}

function renderPreview(d) {
  const tabs = PREVIEW.map((p, i) => `<span class="pill${i === S.previewIndex ? ' on' : ''}">${ICONS[p.level]}${p.name}</span>`).join('');
  const body = S.preview ? renderView(S.preview) : '<p class="none">Asking the server...</p>';
  d.innerHTML = `<h2>How others see you</h2><div class="who-sees"><div class="pills">${tabs}</div></div>`
    + '<p class="vis-help">Until introductions and friends arrive in a later version, every other player sees you as a stranger.</p>'
    + `<div class="seen">${body}</div>`;
}

function renderViewer() {
  $('viewer-title').textContent = S.viewWho;
  $('viewer-sub').textContent = S.viewRelation && S.viewRelation !== 'stranger'
    ? `${RELATION_NAMES[S.viewRelation] || ''}. Their lore card, as they let you see it`
    : 'Their lore card, as they let you see it';
  $('viewer-keys').innerHTML = S.viewFrom === 'nearby'
    ? '<span><kbd>&uarr;&darr;</kbd>Scroll</span><span><kbd>Esc</kbd>Back to Nearby</span>'
    : '<span><kbd>&uarr;&darr;</kbd>Scroll</span><span><kbd>K</kbd>Chat</span><span><kbd>Esc</kbd>Close</span>';
  $('viewer-body').innerHTML = S.viewData
    ? renderView(S.viewData)
    : `<p class="none">${esc(S.viewWho)} has no lore card yet.</p>`;
  $('viewer-body').scrollTop = 0;
}

const KEYS = {
  browse: [['&uarr;&darr;', 'Field'], ['&larr;&rarr;', 'Section'], ['Enter', 'Edit'], ['Space', 'Who sees it'], ['Del', 'Clear'], ['P', 'Preview'], ['V', 'Light or dark'], ['K', 'Chat'], ['Esc', 'Close']],
  trait: [['&uarr;&darr;', 'Trait'], ['&larr;&rarr;', 'Section'], ['R', 'Roll'], ['Enter', 'Type or keep'], ['Space', 'Who sees it'], ['P', 'Preview'], ['V', 'Light or dark'], ['Esc', 'Close']],
  text: [['Enter', 'Save'], ['Esc', 'Cancel']],
  number: [['Enter', 'Apply for good'], ['Esc', 'Cancel']],
  long: [['Ctrl+Enter', 'Save'], ['Enter', 'New line'], ['Esc', 'Cancel']],
  pick: [['&uarr;&darr;', 'Choose'], ['Enter', 'Pick'], ['Type', 'Search'], ['Esc', 'Back']],
  preview: [['P', 'Next viewer'], ['&uarr;&darr;', 'Scroll'], ['Esc', 'Back']],
  nearby: [['&uarr;&darr;', 'Player'], ['&larr;&rarr;', 'Section'], ['Enter', 'Their card'], ['=', 'Introduce'], ['F', 'Friends'], ['-', 'Decline'], ['B', 'Block'], ['Esc', 'Close']],
};

function renderKeys() {
  const set = S.ui === 'browse' && onNearby() ? KEYS.nearby
    : S.ui === 'browse' && current()?.type === 'trait' ? KEYS.trait : (KEYS[S.ui] || KEYS.browse);
  $('keys').innerHTML = set.map(([key, what]) => `<span><kbd>${key}</kbd>${what}</span>`).join('');
}

function setStatus(text, kind) {
  S.status = { text: text || '', kind: kind || 'info' };
  renderStatus();
}

function renderStatus() {
  const el = $('status');
  el.textContent = S.status.text;
  el.className = `status ${S.status.kind}`;
}

function countChars() {
  const input = $('edit');
  if (input && $('count')) $('count').textContent = String(input.value.length);
}

function setTheme(theme) {
  document.body.dataset.theme = theme;
  try {
    localStorage.setItem(THEME_KEY, theme);
  } catch {
    // no storage: the choice lasts until the page reloads
  }
}

// No mouse in game (OB:MP 1.0): a click must never take the keyboard away from a box or the list. The browser
// preview's own buttons (#preview-bar) still work.
for (const type of ['mousedown', 'mouseup', 'click', 'dblclick', 'contextmenu', 'wheel']) {
  document.addEventListener(type, (e) => {
    if (e.target?.closest?.('#preview-bar')) return;
    e.preventDefault();
    e.stopPropagation();
    if (type === 'mouseup') refocus();
  }, { capture: true, passive: false });
}

// In game a mouse click hands the keyboard to the game (UI-only input) and the card seems to hang. While the card is
// open and has lost the keyboard, ask the game for it back (LorePage.cs gives it back unless chat or the dice board
// has it). Twice a second is quick enough and costs nothing.
window.addEventListener('blur', () => { if (S.open) bridge.lostFocus(); });
setInterval(() => { if (S.open && !document.hasFocus()) bridge.lostFocus(); }, 500);

// ---------------------------------------------------------------------------------------------------------------
// start

try {
  const saved = localStorage.getItem(THEME_KEY);
  if (saved === 'dark' || saved === 'light') document.body.dataset.theme = saved;
} catch {
  // no storage
}
const bridge = connect({ onState, onAnswer, onPanel });
render();
bridge.ready();
log('card page ready');
