# VFX 序列圖工具

發射煙霧的序列圖（flipbook）流程：Blender Mantaflow 模擬 → EEVEE 逐格算圖 → 拼成 8×8 圖集 → Unity Particle System 的 Texture Sheet Animation 播放。

## 檔案

| 檔案 | 用途 |
| --- | --- |
| `blender/smoke_billow.blend` | `SmokeLab` 場景：模擬範圍 4 m、解析度 96 + 2× 雜訊、亂流力場、長焦攝影機（200 mm，等效正交）、主光與補光 |
| `make_atlas.py` | 把 64 張逐格 PNG 拼成 8×8 圖集；每格四邊與底部做 alpha 淡出，避免方形邊界 |
| `preview.py` | 把透明 PNG 疊在深色底上並排，方便檢查 |

## 重做一次

1. 用 Blender 5.2 開 `blender/smoke_billow.blend`，切到 `SmokeLab` 場景。
2. 選 `SmokeDomain` → Physics → Fluid → **Bake All**（約 3–5 分鐘）。快取寫在 `blender/smoke_cache/`，不進 Git。
3. 算第 9–72 格（共 64 格），輸出成 `frames/f_009.png` … `f_072.png`（256×256、RGBA、透明背景）。
4. `python make_atlas.py frames ../../Assets/GoNoGo/Launch/Experiments/Smoke_Billow_8x8.png 8`
5. Unity 選單 **GoNoGo → Experiments → Build Smoke Lab** 重建測試場景；正式的發射場景也用同一張圖集（`ArtBuilder` 把 `M_Smoke_Billow.mat` 放進 `LaunchKit`），要更新就跑 **GoNoGo → Build → All Scenes**。

## 已知注意事項

- EEVEE 用正交攝影機時，光不會打進體積煙霧（整團黑色），所以改用 200 mm 長焦透視攝影機。
- 模擬範圍底部會把煙切平，`make_atlas.py` 的底部淡出就是為了蓋掉這條線。
- 圖集 2048×2048；WebGL 版由 `ArtBuilder` 設成 Crunch 壓縮（品質 85）來壓下載大小。
