# RoAura — Tutorial GIF Style Kit v1.1

This directory contains the **fixed style** for short instructional GIF/MP4 videos for RoAura, independent of the application's runtime code. The approved reference is the light-blue step caption with a numbered circle.

## Locked presentation rules

- Real screen recordings only; **do not generate or repaint RoAura controls with AI**.
- Step numeral: **Montserrat ExtraBold / 800**.
- Action text: **Montserrat Medium / 500**.
- Panel fill: `#E0ECFA`; description/outline/divider: `#4176B8`.
- Step circle: `#4275B6`, white numeral.
- Geometry and font sizes are defined by `template/style.json` for the 747×481 reference, scaled by frame width.
- Place the recording in the space above the lower caption. Step changes update the number and description. New visual designs require a **new style version**.

## Source files

| File | Purpose |
| --- | --- |
| `template/style.json` | Authoritative color, spacing, font and rendering settings |
| `template/overlay-reference.svg` | Scalable reference for top and bottom captions |
| `tools/render.py` | Reproducible compositor for PNG, GIF and MP4 |
| `examples/add-effect.json` | Six-stage example based on the real Enchant Poison recording |
| `docs/STYLE_GUIDE.md` | Exact specification |
| `docs/RULES_FOR_FUTURE_INSTRUCTIONS.md` | Rules for making all subsequent tutorials |
| `docs/FONT_CHECKSUMS.txt` | Font-file checksums from the reference rendering |

## Install (Windows)

Install Python 3.10+, FFmpeg (in PATH) and Montserrat **ExtraBold** and **Medium**. Font files are intentionally not committed.

```powershell
cd docs/tutorial-gifs
python -m pip install -r requirements.txt
```

## Render a real recording

Specify the correct source video for the timestamps in `examples/add-effect.json`.

```powershell
python tools/render.py --video "C:\videos\add-effect.mp4" --project examples/add-effect.json --output "add-effect.gif"
python tools/render.py --video "C:\videos\add-effect.mp4" --project examples/add-effect.json --output "add-effect.mp4"
python tools/render.py --preview "C:\videos\frame.png" --step 4 --project examples/add-effect.json --output "preview.png"
```

If automatic Montserrat lookup fails, specify:
`--font-extrabold "C:\Windows\Fonts\Montserrat-ExtraBold.ttf" --font-medium "C:\Windows\Fonts\Montserrat-Medium.ttf"`.

For a save/confirm action, retain the original click and the first settled result in one continuous excerpt. Slow that excerpt as needed.

Other useful flags: `--width 960 --fps 8 --start 0 --max-seconds 8`. Output ratio should be close to 747:481; crop incompatible sources before rendering.

## Reproducibility

The source recording, stage descriptions and timestamps vary per instruction. A project title may be kept as metadata. Identical source frame, fonts, Python/Pillow versions and configuration produce repeatable PNG pixels. GIF and MP4 bytes may also depend on the FFmpeg encoder version.

The style and source tools are committed here. Large binary recording/demo/preview assets from the chat experiment have not been uploaded to GitHub; they are available in the separate local style-kit archive. The vector reference `template/overlay-reference.svg` and renderer remain usable without those optional assets.

## Scope and branch status

This is documentation/tooling only; no application WPF code, settings, overlays, release version, tags or binaries are changed. Keep this branch separate from `main` until review; merging to `main` requires explicit approval.
