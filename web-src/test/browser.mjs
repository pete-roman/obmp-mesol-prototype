// node test/browser.mjs [--shots dir] [--size 1920x1080]
// Opens the browser preview (preview/index.html, built by build.mjs) in Chrome and plays the card page with the
// keyboard only, as in game: fields and sections, typing (Polish letters too), the race and place pickers, choices,
// who-sees-it, clearing, the preview as others see you, the other player's card, traits (roll, reroll, keep, type), Escape and End.
import puppeteer from 'puppeteer-core';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';
import { mkdirSync } from 'node:fs';

const here = dirname(fileURLToPath(import.meta.url));
const url = pathToFileURL(join(here, '..', 'preview', 'index.html')).href;
const arg = (name) => (process.argv.includes(name) ? process.argv[process.argv.indexOf(name) + 1] : null);
const shots = arg('--shots');
const [width, height] = (arg('--size') || '1920x1080').split('x').map(Number);
if (shots) mkdirSync(shots, { recursive: true });

const browser = await puppeteer.launch({
  executablePath: process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  headless: true,
  args: ['--allow-file-access-from-files'],
  defaultViewport: { width, height },
});
const page = await browser.newPage();
const errors = [];
page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
page.on('pageerror', (e) => errors.push(String(e)));

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let failures = 0;
const check = (ok, label) => { if (!ok) failures++; console.log(`${ok ? 'ok  ' : 'FAIL'} ${label}`); };
const press = async (...keys) => { for (const k of keys) { await page.keyboard.press(k); await sleep(40); } };
const type = async (text) => { await page.keyboard.type(text, { delay: 5 }); await sleep(50); };
const state = () => page.evaluate(() => ({
  ui: window.__mesol.ui, mode: window.__mesol.mode, open: window.__mesol.open, tab: window.__mesol.tab, row: window.__mesol.row,
  status: window.__mesol.status.text,
}));
const value = (id) => page.evaluate((i) => window.__mesol.card[i], id);
const settle = () => sleep(250);
const shot = async (name) => { if (shots) await page.screenshot({ path: join(shots, `${name}.png`) }); };
const text = (sel) => page.$eval(sel, (e) => e.textContent);
// Up/down to a field by its label, in the current section
async function goTo(label) {
  for (let i = 0; i < 30; i++) {
    const cur = await page.$eval('.field.cursor .label', (e) => e.textContent.trim());
    if (cur === label) return true;
    await press('ArrowDown');
  }
  return false;
}
async function goSection(name) {
  for (let i = 0; i < 7; i++) {
    if ((await text('.tab.on')).startsWith(name)) return true;
    await press('ArrowRight');
  }
  return false;
}

await page.goto(url);
await page.waitForFunction(() => window.__mesol?.loaded && window.__mesol.open, { timeout: 10000 });
await settle();
await shot('01-empty-card');

let s = await state();
check(s.open && s.mode === 'edit' && s.ui === 'browse', 'the editor opens on Identity, browsing');
check((await text('.field.cursor .label')).trim() === 'First name', 'the cursor starts on First name');
check((await page.$$('.tab')).length === 8, 'eight sections, Nearby last');

// typing a name, with Polish letters
await press('Enter');
check((await state()).ui === 'text' && await page.evaluate(() => document.activeElement.id === 'edit'), 'Enter opens a text box with the keyboard in it');
await type("Ri'Zahra");
await shot('02-typing-name');
await press('Enter');
await settle();
check((await value('firstName')).text === "Ri'Zahra" && (await state()).ui === 'browse', 'Enter saves the name and goes back');
check((await text('#who')).includes("Ri'Zahra"), 'the name shows at the top');

await press('ArrowDown', 'Enter');
await type('Żółkiewska-Ślązak');
await press('Enter');
await settle();
check((await value('lastName')).text === 'Żółkiewska-Ślązak', 'Polish letters are saved as typed');

// Escape over an unsaved change asks first
await press('Enter');
await type(' zmiana');
await press('Escape');
check((await state()).ui === 'text' && (await state()).status.includes('Esc again'), 'Esc over an unsaved change asks first');
await press('Escape');
await settle();
check((await state()).ui === 'browse' && (await value('lastName')).text === 'Żółkiewska-Ślązak', 'a second Esc throws the change away');

// the length limit: the box takes no more than the field holds
await goTo('Title');
await press('Enter');
await type('x'.repeat(45));
check((await page.$eval('#edit', (e) => e.value.length)) === 30, 'the title box stops at 30 characters');
await press('Escape', 'Escape');

