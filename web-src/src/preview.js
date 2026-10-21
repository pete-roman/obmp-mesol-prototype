// Browser preview only (preview/index.html loads this before app.js; the mod never ships it). It plays the game and
// the server with the real lore catalog and the real field list, following the rules of Server/Cards/CardEditor.cs
// closely enough to try the page: canon catalog, race-scoped picks, the 3E 433 era rule, model fit, the rank rule.
// The real rules are the server's and are covered by the C# tests; this is a stand-in, not a second implementation.
//
// Controls at the bottom: open the editor (as End or /mylore), show another player's card (as /lore), close.
// End works here too. window.__preview lets test/browser.mjs reach the stand-in server.

import catalog from '../../catalog/mesol-lore.json';
import FIELDS from './preview-fields.json';

const ENTRIES = new Map(catalog.entries.map((e) => [e.id, e]));
const CHOICE_WORDS = { ic: 'in character', ooc: 'out of character', lfrp: 'looking for RP', ask: 'ask first', wed: 'wed under Mara' };
const LEVELS = ['public', 'known', 'friends', 'private'];
const IN_HEADER = new Set(['firstName', 'lastName', 'title', 'seenAs', 'icOoc']);
const GAME_RACE = 'race.khajiit';
const NATIVE = {
  Altmer: 'province.summerset-isles', Argonian: 'province.black-marsh', Bosmer: 'province.valenwood', Breton: 'province.high-rock',
  Dunmer: 'province.morrowind', Imperial: 'province.cyrodiil', Khajiit: 'province.elsweyr', Nord: 'province.skyrim',
  Orsimer: 'province.orsinium', Redguard: 'province.hammerfell',
};
const DEPENDENTS = { race: ['subtype', 'culture', 'upbringing'], homeland: ['homeRegion'] };
const traitWord = (n) => (n <= 20 ? 'faintly' : n <= 40 ? 'somewhat' : n <= 60 ? 'notably' : n <= 80 ? 'strongly' : 'overwhelmingly');
const d20 = () => 1 + Math.floor(Math.random() * 20);
const inside = (e, placeId) => {
  for (let at = e, i = 0; at && i < 6; i++) {
    if (at.id === placeId) return true;
    at = at.parent ? ENTRIES.get(at.parent) : null;
  }
  return false;
};

const card = {};
const field = (id) => FIELDS.find((f) => f.id === id);
const raceName = () => {
  const id = card.race?.catalogId;
  const e = id && ENTRIES.get(id);
  return e ? e.race || e.name : null;
};
const isEmpty = (v) => !v || (!v.catalogId && !v.text && v.number == null);
const display = (f, v) => {
  if (f.type === 'trait' && v.number != null) return `${v.number} (${traitWord(v.number)}, ${v.rolls?.length ? 'rolled' : 'chosen'})`;
  if (v.catalogId) return ENTRIES.get(v.catalogId)?.name || v.catalogId;
  return f.type === 'choice' ? (CHOICE_WORDS[v.text] || v.text) : v.text;
};
const effectiveVis = (f, v) => (f.locked ? f.vis : (v?.vis || f.vis));
const used = () => Object.values(card).reduce((n, v) => n + (v.text?.length || 0), 0);

function values() {
  const out = {};
  for (const f of FIELDS) {
    const v = card[f.id];
    const empty = isEmpty(v);
    out[f.id] = {
      catalogId: v?.catalogId ?? null,
      text: v?.text ?? null,
      shown: empty ? '' : display(f, v),
      summary: v?.catalogId ? ENTRIES.get(v.catalogId)?.summary ?? null : null,
      number: v?.number ?? null,
      fixed: !!v?.fixed,
      rolls: v?.rolls ?? null,
      vis: effectiveVis(f, v),
      empty,
    };
  }
  return out;
}

