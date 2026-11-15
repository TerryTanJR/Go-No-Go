# Go/No-Go third-party asset credits

Paths are relative to `Assets/GoNoGo/`.

## Licenses in short

- **SIL Open Font License 1.1 (OFL)**: fonts may be bundled and embedded in the game. They may not be sold on their own. The license text sits next to each font as `Art/Fonts/OFL-*.txt` and must ship with the fonts.
- **NASA media usage guidelines** (https://www.nasa.gov/nasa-brand-center/images-and-media/): NASA-produced imagery, models and audio are generally not copyrighted in the US and may be used for educational, informational and commercial purposes. You must not imply NASA endorsement of this game. You must not use the NASA insignia, logotype or "meatball" as a game logo. Credit "NASA" (plus the center or mission named below) as the source. Third-party content inside NASA media, such as SpaceX/Intuitive Machines hardware sounds, is credited as recorded by NASA.

## Fonts (`Art/Fonts/`, OFL 1.1)

| File | Size | Source | Author |
|---|---|---|---|
| NotoSansTC-Regular.otf | 5,683,368 B | https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/SubsetOTF/TC/NotoSansTC-Regular.otf | Google / Adobe (Noto CJK) |
| NotoSansTC-Bold.otf | 5,839,972 B | https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/SubsetOTF/TC/NotoSansTC-Bold.otf | Google / Adobe (Noto CJK) |
| OFL-NotoSansCJK.txt | 4,301 B | https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/LICENSE | |
| Rajdhani-SemiBold.ttf | 390,340 B | https://raw.githubusercontent.com/google/fonts/main/ofl/rajdhani/Rajdhani-SemiBold.ttf | Indian Type Foundry |
| Rajdhani-Bold.ttf | 400,680 B | https://raw.githubusercontent.com/google/fonts/main/ofl/rajdhani/Rajdhani-Bold.ttf | Indian Type Foundry |
| OFL-Rajdhani.txt | 4,373 B | https://raw.githubusercontent.com/google/fonts/main/ofl/rajdhani/OFL.txt | |
| ShareTechMono-Regular.ttf | 43,272 B | https://raw.githubusercontent.com/google/fonts/main/ofl/sharetechmono/ShareTechMono-Regular.ttf | Carrois Apostrophe |
| OFL-ShareTechMono.txt | 4,429 B | https://raw.githubusercontent.com/google/fonts/main/ofl/sharetechmono/OFL.txt | |
| JetBrainsMono-VariableFont_wght.ttf | 187,208 B | https://raw.githubusercontent.com/google/fonts/main/ofl/jetbrainsmono/JetBrainsMono%5Bwght%5D.ttf | JetBrains |
| OFL-JetBrainsMono.txt | 4,399 B | https://raw.githubusercontent.com/google/fonts/main/ofl/jetbrainsmono/OFL.txt | |

Notes:
- We use static Noto Sans TC OTFs (CFF outlines; TextMeshPro/FreeType reads them) instead of google/fonts `NotoSansTC[wght].ttf` (11.9 MB). That variable font's default instance is wght=100 (Thin), and Unity would render it as Thin.
- The JetBrains Mono variable font defaults to wght=400 (Regular).

## Earth textures (`Art/Textures/Earth/`, NASA, equirectangular 2:1)

| File | Dimensions | Size | Source | Credit |
|---|---|---|---|---|
| earth_day_bmng_topo_bathy_200407_5400x2700.jpg | 5400x2700 RGB | 2,308,798 B | https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bmng-topography-bathymetry/july/world.topo.bathy.200407.3x5400x2700.jpg | NASA Earth Observatory, Reto Stöckli; Blue Marble Next Generation (July 2004) with topography & bathymetry |
| earth_night_blackmarble_2016_3600x1800.jpg | 3600x1800 RGB | 779,638 B | https://assets.science.nasa.gov/content/dam/science/esd/eo/images/imagerecords/144000/144898/BlackMarble_2016_01deg.jpg | NASA Earth Observatory, Joshua Stevens; Suomi NPP VIIRS data (Miguel Román, NASA GSFC); Black Marble 2016 |
| earth_topography_gebco08_5400x2700.jpg | 5400x2700 (grey) | 1,244,508 B | https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/topography/gebco_08_rev_elev_5400x2700.jpg | NASA Earth Observatory (BMNG), GEBCO_08 elevation; land elevation map with ocean = 0, usable as a height/bump map |
| earth_water_mask_5400x2700.png | 5400x2700 L | 93,202 B | Made from https://assets.science.nasa.gov/content/dam/science/esd/eo/images/bmng/bathymetry/gebco_08_rev_bath_5400x2700.jpg | NASA Earth Observatory (BMNG), GEBCO_08 bathymetry. Made locally with a Pillow threshold: ocean = 255, land = 0. Use as a specular/water mask |
| earth_clouds_svs4439_4096x2048.jpg | 4096x2048 L | 1,248,445 B | Made from https://svs.gsfc.nasa.gov/vis/a000000/a004400/a004439/frames/4096x2048_1x1_30p/clouds_cpc/flat_cpc_clouds_4096x2048.01000.tif | NASA Scientific Visualization Studio (SVS 4439, "Monsoons: Wet, Dry, Repeat..."), NOAA CPC merged IR clouds. Made locally: alpha channel only, with levels (v-60)*255/150 to remove haze. Covers about 60°N to 60°S; the poles are empty |

## Moon texture (`Art/Textures/Moon/`, NASA, equirectangular 2:1)

| File | Size | Source |
|---|---|---|
| moon_lroc_color_svs4720_2048x1024.jpg | 2048x1024 | NASA Scientific Visualization Studio, "CGI Moon Kit" (SVS 4720), `lroc_color_2k.jpg`, built from Lunar Reconnaissance Orbiter Camera (LROC) data. Credit: NASA's Scientific Visualization Studio. https://svs.gsfc.nasa.gov/4720/ . Re-encoded locally (JPEG q92), no other changes. |

## 3D models (`Art/Models/NASA/`, NASA 3D Resources, glTF binary)

Repo: https://github.com/nasa/NASA-3D-Resources (NASA media usage guidelines). Raw base: `https://raw.githubusercontent.com/nasa/NASA-3D-Resources/master/3D%20Models/`.

| File | Tris | Size | Source path (under `3D Models/`) |
|---|---|---|---|
| Sat_ISS.glb | about 44k (decimated locally) | 200,832 B | International Space Station (ISS), variant B. Decimated in Blender to reduce WebGL size. Used on the title screen |
| Ship_SpaceShuttle.glb | | 1,194,068 B | Space Shuttle, variant A. Used on the title screen |
| Rocket_Atlas6_Friendship7.glb | 164,622 | 1,026,724 B | Atlas 6 (Friendship 7)/Atlas 6 (Friendship 7).glb |
| Rocket_ExplorerJupiterC.glb | 8,933 | 149,372 B | Explorer Jupiter-C Rocket/Explorer Jupiter-C Rocket.glb |
| Sat_Landsat8.glb | 85,364 | 780,280 B | Landsat 8/Landsat 8.glb |
| Sat_OCO2.glb | 41,004 | 280,060 B | Orbiting Carbon Observatory (OCO) 2/Orbiting Carbon Observatory (OCO) 2.glb |
| Ground_DSN_34m.glb | 31,842 | 2,903,116 B | Deep Space Network 34-meter/Deep Space Network 34-meter.glb |
| Ground_MobileLauncher_PadOnly.glb | 11,652 | 149,408 B | Mobile Launcher/Mobile Launcher (pad only).glb |
| SatelliteKit/SatKit_body_1..3.glb | 320 / 1,032 / 656 | 2,916 / 5,256 / 4,944 B | Satellite Kit/Satellite Kit body N.glb |
| SatelliteKit/SatKit_radio_1..3.glb | 748 / 764 / 788 | 8,436 / 8,752 / 9,012 B | Satellite Kit/Satellite Kit radio N.glb |
| SatelliteKit/SatKit_wings_1..3.glb | 216 / 536 / 1,072 | 4,860 / 8,172 / 14,820 B | Satellite Kit/Satellite Kit wings N.glb |

Only `Sat_ISS.glb` and `Ship_SpaceShuttle.glb` are placed in a scene (the title screen, built by `Editor/Builders/SceneBuilder.cs`). The other models are kept in the repo but not used.

Import notes:
- Every file except Saturn V lists `KHR_draco_mesh_compression` as **required**. Unity needs glTFast plus the Draco package (`com.unity.cloud.draco`) to load them.
- Textured models use `EXT_texture_webp`, with PNG/JPG fallback images included.

## Audio (`Audio/NASA/`, NASA)

The four WAV files are used (`Audio/Resources/Audio/AudioBank.asset`). The two Artemis I MP3 files are not assigned to anything.

| File | Format / length | Size | nasa_id or page | Exact source URL |
|---|---|---|---|---|
| NASA_ArtemisI_Liftoff_Commentary.mp3 | MP3 192k 48 kHz stereo, ~8 s | 204,720 B | NASA "Sounds from Beyond" page, item "Liftoff" (Artemis I launch, 2022-11-16) | https://www.nasa.gov/wp-content/uploads/2023/02/liftoff1.mp3 |
| NASA_ArtemisI_WeRiseTogether_Commentary.mp3 | MP3 192k 48 kHz stereo, ~7 s | 178,798 B | NASA "Sounds from Beyond", item "We Rise Together" (Artemis I launch commentary) | https://www.nasa.gov/wp-content/uploads/2023/02/we-rise-together1.mp3 |
| NASA_IM2_CountdownNet_TerminalCount.wav | WAV 16-bit 24 kHz mono, 65.0 s | 3,120,044 B | KSC-20250226-AU-ILW01-0003-SpaceX_CLPS_IM-2_Live_Launch_Coverage_TechFeed-1_Countdown-2 ("SpaceX CLPS IM-2 Live Launch Coverage - TechFeed (1), Countdown (2)"), credit NASA/Isaac Watson | https://images-assets.nasa.gov/audio/KSC-20250226-AU-ILW01-0003-SpaceX_CLPS_IM-2_Live_Launch_Coverage_TechFeed-1_Countdown-2/KSC-20250226-AU-ILW01-0003-SpaceX_CLPS_IM-2_Live_Launch_Coverage_TechFeed-1_Countdown-2~orig.wav. Excerpt 60–125 s, countdown channel (R) only, downmixed and resampled locally. The terminal count is at about 43–58 s in the clip |
| NASA_IM2_PadMics_LiftoffRoar.wav | WAV 16-bit 24 kHz mono, 100.0 s | 4,800,044 B | KSC-20250226-AU-ILW01-0002-SpaceX_CLPS_IM-2_Live_Launch_Coverage_PadMic-1_PadMic-2 ("Pad Mic 1 (1), Pad Mic 2 (2)"), credit NASA/Isaac Watson | https://images-assets.nasa.gov/audio/KSC-20250226-AU-ILW01-0002-SpaceX_CLPS_IM-2_Live_Launch_Coverage_PadMic-1_PadMic-2/KSC-20250226-AU-ILW01-0002-SpaceX_CLPS_IM-2_Live_Launch_Coverage_PadMic-1_PadMic-2~orig.wav. Excerpt 115–215 s, both mics mixed. Ignition is at about 8–10 s. The source mics clip at peak roar |
| NASA_IM2_Beach_DistantLaunchRumble.wav | WAV 16-bit 24 kHz mono, 92.0 s | 4,416,044 B | KSC-20250226-AU-ILW01-0001-SpaceX_CLPS_IM-2_Live_Launch_Coverage_Beach-1_Causeway-2, credit NASA/Isaac Watson | https://images-assets.nasa.gov/audio/KSC-20250226-AU-ILW01-0001-SpaceX_CLPS_IM-2_Live_Launch_Coverage_Beach-1_Causeway-2/KSC-20250226-AU-ILW01-0001-SpaceX_CLPS_IM-2_Live_Launch_Coverage_Beach-1_Causeway-2~orig.wav. Excerpt 3–95 s, beach channel (L) |
| NASA_Juno_GanymedeFlyby_RadioWaves.wav | WAV 16-bit 24 kHz mono, 49.0 s | 2,353,484 B | NASA "Sounds from Beyond", "Audio of Juno's Ganymede Flyby" (Juno Waves instrument sonification; NASA/JPL-Caltech/SwRI/Univ. of Iowa) | https://www.nasa.gov/wp-content/uploads/2024/05/e2-wave-ganymede-flyby-compressed.wav. Downmixed and resampled locally |

All WAV excerpts were made with Python and numpy: 48 kHz audio passed through a windowed-sinc low-pass filter, decimated to 24 kHz, given fade-in/out, and peak-normalized to -1 dBFS.

## Game data sources (`Data/Resources/GameData/`)

Every number in the game data files has a `source` entry (title, URL and a note saying which values are sourced, derived or estimated). We use facts and figures only; no text or images are copied from these sites. The Space Apps rules ask teams to follow the terms of use of any non-US-government site they draw on, so the non-government sources are listed here:

| Source | Terms | Used for |
|---|---|---|
| NASA, NOAA, USGS, SEC EDGAR (US government) | Public domain; credit the agency, no endorsement implied | Most values: orbits, power, propulsion, avionics (NASA SST State-of-the-Art), OCO / OCO-2 press kits, DONKI and NOAA SWPC space weather, the 2010-04-05 geomagnetic storm (USGS Geomagnetism Program), Rocket Lab's launch revenue |
| Max Millennium Major Flare Watch (Montana State University) | Research log, cited by name | The 2011-02-15 X2.2 flare used in the historical OCO mission |
| Wikipedia | CC BY-SA 4.0 (facts are not copyrightable; we credit the article) | Falcon 9 / Falcon Heavy / Electron flight records, the 13 decoy parts, Svalbard station |
| eoPortal (ESA) | ESA terms; credit eoPortal | PRISMA, ICEYE, BIROS/FireBIRD, RADARSAT Constellation |
| SatCatalog, GomSpace, Busek, AWS, SpaceX, ULA | Vendor datasheets and price lists, cited by name | Camera and battery specs, ground-station and launch prices, launch timelines |
| Spaceflight Now, Universe Today, NASASpaceFlight, New Space Economy, Astronomy magazine | News reporting, cited by name | Launch timelines, rideshare price, Transporter flights; the 2011-02-15 flare |
| arXiv, MIT OCW, MDPI Sensors, J. Astron. Space Sci., SmallSat Conference | Open-access papers and course notes, cited by name | Eclipse and power formulas, station keeping, lunar orbit maintenance |

## Made by the team

These assets were made for this project, by hand or by scripts in this repo:

- Procedural textures in `Art/Textures/Generated/` (including the `T_Patch*` badges): drawn in code by `Editor/Builders/BuildUtil.cs` and `FacilityBuilder.cs`.
- Minigame sprites in `Resources/Minigames/`: rendered in Blender by `tools/minigame_art/*.py`.
- Control room and launch complex models in `Art/Models/Room/`: built by `tools/room/*.py`.
- Satellite instrument models in `Presentation/Resources/Presentation/Parts/`: built by `tools/parts/build_instruments.py`.
- Astronaut avatar parts (`Presentation/Avatar/Resources/Avatar/AvatarParts.glb`): `tools/avatar_blender/`.
- Launch smoke flipbook (`Launch/Experiments/Smoke_Billow_8x8.png`): Blender smoke simulation, see `tools/vfx/README.md`.