// who sees it: Space cycles, the server keeps it
await goTo('Title');
await press('Space');
await sleep(700);
check((await value('title')).vis === 'friends', 'Space moves Title from Known to Friends, and the server keeps it');
await goTo('Seen as');
await press('Space');
check((await state()).status.includes('always'), 'Seen as is always public: Space says so');

// race: picked, never typed
await goSection('Background');
check((await text('.field.cursor .label')).trim() === 'Race', 'Background starts with Race');
check(await page.$('.game-hint') !== null, 'an empty race shows the game\'s race as a hint');
await shot('03-race-hint');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
await settle();
await shot('04-race-picker');
const races = await page.$$eval('.option', (o) => o.length);
check(races === 10, `the race list has the 10 races (${races})`);
check((await text('.option.cursor')).startsWith('Khajiit'), 'the picker starts on the game\'s race');
await type('dupa');
check((await page.$$('.option')).length === 0, 'typing "dupa" finds no race: there is nothing to type a race into');
await page.keyboard.down('Control'); await press('a'); await page.keyboard.up('Control');
await press('Backspace');
await type('khaj');
await press('Enter');
await settle();
check((await value('race')).catalogId === 'race.khajiit' && (await state()).ui === 'browse', 'Enter picks Khajiit');

// kind: only Khajiit furstocks that fit the model
await goTo('Kind');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
const kinds = await page.$$eval('.option > span', (o) => o.map((e) => e.firstChild.textContent));
check(kinds.includes('Cathay-raht') && kinds.includes('Cathay') && !kinds.includes('Senche'), `Kind offers Cathay and Cathay-raht, not Senche (${kinds.join(', ')})`);
await type('raht');
await press('Enter');
await settle();
check((await value('subtype')).catalogId?.includes('cathay-raht'), 'Cathay-raht picked');

// home region needs the homeland first
await goTo('Home region');
await press('Enter');
check((await state()).ui === 'browse' && (await state()).status.includes('homeland first'), 'Home region asks for the homeland first');

// homeland: the race's own province first, then the other provinces of Tamriel
await goTo('Homeland');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
await settle();
await shot('05-homeland-picker');
const heads = await page.$$eval('#options .group-head', (h) => h.map((e) => e.textContent));
const firstProvince = await page.$eval('.option > span', (e) => e.firstChild.textContent);
check(heads[0] === 'Native lands' && heads[1] === 'Other Tamrielic Provinces' && firstProvince === 'Elsweyr',
  `Homeland: Elsweyr under "Native lands", then "Other Tamrielic Provinces" (${heads.join(', ')})`);
check((await page.$$('.option')).length === 10, 'all ten provinces are there');
check((await text('#option-detail')).length > 20, 'the highlighted province shows its lore summary');
await press('Enter');
await settle();
check((await value('homeland')).catalogId === 'province.elsweyr', 'Elsweyr is the homeland');

// home region: places inside Elsweyr only, Remastered Origins first
await goTo('Home region');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
await settle();
await shot('05b-home-region');
const regions = await page.$$eval('.option > span', (o) => o.map((e) => e.firstChild.textContent));
check(regions.includes('Anequina') && !regions.includes('Bravil') && !regions.includes('Anvil'), `Home region lists Elsweyr's places only (${regions.length})`);
check(await page.$eval('.option', (e) => e.querySelector('.tag.origin') !== null), 'Remastered Origins come first, marked Origin');
await press('Enter');
await settle();
check(!(await value('homeRegion')).empty, 'a home region is picked');

// upbringing: the race's own ways
await goTo('Upbringing');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
const ways = await page.$$eval('.option > span', (o) => o.map((e) => e.firstChild.textContent));
check(ways.includes('Trading caravan') && !ways.some((w) => /Hold|Ashlander/.test(w)), `Upbringing: Khajiit and shared ways, no Nord or Dunmer ones (${ways.length})`);
await press('Escape');

// changing the homeland clears the home region
await goTo('Homeland');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
await type('cyro');
await press('Enter');
await settle();
check((await value('homeRegion')).empty && (await state()).status.includes('Home region cleared'), 'a new homeland clears the home region');

// affiliation: no Thalmor membership in 3E 433
await goSection('Life');
await goTo('Affiliation');
await press('Enter');
await page.waitForFunction(() => window.__mesol.pick && !window.__mesol.pick.loading);
const factions = await page.$$eval('.option > span', (o) => o.map((e) => e.firstChild.textContent));
check(!factions.includes('Thalmor (Dominion-era council)') && factions.includes('Fighters Guild'), 'affiliations are 3E 433 ones: no Dominion-era Thalmor (a historical entry)');
await press('Escape');