function allowed(f, e) {
  if (e.era === 'future' || (e.era === 'historical' && !f.historical)) return false;
  if (e.modelFit === 'implausible') return false;
  if (e.race === 'Mixed') return false;
  if (f.raceScoped && e.race && e.race !== raceName()) return false;
  if (f.id === 'homeRegion') return !!card.homeland?.catalogId && inside(e, card.homeland.catalogId);
  if (f.id === 'rank') {
    const faction = card.affiliation?.catalogId;
    return (faction && e.parent === faction) || (e.kind === 'title' && !e.parent);
  }
  return true;
}

function options(f) {
  if (f.needs === 'race' && !raceName()) return { ok: true, field: f.id, options: [], message: 'Pick your race first.' };
  const native = f.id === 'homeland' ? NATIVE[raceName()] : null;
  const list = catalog.entries
    .filter((e) => f.kinds.includes(e.kind) && allowed(f, e))
    .sort((a, b) => (b.remasteredOrigin ? 1 : 0) - (a.remasteredOrigin ? 1 : 0) || a.name.localeCompare(b.name))
    .sort((a, b) => (native ? (a.id === native ? 0 : 1) - (b.id === native ? 0 : 1) : 0))
    .map((e) => ({
      id: e.id, name: e.name, summary: e.summary, kind: e.kind, race: e.race, origin: !!e.remasteredOrigin, historical: e.era === 'historical',
      group: native ? (e.id === native ? 'Native lands' : 'Other Tamrielic Provinces') : null,
    }));
  return { ok: true, field: f.id, options: list, message: list.length ? null : `Nothing in the catalog fits ${f.label} on this card.` };
}

function view(level) {
  const lv = LEVELS.indexOf(level);
  const visible = FIELDS.filter((f) => !isEmpty(card[f.id]) && LEVELS.indexOf(effectiveVis(f, card[f.id])) <= lv);
  const shown = (id) => (visible.some((f) => f.id === id) ? display(field(id), card[id]) : '');
  const name = [shown('firstName'), shown('lastName')].filter(Boolean).join(' ');
  const status = card.icOoc?.text ? ` (${CHOICE_WORDS[card.icOoc.text]})` : '';
  const header = (name ? (shown('title') ? `${name}, ${shown('title')}` : name)
    : shown('seenAs') || (shown('race') ? `a ${shown('race')}` : "someone you don't know")) + status;
  return {
    header,
    level,
    fields: visible.filter((f) => !IN_HEADER.has(f.id)).map((f) => ({ id: f.id, part: f.part, label: f.label, text: display(f, card[f.id]), long: f.type === 'longText' })),
  };
}

const done = (ok, message) => ({ ok, message, card: values(), used: used() });

