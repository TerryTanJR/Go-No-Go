using GoNoGo.Presentation;
using UnityEngine;

namespace GoNoGo.Launch
{
    /// <summary>
    /// 共乘任務（Falcon 9 Transporter）整流罩裡的其他衛星：幾層 ESPA 式的轉接環疊起來，
    /// 每一環側面的接口各掛一顆別人的小衛星或立方衛星發射盒，玩家的衛星裝在最上面。
    /// 真正的 Transporter 一次載一百多顆衛星，所以 1 m 的衛星在 5.2 m 的整流罩裡本來就很小，畫出鄰居才看得出比例。
    /// 尺寸：ESPA 環直徑約 1.6 m（62 吋）、高約 0.6 m（24 吋）。
    /// </summary>
    public static class RideshareStack
    {
        const float RingRadius = 0.79f;
        const float RingHeight = 0.61f;
        const float Pitch = 1.3f;    // 每一層的高度：環加上兩顆衛星之間的間隙
        const int Rings = 3;
        const int Ports = 6;

        static Mesh _cyl, _cube;
        static Mesh CylMesh => _cyl ??= Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        static Mesh CubeMesh => _cube ??= Resources.GetBuiltinResource<Mesh>("Cube.fbx");

        /// <summary>在 mount 上組出整疊共乘衛星，回傳最上面（玩家衛星底部）的高度。</summary>
        public static float Build(Transform mount, float fairingRadius)
        {
            var m = SatMaterials.Load();
            var root = new GameObject("RideshareStack").transform;
            root.SetParent(mount, false);
            if (m == null) return 0f;

            var rng = new System.Random(18); // 固定種子：每次看到的都是同一疊
            float maxReach = fairingRadius * 0.82f - RingRadius;
            for (int i = 0; i < Rings; i++)
            {
                float y = i * Pitch;
                // 環本體與上下法蘭
                Cyl(root, new Vector3(0, y + RingHeight / 2, 0), RingRadius, RingHeight, m.frameDark);
                Cyl(root, new Vector3(0, y + 0.02f, 0), RingRadius * 1.04f, 0.04f, m.metal);
                Cyl(root, new Vector3(0, y + RingHeight - 0.02f, 0), RingRadius * 1.04f, 0.04f, m.metal);
                // 環和環之間的轉接柱（上一層的衛星比環高，中間要撐起來）
                if (i < Rings - 1) Cyl(root, new Vector3(0, y + (RingHeight + Pitch) / 2, 0), RingRadius * 0.75f, Pitch - RingHeight, m.metal);

                float turn = i * 30f; // 每一層錯開半個接口，從側面看比較不會疊在一起
                for (int p = 0; p < Ports; p++)
                {
                    float a = (turn + p * 360f / Ports) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var port = new GameObject("Port" + i + "_" + p).transform;
                    port.SetParent(root, false);
                    port.localPosition = new Vector3(0, y + RingHeight / 2, 0) + dir * RingRadius;
                    port.localRotation = Quaternion.LookRotation(dir, Vector3.up); // +Z 朝外
                    Cyl(port, new Vector3(0, 0, 0.03f), 0.2f, 0.06f, m.metal, true);
                    if (rng.NextDouble() < 0.3) CubeSatDispensers(port, rng, m);
                    else Microsat(port, rng, m, maxReach);
                }
            }
            // 最上面接玩家衛星的轉接錐
            float top = (Rings - 1) * Pitch + RingHeight;
            Cyl(root, new Vector3(0, top + 0.15f, 0), RingRadius * 0.55f, 0.3f, m.metal);
            return top + 0.3f;
        }

        /// <summary>一顆 50–200 kg 級的小衛星：隔熱毯本體、朝外那面貼太陽能板，有的帶小天線。</summary>
        static void Microsat(Transform port, System.Random rng, SatMaterials m, float maxReach)
        {
            float depth = Mathf.Min(maxReach, Range(rng, 0.35f, 0.85f));
            float w = Range(rng, 0.45f, 0.9f), h = Range(rng, 0.45f, 1.05f);
            double pick = rng.NextDouble();
            var skin = pick < 0.45 ? m.mliGold : pick < 0.8 ? m.mliSilver : m.paintWhite;
            float z0 = 0.06f + depth / 2;
            Box(port, new Vector3(0, 0, z0), new Vector3(w, h, depth), skin);
            Box(port, new Vector3(0, 0, z0 + depth / 2 + 0.01f), new Vector3(w * 0.9f, h * 0.9f, 0.015f), m.solarCell);
            if (rng.NextDouble() < 0.5)
            {
                // 頂上一支小天線
                Cyl(port, new Vector3(w * 0.25f, h / 2 + 0.06f, z0), 0.07f, 0.12f, m.dish);
            }
        }

        /// <summary>立方衛星發射盒（每個裝幾顆 3U/6U）：深灰色的長盒子並排。</summary>
        static void CubeSatDispensers(Transform port, System.Random rng, SatMaterials m)
        {
            int n = rng.Next(2, 4);
            float w = 0.16f, gap = 0.04f;
            float x0 = -(n - 1) * (w + gap) / 2;
            for (int k = 0; k < n; k++)
            {
                float len = rng.NextDouble() < 0.5 ? 0.42f : 0.62f;
                Box(port, new Vector3(x0 + k * (w + gap), 0, 0.06f + len / 2), new Vector3(w, 0.28f, len), m.frameDark);
                Box(port, new Vector3(x0 + k * (w + gap), 0, 0.06f + len + 0.005f), new Vector3(w * 0.85f, 0.24f, 0.01f), m.metal);
            }
        }

        static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static GameObject Obj(Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject("part");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static void Box(Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = Obj(parent, CubeMesh, mat);
            go.transform.localPosition = center;
            go.transform.localScale = size;
        }

        /// <summary>圓柱：預設軸朝 +Y；alongZ 時軸朝 +Z（接口的圓盤）。</summary>
        static void Cyl(Transform parent, Vector3 center, float radius, float height, Material mat, bool alongZ = false)
        {
            var go = Obj(parent, CylMesh, mat);
            go.transform.localPosition = center;
            if (alongZ) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var m = CylMesh.bounds.size; // 內建 Cylinder.fbx 直徑 2、高 2
            go.transform.localScale = new Vector3(radius * 2 / m.x, height / m.y, radius * 2 / m.z);
        }
    }
}
