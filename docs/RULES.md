# Galaxy rules summary

Summarised from the non-card files in `data/` (`mechanic_descriptions.json`,
`captains_mechanic.json`, `character-tier_mechanic.json`, `spells_mechanic.json`,
`treasures_mechanic.json`; source: guide.galaxy.fun). Nothing here is implemented yet —
this is the reference for later work. Terms the source uses but does not define are listed
at the end.

## Captains
- Before each game you select **one Captain** and **one of that Captain's decks**; the deck is
  shuffled into the **shared card pool**.
- Most Captains begin the game with **3 full hearts** (see Heart Damage — not described in the data).
- Each Captain has their own collection of collectible cards, unlocked through play.
- Outside gameplay: new premium Captains are released monthly and become free 2 months after
  release; 50 unlocked collectible cards for a Captain grants their Diamond frame; costumes
  and frame banners are cosmetic.

## Characters
- The primary card type. Each turn you **recruit** Characters by dragging them from the
  **shop** to your **board** or to the **bank**.
- Characters fight in the **fight phase**: each deals damage with its **Attack** (left number)
  and takes damage on its **Health** (right number). At 0 Health a Character is **KO'd**; you
  see it again when you return to the shop.
- Most Characters have ability text.
- **Rarity** (Common, Uncommon, Rare, Epic, Legendary) determines when a Character starts
  appearing in the shop and which Treasure it finds when goldened.
- Characters can have one or more **faction types**.
- **Bronze / Silver / Golden**:
  - 2nd time you see a Character in the shop it is **Silver**; recruiting it grants a **Bonus Shop**.
  - 3rd time it is **Golden**; recruiting it grants a **Bonus Treasure Shop**.

## Spells
- From **Turn 3** the shop offers **3 Characters and 1 Spell**.
- Spells have no stats and are one-time use. Cast by dragging onto the board; some need a target.
  - Targeting a Character: drag onto that Character.
  - Targeting a **slot**: drag onto the Character in that slot. This still counts as casting
    the Spell *on* a Character.
- Casting a Spell **uses up your shop** — it replaces recruiting a Character that turn.
- Spells cannot be banked. A granted **Bonus Spell must be cast**.
- Spells have rarities (not shown on the frame) and follow the same shop rarity progression
  as Characters.
- Some Spells only appear under specific circumstances (e.g. Candy Spells appear if you play a
  Candy Captain or have Candy Characters in your deck).

## Treasures
- Permanent bonuses that exist only for the current game.
- Most commonly earned by recruiting the **Golden** (third copy) of a Character or by
  completing a Character's **quest**.
- Wording:
  - **Discover / Choose** — pick from a treasure shop of 3 choices.
  - **Get** — gain it directly.
  - **Collect** — happens any time a Treasure is added, by either method.
- Treasures usually cannot be destroyed except by their own effects. There is a maximum cap;
  gaining Treasures past the cap removes older ones (the cap's value is not given).
- Treasures discovered by a Character match that Character's rarity.
- **Treasure Hoard** ability: the Character gets +1/+1 for each Treasure you own.

## Boons
- Boons are special Treasures. They are **not** rewarded for Golden Characters or Quests; they
  come only from card effects (e.g. Orpheus gives Boons if you have a cracked heart).
- Each Boon has a **condition** (like a Quest) that must be met before its effect happens.
- A Boon rewards other cards or plays a powerful Spell, then **disappears** (it stops counting
  for Treasure Hoard).
- Unfinished Boons you hold cannot be offered again in Boon discovery shops; completed Boons
  can be rediscovered and completed again.
- Boons are Treasures, so treasure-copying and treasure-stealing effects affect them.

## Buffs
- Buffs are usually **permanent** for the rest of the game: they apply in all future fights and
  to future copies of the Character (Silver/Golden) in the shop.
- A buff given during a fight is kept on return to the shop.
- A Character **summoned** during a fight and buffed keeps the buff when summoned again or
  seen in the shop.
- **Transforming** a Character carries all its buffs to the new Character.
- Buffs to a card's *ability* are also permanent and carry through transformations: the
  improvement is added to the new ability (Snow Ball's +1/+1 buffed to +6/+6, transformed
  into Frankenstein, makes Frankenstein's +2/+2 become +7/+7).
- **Slot buffs** are permanent and tied to the slot, not the Character in it.
- **Auras**: abilities that improve stats only while the source is on your board (most
  **support** abilities). They stop applying when the condition changes or the source is
  removed (Moonshadow grants other support Characters Ranged; they lose it when Moonshadow
  is KO'd).

## Core loop (from the full guide, `data/guide_rules.json`)
- A turn is a **Shop phase** then a **Fight**. You get **2 shops per turn**; each shop offers **3 Characters**, plus
  **1 Spell from turn 3**. Bonus Shops add extra shops that turn.
- **Shop level**: Commons only on turns 1-2; Uncommons from turn 3, Rares 5, Epics 7, Legendaries 9.
- **Board**: 5 slots - 3 front, 2 back - plus a Bank slot. Banking is only allowed once the board is full.
- **Attack order**: each player's characters attack left to right, front to back. The first attacker is random.
  Targets are random front-row enemies while any stand. **Sneak** attacks the back row first. **Ranged** attackers
  take no damage back.
- **Hearts**: 3 to start. Lose to 3+ survivors: a heart is emptied. Lose to 2 or fewer: a heart is cracked (an
  already-cracked heart is emptied). Win with a cracked heart: it is healed. With only 2 players left, every loss
  empties a heart.
- **Silver** = 2nd copy (+1/+1, Bonus Shop). **Golden** = 3rd copy (+1/+1, Treasure shop of its rarity, removed from
  the pool, many abilities doubled).
- **Decks** on the site are 12 collectible cards, shuffled into the shared pool; pools are per player.
- Keyword pages (Hunt, Farewell, Recruit, Quest, Support, Summon, Transform, Paint, Prophecy, Slot Buffs,
  Treasure Find, Chance Encounter) are summarised in `docs/SIMULATION.md` where implemented.

## Deck editor rules (as currently implemented)
- A deck is **1 Captain + 10 Characters**.
- Only Characters whose `pool` is not `shared` can be put in a deck.
- Spells and Treasures are never part of a deck.

## Still not defined anywhere in the data
The treasure cap, exact support/neighbour slot geometry, and how "your shop" buffs persist.
