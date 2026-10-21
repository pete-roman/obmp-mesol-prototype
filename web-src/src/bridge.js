// Talking to the game. In game, ReadyM's CEF bridge supplies window.readym. LorePage.cs pushes the state (open or not,
// and the server's last own message) as one JSON message, MesolState, again a few times after every change; the page
// confirms it with MesolSeen. Requests go out as MesolAsk(id, json) and come back as MesolAnswer(id, json).
// MesolTyping(bool) says whether a text or search box has the keyboard.
//
// In a normal browser there is no readym. The preview build (preview/index.html) loads preview.js first, which plays
// the game's and the server's part with the real lore catalog; the shipped page has no preview code at all.

export function connect(handlers) {
  if (typeof window.readym === 'object' && window.readym !== null) return gameBridge(handlers);
  if (window.MESOL_PREVIEW) return window.MESOL_PREVIEW.connect(handlers);
  console.warn('[MESOL] no game and no preview: nothing to talk to');
  const nothing = () => {};
  return { preview: false, ready: nothing, seen: nothing, ask: nothing, close: nothing, chat: nothing, typing: nothing, lostFocus: nothing };
}

function gameBridge({ onState, onAnswer, onPanel }) {
  const r = window.readym;
  const listen = (name, fn) => {
    try {
      if (typeof r['on' + name] === 'function') r['on' + name](fn);
      else r.on(name, fn);
    } catch (e) {
      console.error('[MESOL] could not listen for ' + name, e);
    }
  };
  listen('MesolState', (json) => {
    try {
      onState(JSON.parse(json));
    } catch (e) {
      console.error('[MESOL] bad state message', e);
    }
  });
  listen('MesolAnswer', (id, json) => onAnswer(Number(id), json));
  listen('MesolPanel', (json) => onPanel?.(json));
  const send = (name, ...args) => {
    try {
      r['send' + name](...args);
    } catch (e) {
      console.warn('[MESOL] send' + name + ' did not go through', e);
    }
  };
  return {
    preview: false,
    ready: () => send('MesolReady'),
    seen: (seq) => send('MesolSeen', seq),
    ask: (id, json) => send('MesolAsk', id, json),
    close: () => send('MesolClose'),
    chat: () => send('MesolChat'),
    // a box on the page has the keyboard (or not): End and RpRoll's Home then leave the page alone
    typing: (on) => send('MesolTyping', !!on),
    // the page is open but no longer has the keyboard (a mouse click gave it to the game): ask for it back
    lostFocus: () => send('MesolLostFocus'),
  };
}
