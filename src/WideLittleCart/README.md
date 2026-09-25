# WideLittleCart

Makes the vanilla **POCKET C.A.R.T.** (the little cart) **as wide as the normal C.A.R.T.** and **twice as deep** from front to back. The height stays the same, and it still goes into your pocket like normal.

## What it does

- Resizes the little cart's mesh, walls, floor and physics colliders. The wheels keep their size, so the cart doesn't ride higher.
- Resizes the **in-cart detection** to match, so everything inside the bigger cart counts, rides along and shows on the cart screen.
- Moves the handle, its grab area and the grab point to the new back edge. The handle is stretched to the new width, which you can turn off.
- Increases the push/hold distance by the extra depth, so you don't walk into the longer cart.
- Moves the value screen with the back edge without stretching it, so it stays readable.
- Leaves the cart's root scale alone, so the game's pocket animation and mods that check the cart's scale (like PocketCartPlus) keep working.

Only the vanilla little cart (`Item Cart Small`) is changed by default. The normal C.A.R.T. and modded carts are not touched.

### Size maths

The mod measures the floor of both carts when the game runs. The normal cart's width is read from the game's own `Item Cart Medium` prefab.

- Width: normal cart floor width ÷ little cart floor width ≈ 1.232 m ÷ 0.774 m = **×1.59**
- Depth: `DepthMultiplier` = **×2.0** (0.786 m becomes 1.572 m)
- Height: `HeightMultiplier` = ×1.0

The measured values are written to the BepInEx log.

## Requirements

- [BepInExPack](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/)
- No other mods required.

## Who needs it (host-controlled)

- **The host decides.** The host publishes its size through the Photon room, and guests running the mod use exactly that size. Guests' own size settings are ignored in multiplayer.
- **If the host does not run it, guests keep the vanilla cart.** Guests can't get a bigger cart on their own.
- **Recommended: everyone installs it.** Cart physics runs on the host, but each player builds their own visuals. A guest without the mod sees the small cart while the host's physics uses the big one, so items will look like they float at the edges.

## Config (`BepInEx/config/MrGlim.WideLittleCart.cfg`)

| Section / Key | Default | Description |
| --- | --- | --- |
| General / `Enabled` | `true` | Master switch. As a guest, turning it off keeps your cart visuals vanilla. |
| General / `ApplyToModdedSmallCarts` | `false` | Also resize other small carts, such as PocketCartPlus's POCKET C.A.R.T. PLUS, which already scales itself. |
| Size / `WidthMatchesNormalCart` | `true` | Width matches the normal C.A.R.T. (measured). |
| Size / `DepthMultiplier` | `2.0` | Depth compared with the vanilla little cart (0.5 to 4). |
| Size / `ManualWidthMultiplier` | `0` | If above 0, this width factor is used instead of matching the normal cart (0 to 4). |
| Size / `HeightMultiplier` | `1.0` | Height factor (0.5 to 3). |
| Visuals / `ScaleHandleWidth` | `true` | Stretch the handle and its grab area to the new width. |
| Debug / `DebugLogging` | `false` | Log measurements and every resized cart. |

Size settings are the host's in multiplayer. Changes apply to carts spawned after the change (next level or shop visit).

## Compatibility

- Built for R.E.P.O. (September 2026 build).
- PocketCartPlus: the vanilla little cart, including its Keep Items upgrade, works with the resized cart. The PLUS cart is left alone unless `ApplyToModdedSmallCarts` is on.
- Mods that add *separate* bigger carts are unaffected, because this mod only changes the vanilla little cart.

## Known issues / notes

- Guests without the mod see the vanilla-sized cart (see above).
- Extreme config values (very deep or very tall carts) can make the cart awkward to steer through doors.

## AI disclosure

This mod was made with the help of generative AI. The code, this README and the icon were produced with an AI coding agent (xAI Grok), directed by the author. The DLL also declares this in its `AI_Assisted_Creation` / `AI_Model_Vendor` assembly metadata, following Thunderstore's AI guidelines. The package is listed in the **AI Generated** category.

## Credits

- semiwork for R.E.P.O.
- Thanks to darmuh (PocketCartPlus) for the scale-safe approach this mod follows: it never touches the cart's root scale.
- Made by MrGlim.

## Changelog

See CHANGELOG.md.
