# Data sources and terms of use

Copy-ready for the Space Apps project page. Full per-file credits: `Assets/GoNoGo/Art/CREDITS.md`.
Every number in `Assets/GoNoGo/Data/Resources/GameData/*.json` carries a `source` entry (title, URL, sourced/derived/estimated).

## 1. NASA data and resources used

| NASA resource | Where it is used in the game | How it is integrated |
|---|---|---|
| DONKI (CCMC space weather database) | `donki_events.json` → `GameData.SpaceWeather` → in-mission space-weather events (flares X5+, geomagnetic storms Kp 7+) | `tools/data/fetch_donki.py` queries `api.nasa.gov/DONKI` (GST + FLR), filters notable events and maps them to a 1–3 severity scale. Severity drives radiation and power effects in the simulation. Snapshot is bundled so the game runs offline. |
| NASA SST State-of-the-Art Small Spacecraft Technology report | `parts.json` (power, propulsion, avionics values) | Real part specs become the stats the player trades off (mass, power, downlink, lifetime). |
| NASA mission press kits (OCO, OCO-2), JPL, NSSDC, NTRS | `missions.json`, `scenarios.json`, `orbits.json`, `constants.json` | Historical missions replayed as scenarios ("How NASA fixed it"); orbit and eclipse constants. |
| NASA Earth Observatory, SVS (Blue Marble, Black Marble, GEBCO, LROC Moon Kit) | `Art/Textures/Earth`, `Art/Textures/Moon` | Planet textures in the launch and orbit views. |
| NASA 3D Resources (Landsat 8, OCO-2, DSN 34 m, Saturn V, ...) | `Art/Models/NASA` | Satellite, rocket and ground-station models. |
| NASA audio (Artemis I, CLPS IM-2, Juno) | `Audio/NASA` | Launch countdown, pad and liftoff sound in the control room. |
| JPL Concept Maturity Levels paper (Wessen et al., AIAA 2013, CC0), listed in the challenge Resources | `ui.tut.done` text; the tutorial flow (requirements, trade-off, point design) | CML 1–4 named in the end-of-training message. |
| Goddard Mission Design Lab (challenge Resources) | `ui.tut.call` text; the five departments in `departments.json` | Departments are framed as MDL-style engineering disciplines. |
| NASA CubeSat 101 (CSLI, challenge Resources) | CubeSat-class parts in `parts.json` (computer, battery) | Standard-size reference (1U = 10×10×11 cm, ~1–1.33 kg). Not yet cross-checked part by part. |
| NOAA SWPC scales | `donki_events.json` links, severity mapping | G-scale / flare class definitions. |

### Challenge "Resources" checklist

> TODO (team): open the "Resources" tab of the challenge we submitted to and tick each listed dataset.
> Anything unticked is a chance to add integration depth before judging.

| Dataset listed by the challenge | Used? | Where |
|---|---|---|
|  |  |  |

Known gap: DONKI is a bundled snapshot, not a live call at runtime. If time allows, add an optional live refresh; otherwise state this clearly on the project page.

## 2. Non-NASA sources and their terms

Only facts and figures are used. No text or images are copied from these sites.

| Source | Terms | Used for |
|---|---|---|
| NOAA SWPC, SEC EDGAR (US government) | Public domain; credit the agency, no endorsement implied | Space-weather scales; Rocket Lab launch revenue |
| Wikipedia | CC BY-SA 4.0; facts are not copyrightable, article credited | Falcon 9 / Falcon Heavy / Electron flight records, decoy parts, Svalbard station |
| eoPortal (ESA) | ESA terms; credit eoPortal | PRISMA, ICEYE, BIROS/FireBIRD, RADARSAT Constellation |
| SatCatalog, GomSpace, Busek, AWS, SpaceX, ULA | Vendor datasheets and price lists, cited by name | Camera and battery specs, ground-station and launch prices, launch timelines |
| Spaceflight Now, Universe Today, NASASpaceFlight, New Space Economy | News reporting, cited by name | Launch timelines, rideshare price, Transporter flights |
| arXiv, MIT OCW, MDPI Sensors, J. Astron. Space Sci., SmallSat Conference | Open-access papers and course notes, cited by name | Eclipse and power formulas, station keeping |
| Noto Sans TC, Rajdhani, Share Tech Mono, JetBrains Mono | SIL OFL 1.1; license files ship in `Art/Fonts/` | UI fonts |

Rocket and company names (Falcon 9, SpaceX, ULA, Rocket Lab) are used only to identify real products. No third-party logos are used.

## 3. Disclaimer shown in the game

"Fan-made educational game: no NASA endorsement implied." (title screen, `ui.title.credits`). The NASA insignia is not used anywhere.