// a choice
await goTo('Relationship');
await press('Enter');
await type('court');
await press('Enter');
await settle();
check((await value('relationship')).text === 'courting', 'Relationship: courting, chosen from the list');

// a long text in a box, with a new line
await goSection('Story');
await goTo('History');
await press('Enter');
check(await page.evaluate(() => document.activeElement.tagName === 'TEXTAREA'), 'History opens a big text box');
await type('Born in Rimmen, raised among caravans.');
await page.keyboard.down('Shift'); await press('Enter'); await page.keyboard.up('Shift');
await press('Enter');
await type('Ma kutasa na czole, bo czemu nie.');
await shot('06-long-text');
await page.keyboard.down('Control'); await press('Enter'); await page.keyboard.up('Control');
await settle();
const history = await value('history');
check(history.text?.includes('\n') && history.text.includes('kutasa'), 'Ctrl+Enter saves several lines as written');
await shot('07-history-saved');

// clearing takes two presses
await goTo('History');
await press('Delete');
check(!(await value('history')).empty && (await state()).status.includes('again'), 'one Delete only asks');
await press('Delete');
await settle();
check((await value('history')).empty, 'the second Delete clears it');

// look: free text
await goSection('Look');
await goTo('Marks');
await press('Enter');
await type('blizna nad okiem, ślad po oparzeniu');
await press('Enter');
await settle();
check((await value('marks')).text === 'blizna nad okiem, ślad po oparzeniu', 'Marks: free text with Polish letters');

// status and seen as, for the preview
await goSection('Identity');
await goTo('Seen as');
await press('Enter');
await type('a scarred Khajiit with a merchant\'s sash');
await press('Enter');
await settle();

// preview
await press('p');
await page.waitForFunction(() => window.__mesol.preview);
await settle();
await shot('08-preview-stranger');
const strangerView = await text('.seen');
check(strangerView.includes("scarred Khajiit") && !strangerView.includes("Ri'Zahra"), 'a stranger sees "seen as", not the name');
check(strangerView.includes('blizna'), 'a stranger sees the public Marks');
await press('p');
await page.waitForFunction(() => window.__mesol.preview && window.__mesol.previewIndex === 1);
await settle();
check((await text('.seen')).includes("Ri'Zahra"), 'someone you met sees the name');
await press('Escape');
check((await state()).ui === 'browse', 'Esc leaves the preview');

// traits: a roll, the reroll, then fixed; a typed one is fixed at once
await goSection('Traits');
check((await text('.group-head')).includes('Temperament'), 'Traits are listed under their groups');
check((await page.$$('.field.trait .die')).length === 24, 'each of the 24 trait rows carries a die');
await goTo('Lazy');
await shot('09-traits');
await press('r');
await page.waitForFunction(() => window.__mesol.rolling === 'trait.lazy');
check((await text('#detail')).includes('rolling'), 'R: the dice are rolling (RpRoll shows them in game)');
await page.waitForFunction(() => !window.__mesol.rolling, { timeout: 8000 });
await settle();
let lazy = await value('trait.lazy');
const firstRoll = await page.evaluate(() => window.__preview.lastRoll);
check(lazy.number === firstRoll.total && !lazy.fixed && lazy.number >= 5 && lazy.number <= 100, `the roll's 5d20 sum is the value (${firstRoll.dice.join('+')} = ${lazy.number}), one reroll left`);
await shot('10-trait-rolled');
await press('r');
await page.waitForFunction(() => !window.__mesol.rolling && (window.__mesol.card['trait.lazy'].rolls || []).length === 2, { timeout: 8000 });
await settle();
lazy = await value('trait.lazy');
check(lazy.fixed && lazy.rolls.length === 2, 'the reroll is final');
await press('r');
check((await state()).status.includes('no more'), 'a third roll is refused');
await goTo('Romantic');
await press('r');
await page.waitForFunction(() => !window.__mesol.rolling && window.__mesol.card['trait.romantic'].number != null, { timeout: 8000 });
await press('Enter');
await settle();
check((await value('trait.romantic')).fixed, 'Enter keeps a first roll for good');
await goTo('Fearful');
await press('Enter');
check((await state()).ui === 'number', 'Enter on an empty trait asks for a number');
await type('x7');
check(await page.$eval('#edit', (e) => e.value) === '7', 'the number box takes digits only');
await press('Backspace');
await type('63');
await press('Enter');
await settle();
check((await value('trait.fearful')).number === 63 && (await value('trait.fearful')).fixed, 'a typed 63 is fixed at once');
await press('Delete');
check(!(await value('trait.fearful')).empty && (await state()).status.includes('set for good'), 'a trait cannot be cleared');
await shot('11-traits-filled');