// ---- other players nearby (Server/Relations/Social.cs plays this in game) ----
// Each one: a card shown at a level (what this player may see), a relation, and offers both ways.
const OTHERS = [
  { id: '7', meters: 3.2, relation: 'stranger', seenAs: 'a scarred Khajiit with a merchant\'s sash', name: "Ri'Zahra, Caravan guard",
    known: [['Race', 'Khajiit'], ['Homeland', 'Elsweyr'], ['Currently', 'Haggling over a saddle.']], friends: [['History', 'Born in Rimmen, raised among caravans.']] },
  { id: '9', meters: 8.4, relation: 'known', seenAs: 'a broad Imperial in Legion boots', name: 'Marcus Varro, Legion veteran',
    known: [['Race', 'Imperial'], ['Affiliation', 'Imperial Legion']], friends: [['Personality', 'Gruff, loyal, owes nobody.']] },
  { id: '12', meters: 15.0, relation: 'stranger', seenAs: 'a hooded Dunmer', name: 'Dravyn Sadri', known: [['Race', 'Dunmer']], friends: [] },
];
const offersIn = new Map();  // other id -> kind they offered this player
const offersOut = new Map(); // other id -> kind this player offered
const other = (id) => OTHERS.find((o) => o.id === id);
const headerOf = (o) => (o.relation === 'known' || o.relation === 'friend' ? o.name : o.seenAs);
function viewOf(o) {
  const fields = [['Seen as', o.seenAs]];
  const level = o.relation === 'friend' ? 2 : o.relation === 'known' ? 1 : 0;
  if (level >= 1) fields.push(...o.known);
  if (level >= 2) fields.push(...o.friends);
  return { header: headerOf(o), level: ['public', 'known', 'friends'][level], fields: fields.slice(1).map(([label, text]) => ({ id: label, part: 'life', label, text, long: false })) };
}
function nearbyList() {
  return { ok: true, range: 20, players: OTHERS.map((o) => ({
    id: o.id, header: headerOf(o), meters: o.meters, relation: o.relation, hasCard: true,
    incoming: offersIn.get(o.id) || null, outgoing: offersOut.get(o.id) || null,
  })) };
}
function panel(json) {
  api.onPanel?.(JSON.stringify(json));
}
function acceptOffer(o, kind) {
  o.relation = kind === 'friend' ? 'friend' : (o.relation === 'friend' ? 'friend' : 'known');
  offersIn.delete(o.id);
  offersOut.delete(o.id);
  const text = kind === 'friend' ? `You and ${headerOf(o)} are friends now.` : `You and ${headerOf(o)} know each other now.`;
  panel({ op: 'note', text });
  return { ok: true, message: text };
}
function social(req) {
  if (req.op === 'nearby') return nearbyList();
  const o = other(req.player);
  if (req.op === 'decline' && !o) return { ok: false, message: 'Nobody is waiting for your answer.' };
  if (!o) return { ok: false, message: 'That player is not here any more.' };
  switch (req.op) {
    case 'view': return { ok: true, id: o.id, who: headerOf(o), relation: o.relation, view: viewOf(o) };
    case 'introduce': case 'befriend': {
      const kind = req.op === 'introduce' ? 'introduce' : 'friend';
      if (o.relation === 'blocked') return { ok: false, message: "You can't make offers to this player." };
      if (kind === 'introduce' && o.relation !== 'stranger') return { ok: false, message: `You already know ${headerOf(o)}.` };
      if (kind === 'friend' && o.relation === 'friend') return { ok: false, message: `You and ${headerOf(o)} are friends already.` };
      if (offersIn.get(o.id) === kind) return acceptOffer(o, kind);
      offersOut.set(o.id, kind);
      // the stand-in player says yes after a moment (window.__preview.autoAccept = false to make them wait)
      if (api.autoAccept) setTimeout(() => { if (offersOut.get(o.id) === kind) acceptOffer(o, kind); }, 1500);
      return { ok: true, message: kind === 'introduce' ? `You offered to introduce yourself to ${headerOf(o)}.` : `You asked ${headerOf(o)} to be friends.` };
    }
    case 'accept': {
      const kind = offersIn.get(o.id);
      return kind ? acceptOffer(o, kind) : { ok: false, message: 'They are not waiting for your answer.' };
    }
    case 'decline':
      offersIn.delete(o.id);
      return { ok: true, message: 'Declined.' };
    case 'forget':
      if (o.relation === 'stranger') return { ok: false, message: `You don't know ${headerOf(o)}.` };
      o.relation = 'stranger';
      return { ok: true, message: `You and ${headerOf(o)} are strangers again.` };
    case 'unfriend':
      if (o.relation !== 'friend') return { ok: false, message: 'You are not friends.' };
      o.relation = 'known';
      return { ok: true, message: `You and ${headerOf(o)} are no longer friends.` };
    case 'block': o.relation = 'blocked'; return { ok: true, message: `Blocked ${o.seenAs}.` };
    case 'unblock': o.relation = 'stranger'; return { ok: true, message: `Unblocked ${o.seenAs}.` };
    default: return { ok: false, message: `Unknown request '${req.op}'.` };
  }
}
const SOCIAL_OPS = new Set(['nearby', 'view', 'introduce', 'befriend', 'accept', 'decline', 'forget', 'unfriend', 'block', 'unblock']);
const word = (v) => v[0].toUpperCase() + v.slice(1);

