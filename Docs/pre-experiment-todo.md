# Co doladit před experimentem

Seznam věcí k dořešení před spuštěním experimentu. Průběžně se doplňuje.

## Otevřené

_(zatím nic — doplní se později)_

## Hotové

- [x] **Doladit vzhled MAT.** Přidány vylepšené MAT prefaby a model dlaždice
  (`Assets/Prefabs/GrabbableMatBetter.prefab`, `Assets/Prefabs/MatContentBetter.prefab`,
  `Assets/Models/tile.fbx`).

- [x] **Mazání objektů a portálů nefunguje na brýlích.** Tlačítka se v nativním
  buildu vůbec nevykreslila (fungovala jen přes Quest Link). Příčina: mazací widget
  si materiály vyrábí za běhu přes `Shader.Find("Universal Render Pipeline/Unlit")`,
  jenže tento shader nebyl v Always Included Shaders ani ho nepoužíval žádný materiál
  → v Android buildu se vystripoval a `Shader.Find` vracel `null`. V Editoru (Link)
  je dostupný vždy, proto tam fungoval. Fix: URP/Unlit přidán do Always Included
  Shaders (`ProjectSettings/GraphicsSettings.asset`). Ověřeno na zařízení — tlačítka
  jsou vidět a fungují.
