# MESOL: commands and keys

Player reference for MESOL 0.4.x (with RpRoll 0.8.x). Keep it in step with `Server/Commands/*.cs`, `Client/LorePage.cs` and the `KEYS` table in `web-src/src/main.js`.

## Chat commands
- `/mylore` : open your lore card (same as End)
- `/mylore card` : print your card in chat, with who sees each field
- `/mylore set <field> <text>` : fill a field, e.g. `/mylore set firstName Ri'Zahra`
- `/mylore add <field> <text>` : add text to a long field (physical, personality, history, notes)
- `/mylore pick <field> <search>` : pick from the lore list, then `/mylore pick <field> <number>`
- `/mylore vis <field> public|known|friends|private` : who sees a field
- `/mylore clear <field>` : empty a field (traits can't be cleared)
- `/mylore show <field>` : show one field in full
- `/mylore preview public|known|friends` : your card as others see it
- `/mylore fields` : list every field
- `/mylore help` : this list in chat
- `/lore` : the card of the player in front of you (5 m), cut to what they share with you
- `/lore <id>` : the card of a player by their `/who` id
- `/lore <id> <field>` : one field of their card, in chat
- `/introduce` : offer to introduce yourself to the player in front of you (same as =)
- `/introduce <id>` : offer it to a player by `/who` id
- `/introduce accept` / `/introduce decline` : answer the newest offer
- `/friend add [id]` : ask to be friends (the player in front of you, or by id)
- `/friend accept` / `/friend decline` : answer the newest friendship offer
- `/friend remove [id]` : end a friendship (you stay introduced)
- `/friend forget [id]` : become strangers again
- `/friend block [id]` / `/friend unblock [id]` : block or unblock a player
- `/roll`, `/board` : RpRoll dice, as before

## Keys in the game (card closed)
- **End** : open or close your lore card
- **Shift+End** : open the card of the player in front of you
- **=** : introduce yourself to the player in front of you, or accept an offer made to you
- **-** : decline an offer made to you
- **Home** : RpRoll dice board
- **K** : chat

An offer shows at the top of the screen for 30 seconds. You can keep walking while it is there.
= and - do nothing while chat is open or while you type.

## On your card
**Browsing**
- **Up / Down** : choose a field
- **Left / Right** or **Tab** : change section (Identity, Background, Life, Look, Story, Status, Traits, Nearby)
- **Enter** : edit the field
- **Space** : change who sees the field (Public, Known, Friends, Private)
- **Delete** : clear the field (press twice)
- **P** : preview your card as a stranger, someone you met, or a friend
- **V** : light or dark
- **K** : chat
- **Esc** : close the card

**Writing in a field**
- **Enter** : save
- **Esc** : cancel (press twice if you changed something)

**Writing a long text** (Appearance, Personality, History, Notes)
- **Enter** : new line
- **Ctrl+Enter** : save
- **Esc** : cancel (press twice if you changed something)

**Picking from a lore list** (race, kind, homeland, upbringing and the rest)
- **Type** : search
- **Up / Down**, **Page Up / Page Down** : move through the list
- **Enter** : pick
- **Esc** : back

**Traits**
- **R** : roll 5d20 for the trait. The dice land on the board at the bottom right, and players within 20 m see the roll in chat.
- **R** again : the one reroll. The new number stays for good.
- **Enter** after a roll : keep it for good
- **Enter** on an empty trait : type a value from 5 to 100. Enter applies it for good.
- Traits can't be cleared or changed once set.

**Preview (P)**
- **P**, **Space** or **Right** : next viewer
- **Left** : previous viewer
- **Up / Down** : scroll
- **Esc** : back

**Nearby** (players within 20 m)
- **Up / Down** : choose a player
- **Enter** : read their card (Esc goes back to Nearby)
- **=** : introduce yourself, or accept their introduction
- **F** : ask to be friends, or accept their friendship
- **-** : decline their offer
- **Delete** : end a friendship, or become strangers again (press twice)
- **B** : block (press twice) or unblock
- **Esc** : close the card

**Reading another player's card**
- **Up / Down**, **Page Up / Page Down** : scroll
- **K** : chat
- **Esc** : close (or back to Nearby)