function handle(req) {
  if (SOCIAL_OPS.has(req.op)) return social(req);
  if (req.op === 'load') {
    return { ok: true, mode: 'canon', readOnly: false, fields: FIELDS, card: values(), raceHint: card.race ? null : { id: GAME_RACE, name: 'Khajiit' }, maxCard: 16000, used: used() };
  }
  if (req.op === 'preview') {
    if (!['public', 'known', 'friends'].includes(req.level)) return { ok: false, message: 'Preview as public, known or friends.' };
    return { ok: true, view: view(req.level) };
  }
  const f = field(req.field);
  if (!f) return { ok: false, message: `There is no field '${req.field}'.` };
  if (req.op === 'options') return options(f);
  const old = card[f.id];
  const vis = old?.vis || f.vis;
  switch (req.op) {
    case 'set': {
      if (f.type === 'catalog') return done(false, `${f.label} is picked from the lore list.`);
      if (f.type === 'trait') {
        if (old?.number != null) return done(false, old.fixed ? `${f.label} is set for good.` : `${f.label} was rolled. Reroll it once, or keep it.`);
        const n = Number(req.text);
        if (!/^\d+$/.test(req.text) || n < 5 || n > 100) return done(false, `${f.label} takes a number from 5 to 100.`);
        card[f.id] = { number: n, fixed: true, vis };
        return done(true, `${f.label} is ${n} (${traitWord(n)}), set for good.`);
      }
      const text = f.type === 'longText' ? req.text.trim() : req.text.replace(/\s+/g, ' ').trim();
      if (!text) return done(false, `Nothing to set.`);
      if (f.type === 'choice' && !f.choices.some((c) => c.id === text)) return done(false, `${f.label} is one of: ${f.choices.map((c) => c.id).join(', ')}.`);
      if (text.length > f.max) return done(false, `${f.label} takes at most ${f.max} characters, this is ${text.length}.`);
      card[f.id] = { text, vis };
      return done(true, `${f.label} set: ${display(f, card[f.id])} [${word(effectiveVis(f, card[f.id]))}].`);
    }
    case 'pick': {
      const e = ENTRIES.get(req.id);
      if (!e || !f.kinds?.includes(e.kind) || !allowed(f, e)) return done(false, `${e?.name || 'That'} does not fit ${f.label} on this card.`);
      const oldPick = DEPENDENTS[f.id] ? old?.catalogId : null;
      card[f.id] = { catalogId: e.id, vis };
      let extra = '';
      if (oldPick && oldPick !== e.id) {
        const cleared = DEPENDENTS[f.id].filter((id) => !isEmpty(card[id]));
        cleared.forEach((id) => { card[id] = { vis: card[id].vis }; });
        if (cleared.length) extra = ` ${cleared.map((id) => field(id).label).join(' and ')} cleared: ${cleared.length === 1 ? 'it' : 'they'} belonged to the old ${f.id === 'race' ? 'race' : f.label.toLowerCase()}.`;
      }
      return done(true, `${f.label} set: ${e.name} [${word(vis)}].${extra}`);
    }
    case 'vis':
      if (f.locked) return done(false, `${f.label} is always ${word(f.vis)}.`);
      card[f.id] = { ...(old || {}), vis: req.vis };
      return done(true, `${f.label} is now ${word(req.vis)}.`);
    case 'roll': {
      if (f.type !== 'trait') return done(false, `${f.label} is not a trait.`);
      if (old?.fixed) return done(false, `${f.label} is set for good: no more rerolls.`);
      const dice = [d20(), d20(), d20(), d20(), d20()];
      const total = dice.reduce((a, b) => a + b, 0);
      const reroll = old?.number != null;
      card[f.id] = { number: total, fixed: reroll, rolls: [...(old?.rolls || []), { dice, total }], vis };
      api.lastRoll = { field: f.id, dice, total };
      const r = done(true, reroll ? `${f.label}: rerolled ${total} (${traitWord(total)}), set for good.` : `${f.label}: rolled ${total} (${traitWord(total)}). Keep it, or reroll once.`);
      r.roll = { dice, total, reroll };
      return r;
    }
    case 'keep':
      if (old?.number == null || old.fixed) return done(false, old?.fixed ? `${f.label} is already set for good.` : `${f.label} has no roll to keep.`);
      card[f.id] = { ...old, fixed: true };
      return done(true, `${f.label} kept at ${old.number}, set for good.`);
    case 'clear':
      if (isEmpty(old)) return done(false, `${f.label} is already empty.`);
      if (f.type === 'trait') return done(false, `${f.label} is set for good. Only staff can change a trait.`);
      card[f.id] = { vis };
      return done(true, `${f.label} cleared.`);
    default:
      return { ok: false, message: `Unknown request '${req.op}'.` };
  }
}

