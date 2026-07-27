# Tank models go here

Drop 3D tank models into **this folder** and they show up in the game's
**MY TANK** (Garage) screen automatically as extra body styles. No code changes,
no scene setup — the game scans this folder at runtime.

## Where to get them

<https://www.meshy.ai/tags/tank> — free, and the gallery models are CC0
(royalty-free, commercial use OK, no credit required). You need a free Meshy
account to press download.

## What to download

| | |
|---|---|
| **Format** | `.fbx` (best) or `.glb` |
| **How many** | 5–6 is plenty |
| **Style** | Low-poly / stylised looks best and runs fastest on phones |
| **Size** | Keep each file under ~5 MB so the APK stays small |

## How to add them

1. Download the model.
2. Copy the file into this folder: `Assets/Resources/TankModels/`
3. If the download came with a texture folder, copy that in too — keep the
   texture files next to the model.
4. Done. Next build, the Garage lists them after STANDARD / HEAVY / SCOUT.

## What the game does automatically

- Scales the model to tank size (~4.2 m long) and sits it on the ground
- Removes the model's colliders (the tank body already handles collision)
- Hides the model's own turret if it has one, so the game's aiming turret works
- Tints the model with your chosen player colour so tanks stay tellable apart

## Naming

The file name becomes the button label, so name them clearly:

```
heavy_crusher.fbx   ->  "HEAVY CRUSHER"
desert_raider.fbx   ->  "DESERT RAIDER"
```

Avoid spaces and odd characters in the file name.

## If the folder is empty

Nothing breaks — the game just uses the three built-in tank bodies exactly as
before. This folder is safe to leave empty.
