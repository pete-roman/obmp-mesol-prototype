// The offer panel: introductions and friendship offers made to this player, and short notes ("You and Marcus know
// each other now."). LorePage.cs sends MesolPanel(json) with op offer, clear or note; = and - in the game answer an
// offer (Server/Relations/Social.cs decides). When nothing is left on screen the page says MesolPanelEmpty and the game
// hides its viewport. In a browser, window.__panel.show(json) plays a message.

const $ = (id) => document.getElementById(id);
const NOTE_MS = 5000;
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const ICONS = {
  introduce: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="10" cy="8" r="3.5"/><path d="M3.5 20c.6-4 3.2-6 6.5-6 1.9 0 3.4.6 4.5 1.7"/><path d="M14.5 18.3l2.2 2.2 4.3-4.6"/></svg>',
  friend: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="8.5" cy="8.5" r="3"/><circle cx="16.2" cy="9" r="2.6"/><path d="M2.5 19.5c.5-3.6 2.8-5.5 6-5.5s5.5 1.9 6 5.5"/><path d="M15.5 14.2c2.9-.3 5.3 1.3 6 5.3"/></svg>',
};

const offers = new Map(); // id -> { el, timer }
let shown = 0;

function send(name, ...args) {
  try {
    if (window.readym) window.readym['send' + name](...args);
  } catch (e) {
    console.warn('[MESOL] send' + name + ' did not go through', e);
  }
}

function changed() {
  const count = offers.size + document.querySelectorAll('.note:not(.out)').length;
  if (count === 0 && shown > 0) send('MesolPanelEmpty');
  shown = count;
}

function remove(el) {
  el.classList.add('out');
  setTimeout(() => { el.remove(); changed(); }, 400);
}

function show(json) {
  let m;
  try {
    m = JSON.parse(json);
  } catch {
    return;
  }
  try {
    const saved = localStorage.getItem('mesol.theme');
    if (saved === 'dark' || saved === 'light') document.body.dataset.theme = saved;
  } catch {
    // no storage
  }
  if (m.op === 'offer') {
    const el = document.createElement('div');
    el.className = 'offer';
    el.innerHTML = `<i class="mark">${ICONS[m.kind] || ICONS.introduce}</i><h2>${esc(m.text)}</h2>`
      + `<p>${m.kind === 'friend' ? 'You would both see what the other shares with friends.' : 'You would both see what the other shares with people they know.'}</p>`
      + '<div class="answer"><span><kbd>=</kbd>Accept</span><span><kbd>-</kbd>Decline</span></div><div class="time"><span></span></div>';
    $('offers').prepend(el);
    const bar = el.querySelector('.time span');
    requestAnimationFrame(() => { bar.style.transition = `width ${m.seconds || 30}s linear`; bar.style.width = '0%'; });
    const timer = setTimeout(() => clear(m.id), (m.seconds || 30) * 1000 + 500); // the server clears it too
    offers.set(m.id, { el, timer });
  } else if (m.op === 'clear') {
    clear(m.id);
    return;
  } else if (m.op === 'note') {
    const el = document.createElement('div');
    el.className = 'note';
    el.textContent = m.text;
    $('notes').prepend(el);
    setTimeout(() => remove(el), NOTE_MS);
  }
  changed();
}

function clear(id) {
  const o = offers.get(id);
  if (!o) return;
  offers.delete(id);
  clearTimeout(o.timer);
  remove(o.el);
}

window.__panel = { show };
if (window.readym) {
  try {
    if (typeof window.readym.onMesolPanel === 'function') window.readym.onMesolPanel(show);
    else window.readym.on('MesolPanel', show);
  } catch (e) {
    console.error('[MESOL] could not listen for MesolPanel', e);
  }
  send('MesolPanelReady');
}
