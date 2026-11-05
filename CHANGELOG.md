# Changelog

All notable changes to Throng. Versions follow `major.minor.patch`.

## 1.0.0 — 2026-11

First public release.

- **Modelling:** multi-level studies drawn in plan, with walkable regions, obstacles, walls, zones, image underlays,
  copy to levels, snapping and full undo.
- **Vertical circulation:**
  - stairs, ramps, escalators, travelators and lift banks
  - landings, step-loading, keep-side lanes with counterflow, and stair ends that pack instead of jamming
  - route choice that notices big crowds from further away and spills people over to quieter stairs
- **People:**
  - agent profiles (speed, size, patience, stair effort, sight, mechanised preference, accessibility needs)
  - mix lines with constant or Poisson arrivals and rate curves
  - groups (Family, Couple, Friends, Wheelchair user + companion, School party, Fans) that walk, queue and board
    together
- **Devices and journeys:** service points with lanes and queues, and journeys with branches, tags, dwell, balk and
  give-up.
- **Services:** trains, planes, buses / coaches and rides on a timetable.
- **Views:** plan, 3D orbit and isometric, with clip above a level, a section box, exploded levels, saved views, and
  colouring by profile, group or stress.
- **Results:**
  - live and cumulative heatmaps with Fruin Level of Service
  - zone, device, journey, lift and connector tables, and graphs
  - CSV export and replay scrubbing
  - seeded batch runs in parallel
  - a one-page A3 PDF report
- **CHECK:** rules for reachability, capacity, narrow gaps, journey picks and timetables.
- **Library:** your own profiles, devices, groups and journeys across studies, shared as `.thronglib` (plus
  `.throngprofile` and `.throngdevice`).
- **Demos:** Interchange Station, a stadium sector (about 5,000 seats) and a six-sector stadium (about 30,000).
- **Installer:** per user with no admin rights, self-contained (no .NET install needed), and `.throng` opens on
  double-click.