// ---- the game's part: state with resends, End, /mylore and /lore ----

let api = null;

window.MESOL_PREVIEW = {
  connect({ onState, onAnswer, onPanel }) {
    const st = { seq: 0, open: false, pushId: 0, push: null };
    const send = () => { const copy = { ...st }; setTimeout(() => onState(copy), 0); };
    const change = (fn) => { fn(); st.seq++; send(); };
    const show = (push) => change(() => { st.open = true; st.pushId++; st.push = push; });
    api = {
      preview: true,
      typing: (on) => { api.typingNow = !!on; },
      onPanel,
      autoAccept: true,
      // a nearby player makes this one an offer, as Social.MakeOffer does
      offerFrom: (id = '12', kind = 'introduce') => {
        const o = other(id);
        offersIn.set(o.id, kind);
        panel({ op: 'offer', id: Date.now(), kind, from: headerOf(o), seconds: 30, text: `${headerOf(o)} wants to introduce themselves.` });
      },
      others: OTHERS,
      lostFocus: () => {},
      typingNow: false,
      latency: 60,
      card,
      ready: () => send(),
      seen: () => {},
      ask: (id, json) => setTimeout(() => onAnswer(id, JSON.stringify(handle(JSON.parse(json)))), api.latency),
      close: () => change(() => { st.open = false; }),
      chat: () => console.info('[MESOL preview] K: chat would open here'),
      edit: () => show(JSON.stringify({ op: 'edit' })),
      lore: (level = 'public') => show(JSON.stringify({ op: 'view', who: 'Pete', view: isEmpty(card.firstName) && FIELDS.every((f) => isEmpty(card[f.id])) ? null : view(level) })),
      toggle: () => (st.open ? api.close() : api.edit()),
    };
    window.__preview = api;
    // End, as LorePage.cs binds it: ignored while a box on the page has the keyboard
    document.addEventListener('keydown', (e) => {
      if (e.key === 'End' && !api.typingNow) {
        e.preventDefault();
        api.toggle();
      }
    });
    addControls(api);
    setTimeout(() => api.edit(), 50);
    return api;
  },
};

function addControls(api) {
  const bar = document.createElement('div');
  bar.id = 'preview-bar';
  bar.innerHTML = '<b>Browser preview, stand-in server.</b> '
    + '<button data-a="edit">End / mylore</button><button data-a="lore">lore (as a stranger)</button><button data-a="offerFrom">offer from Dravyn</button><button data-a="close">close</button>';
  bar.style.cssText = 'position:fixed;left:8px;bottom:6px;z-index:9;display:flex;gap:6px;align-items:center;padding:4px 8px;'
    + 'font:11px/1.4 system-ui,sans-serif;color:#d2cec3;background:#272823;border:1px solid #6e695b';
  bar.addEventListener('click', (e) => {
    const a = e.target.dataset?.a;
    if (a && api[a]) api[a]();
    e.target.blur?.();
  });
  document.addEventListener('DOMContentLoaded', () => document.body.appendChild(bar));
  if (document.body) document.body.appendChild(bar);
}