// End while typing moves the caret; End otherwise closes and opens
await goSection('Identity');
await goTo('Title');
await press('Enter');
await type('Caravan guard');
await press('End');
check((await state()).open && (await state()).ui === 'text', 'End in a text box does not close the card');
await press('Enter');
await settle();
await press('End');
await settle();
check(!(await state()).open, 'End closes the card');
await press('End');
await page.waitForFunction(() => window.__mesol.open);
await settle();
check((await state()).mode === 'edit' && (await value('firstName')).text === "Ri'Zahra", 'End opens the card again, still filled');

// Nearby: other players as you may see them, their cards, the handshake
await goSection('Nearby');
await page.waitForFunction(() => window.__mesol.nearbyLoaded);
await settle();
await shot('12-nearby');
const people = await page.$$eval('.field.person .label', (l) => l.map((e) => e.textContent.trim()));
check(people.length === 3 && people[0].includes('scarred Khajiit') && people[1].includes('Marcus Varro'),
  `Nearby lists three players, strangers by their Seen as, an acquaintance by name (${people.join(' | ')})`);
await press('Enter');
await page.waitForFunction(() => window.__mesol.mode === 'view');
await settle();
check((await text('#viewer-title')).includes('scarred Khajiit') && !(await text('#viewer-body')).includes('Rimmen'), 'Enter opens a stranger\'s card: no name, no history');
check((await text('#viewer-keys')).includes('Back to Nearby'), 'the viewer offers the way back to Nearby');
await press('Escape');
await settle();
check((await state()).mode === 'edit' && (await state()).open, 'Esc goes back to Nearby, the card stays open');
await press('=');
await page.waitForFunction(() => window.__mesol.nearby[0]?.relation === 'known', { timeout: 6000 });
await settle();
check((await page.$eval('.field.person.cursor .label', (e) => e.textContent)).includes("Ri'Zahra"), '= introduces; once they agree, their name shows');
await press('f');
await page.waitForFunction(() => window.__mesol.nearby[0]?.relation === 'friend', { timeout: 6000 });
await press('Enter');
await page.waitForFunction(() => window.__mesol.mode === 'view');
await settle();
check((await text('#viewer-body')).includes('Rimmen'), 'as friends, their history shows on their card');
await shot('13-friend-card');
await press('Escape');
await settle();
await press('Delete');
check((await state()).status.includes('again'), 'Delete on a friend asks first');
await press('Delete');
await page.waitForFunction(() => window.__mesol.nearby[0]?.relation === 'known', { timeout: 6000 });
check(true, 'the second Delete ends the friendship; the introduction stays');
await press('ArrowDown', 'ArrowDown');
await page.evaluate(() => { window.__preview.autoAccept = false; window.__preview.offerFrom('12', 'introduce'); });
await page.waitForFunction(() => window.__mesol.nearby[2]?.incoming === 'introduce', { timeout: 6000 });
await settle();
await shot('14-incoming-offer');
check((await text('.field.person.cursor')).includes('Wants to meet you'), 'an incoming offer is marked on the player');
await press('=');
await page.waitForFunction(() => window.__mesol.nearby[2]?.relation === 'known', { timeout: 6000 });
check((await page.$eval('.field.person.cursor .label', (e) => e.textContent)).includes('Dravyn'), '= accepts their introduction: their name shows');
await press('b');
check((await state()).status.includes('again'), 'B asks before blocking');
await press('b');
await page.waitForFunction(() => window.__mesol.nearby[2]?.relation === 'blocked', { timeout: 6000 });
check(true, 'the second B blocks');
await goSection('Identity');

// another player's card (/lore)
await page.evaluate(() => window.__preview.lore('public'));
await page.waitForFunction(() => window.__mesol.mode === 'view');
await settle();
await shot('09-lore-viewer');
const viewer = await text('#viewer');
check(viewer.includes('Pete') && viewer.includes('scarred Khajiit') && !viewer.includes("Ri'Zahra"), '/lore shows the stranger\'s cut of the card');
await press('Escape');
await settle();
check(!(await state()).open, 'Esc closes the viewer');

// dark
await page.evaluate(() => window.__preview.edit());
await page.waitForFunction(() => window.__mesol.open && window.__mesol.mode === 'edit');
await press('v');
await settle();
check(await page.evaluate(() => document.body.dataset.theme === 'dark'), 'V switches to dark');
await shot('10-dark');
await press('v');

check(errors.length === 0, `no page errors${errors.length ? ': ' + errors.join(' | ') : ''}`);
await browser.close();
console.log(failures ? `\n${failures} FAILED` : '\nall passed');
process.exit(failures ? 1 : 0);
