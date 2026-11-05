# Retro font (VT323)

Throng uses **VT323** (SIL Open Font License 1.1) for titles and toolbar labels (D-018).

`VT323-Regular.ttf` and its `OFL.txt` are bundled here (added in round 001). To replace or re-add it:

1. Download VT323 from Google Fonts: https://fonts.google.com/specimen/VT323
2. Copy `VT323-Regular.ttf` into this folder.
3. Copy the font's `OFL.txt` into this folder too (it ships next to `Throng.exe`).
4. Rebuild.

The `.csproj` picks up any `*.ttf` here as an embedded resource automatically. Until the file is present the UI
falls back to Consolas, so nothing breaks.
