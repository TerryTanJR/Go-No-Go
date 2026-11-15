using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using GoNoGo.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// MainMenu 與 Facility 場景。
    /// Facility 是一個場景裡的三個區域：組裝區（x -9…11、z -8…8）、中控室（x 12…34，中間一道自動門）、
    /// 大廳（z 34…50，多人才用，開始任務時傳送過去）。營運時中控室大螢幕顯示遠處（y -400）的地球縮景。
    /// </summary>
    public static partial class SceneBuilder
    {
        const float RoomH = 8.5f;
        const float BayX0 = -15f, BayX1 = 15f, CtrlX0 = 16f, CtrlX1 = 34f, RoomZ = 9f;
        /// <summary>組裝區南北牆（中控室和隔牆還是 ±RoomZ）。</summary>
        const float BayZ = 13.5f;   // 整合台在大廳正中央；中控室比它淺，隔牆補到這個深度
        /// <summary>中控室模型整體放大的倍數（以靠隔牆的西邊為基準）：房間、控制台、影像牆一起變大，功能位置照比例跟著動。</summary>
        const float CtrlS = 1.3f;
        /// <summary>南牆正中間的落地大窗，左右各 WinHalf。</summary>
        const float WinHalf = 6f;
        static readonly Vector3 DioramaPos = new Vector3(0, -400f, 0);

        // ================= MainMenu =================

        static string MainMenu(ArtBuilder.Art art, Mesh sphere)
        {
            var scene = NewScene();
            Env(art.starfield, new Color(0.18f, 0.2f, 0.26f), new Color(0.1f, 0.11f, 0.14f), new Color(0.03f, 0.03f, 0.04f));
            var sun = Sun(new Vector3(-0.5f, -0.55f, 0.65f), new Color(1f, 0.96f, 0.9f), 1.6f);
            sun.shadowStrength = 0.6f;

            // 地球在遠處左後方，讓右邊的 avatar 有背景
            var tilt = BuildUtil.Group("EarthTilt");
            tilt.position = new Vector3(-16f, -10f, 40f);
            tilt.rotation = Quaternion.Euler(0, 0, -23.4f);
            var earth = Planet("Earth", tilt, sphere, art.earth, art.atmosphere, Vector3.zero, 22f, out _);
            var spin = earth.AddComponent<Spin>();
            spin.axis = Vector3.up;
            spin.degreesPerSecond = 0.8f;

            // avatar 站的圓台
            var ped = BuildUtil.Group("Pedestal");
            ped.position = new Vector3(1.55f, -1.05f, 1.2f);
            var cyl = HiCylinder();
            MeshObj("Base", ped, cyl, art.trim, new Vector3(0, -0.1f, 0), 1f).transform.localScale = new Vector3(1.3f, 0.1f, 1.3f);
            MeshObj("Top", ped, Annulus("Disk_0_0.65", 0f, 0.65f), art.consoleBody, new Vector3(0, 0.001f, 0), 1f);
            MeshObj("Ring", ped, Annulus("Ring_0.65_0.7", 0.65f, 0.7f), art.accentGlow, new Vector3(0, 0.002f, 0), 1f);
            foreach (var r in ped.GetComponentsInChildren<MeshRenderer>()) r.receiveShadows = true;
            var av = new GameObject("MenuAvatar");
            av.transform.SetParent(ped, false);
            av.AddComponent<MenuAvatar>().scale = 1.35f;

            var key = BuildUtil.Light("KeyLight", LightType.Spot, new Color(1f, 0.95f, 0.88f), 22f, new Vector3(-0.6f, 2.6f, -2.2f), Quaternion.identity, 8f);
            key.transform.rotation = Quaternion.LookRotation(ped.position + Vector3.up * 0.6f - key.transform.position);
            key.spotAngle = 40f;
            var rim = BuildUtil.Light("RimLight", LightType.Spot, new Color(0.4f, 0.75f, 1f), 18f, new Vector3(3.6f, 2.2f, 2.6f), Quaternion.identity, 8f);
            rim.transform.rotation = Quaternion.LookRotation(ped.position + Vector3.up * 0.6f - rim.transform.position);
            rim.spotAngle = 45f;

            var cam = MainCamera(new Vector3(0, 0, -4.2f), new Vector3(0, -0.2f, 0), 40f, 0.1f, 500f);
            var drift = cam.gameObject.AddComponent<CameraDrift>();
            drift.positionAmplitude = new Vector3(0.05f, 0.03f, 0.02f);

            var post = BuildUtil.DefaultPost;
            post.bloom = 1.0f; post.bloomThreshold = 0.9f; post.vignette = 0.32f; post.contrast = 12f;
            BuildUtil.PostVolume("MainMenu", post);
            return Save(scene, SceneFlow.MainMenu);
        }

        // ================= Facility =================

        sealed class FacMats
        {
            public Material glow, red, redGlow, paper, ringFx, white, hazard, screenRt, screenOff;
        }

        static string Facility(ArtBuilder.Art art, Mesh sphere)
        {
            var scene = NewScene();
            // 室內：環境光要亮（天花板朝下，吃 ground 色；太暗會變成黑頂）
            Env(null, new Color(0.62f, 0.65f, 0.7f), new Color(0.46f, 0.48f, 0.52f), new Color(0.32f, 0.33f, 0.35f));
            var sun = Sun(new Vector3(-0.35f, -0.85f, 0.4f), new Color(1f, 0.97f, 0.92f), 1.3f);
            sun.shadowStrength = 0.55f;

            var fm = new FacMats
            {
                glow = BuildUtil.Lit("M_ConsoleGlow", new Color(0.1f, 0.25f, 0.3f), 0f, 0.6f, emission: new Color(0.12f, 0.55f, 0.8f)),
                red = BuildUtil.Lit("M_ButtonRed", new Color(0.75f, 0.08f, 0.06f), 0.1f, 0.7f, emission: new Color(0.6f, 0.04f, 0.02f)),
                redGlow = BuildUtil.Unlit("M_RedGlow", new Color(2.4f, 0.25f, 0.15f)),
                paper = BuildUtil.Lit("M_Paper", new Color(0.92f, 0.9f, 0.84f), 0f, 0.2f),
                ringFx = BuildUtil.Particle("M_RingFx", null, new Color(0.35f, 0.9f, 1f, 1f), true),
                white = BuildUtil.Lit("M_WhiteMatte", new Color(0.86f, 0.87f, 0.9f), 0f, 0.35f),
                hazard = BuildUtil.Lit("M_Hazard", new Color(0.95f, 0.72f, 0.1f), 0f, 0.4f),
                screenOff = BuildUtil.Unlit("M_ScreenIdle", new Color(0.05f, 0.08f, 0.12f)),
            };
            var rt = OpsScreenTexture();
            fm.screenRt = BuildUtil.Unlit("M_ScreenOps", Color.white, rt);

            var root = BuildUtil.Group("Facility");
            var bay = BuildUtil.Group("AssemblyBay", root);
            var ctrl = BuildUtil.Group("ControlRoom", root);
            var lobby = BuildUtil.Group("Lobby", root);

            BuildShell(art, fm, root, bay, ctrl);
            var stand = BuildStand(art, fm, bay);
            var delivery = BuildDelivery(art, fm, bay);
            BuildBayDecor(art, fm, bay);
            BuildBayConsoles(art, fm, bay);
            BuildBayRing(art, fm, BuildUtil.Group("AssemblyRing", root), sphere);
            BuildControlRoom(art, fm, ctrl);
            BuildControlDecor(art, fm, ctrl);
            BuildLobby(art, fm, lobby, sphere);
            var opsView = BuildDiorama(art, sphere, sun, rt);

            // ---- 出生點 ----
            var spawns = BuildUtil.Group("Spawns", root);
            Transform Spawn(string n, Vector3 p, float yaw)
            {
                var t = BuildUtil.Group(n, spawns);
                t.position = p;
                t.rotation = Quaternion.Euler(0, yaw, 0);
                return t;
            }

            // ---- 玩家鏡頭 ----
            var cam = BuildUtil.Camera("Main Camera", new Vector3(CtrlX0 + 3f, 1.6f, 0), Quaternion.Euler(0, 90, 0), 62f, 0.05f, 420f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.03f);

            var dir = new GameObject("FacilityDirector").AddComponent<FacilityDirector>();
            dir.assemblySpawn = Spawn("AssemblySpawn", new Vector3(0, 0.05f, -5.2f), 0);
            dir.controlSpawn = Spawn("ControlSpawn", new Vector3(CtrlX0 + 3f, 0.05f, 0), 90);
            dir.opsSpawn = Spawn("OpsSpawn", new Vector3(CtrlX0 + 4.5f, 0.05f, 0), 90);
            dir.lobbySpawn = Spawn("LobbySpawn", new Vector3(0, 0.05f, 38f), 0);
            dir.playerCamera = cam;
            dir.stand = stand;
            dir.delivery = delivery;
            dir.areaSplitX = DivX;
            dir.lobbyMinZ = 30f;

            Probe(bay, new Vector3((BayX0 + BayX1) / 2, 2.5f, 0), new Vector3(BayX1 - BayX0, RoomH, BayZ * 2));
            Probe(ctrl, CtrlOrigin + new Vector3(0, 3.5f * CtrlS, 0), new Vector3(22f, 7f, 18f) * CtrlS, 0.6f);

            var post = BuildUtil.DefaultPost;
            post.bloom = 0.8f; post.bloomThreshold = 1.0f; post.vignette = 0.2f; post.contrast = 8f; post.grain = 0.05f; post.exposure = 0.35f;
            BuildUtil.PostVolume("Facility", post);
            return Save(scene, SceneFlow.Facility);
        }

        static RenderTexture OpsScreenTexture()
        {
            const string path = "Assets/GoNoGo/Art/RT_OpsScreen.renderTexture";
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt == null)
            {
                rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { name = "RT_OpsScreen", antiAliasing = 1 };
                AssetDatabase.CreateAsset(rt, path);
            }
            return rt;
        }

        // ---------------- 牆、地板、門 ----------------

        /// <summary>有碰撞體的方塊（牆、桌子）：角色會被擋住。</summary>
        static GameObject Solid(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, bool shadows = false)
        {
            var go = BuildUtil.Prim(PrimitiveType.Cube, name, parent, pos, size, mat, collider: true);
            if (!shadows) go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static void BuildShell(ArtBuilder.Art art, FacMats fm, Transform root, Transform bay, Transform ctrl)
        {
            float bayW = BayX1 - BayX0, depth = BayZ * 2;
            float bayCx = (BayX0 + BayX1) / 2;
            // 中控室的地板、牆、天花板在 Blender 模型裡（BuildControlRoom）
            Solid("Floor", bay, new Vector3(bayCx, -0.1f, 0), new Vector3(bayW, 0.2f, depth), art.floor);
            Solid("DoorSill", root, new Vector3(DivX, -0.1f, 0), new Vector3(1f, 0.2f, 2.6f), art.trim);
            // 組裝區的牆與天花板改成深石板色（和大廳、中控室同一套深色風格），徽章與燈光才跳得出來
            var bayWall = BuildUtil.Lit("M_BayWall", new Color(0.13f, 0.15f, 0.19f), 0.25f, 0.5f);
            Solid("Ceiling", bay, new Vector3(bayCx, RoomH + 0.1f, 0), new Vector3(bayW + 2, 0.2f, depth + 2), bayWall);

            // 組裝區：白色無塵室
            Solid("WallW", bay, new Vector3(BayX0 - 0.15f, RoomH / 2, 0), new Vector3(0.3f, RoomH, depth), bayWall);
            Solid("WallN", bay, new Vector3(bayCx, RoomH / 2, BayZ + 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), bayWall);
            // 南牆（含大窗）在 BuildBayRing

            // 中間的隔牆（x 11…12），門洞 z -1.3…1.3、高 2.7
            const float doorHalf = 1.3f, doorH = 2.7f;
            // 隔牆要接上放大後的中控室牆（z ±9×倍數、模型西牆從 6×倍數 以上補起）
            float divZ = Mathf.Max(RoomZ * CtrlS, BayZ), divH = RoomH * CtrlS;
            float seg = divZ - doorHalf;
            Solid("DividerN", root, new Vector3(DivX, divH / 2, doorHalf + seg / 2), new Vector3(1f, divH, seg), art.trim);
            Solid("DividerS", root, new Vector3(DivX, divH / 2, -doorHalf - seg / 2), new Vector3(1f, divH, seg), art.trim);
            Solid("DividerTop", root, new Vector3(DivX, (divH + doorH) / 2, 0), new Vector3(1f, divH - doorH, doorHalf * 2), art.trim);
            Box("DoorFrameGlow", root, new Vector3(DivX - 0.52f, doorH + 0.04f, 0), new Vector3(0.04f, 0.06f, doorHalf * 2), art.accentGlow, shadows: false);
            Box("DoorFrameGlow2", root, new Vector3(DivX + 0.52f, doorH + 0.04f, 0), new Vector3(0.04f, 0.06f, doorHalf * 2), art.accentGlow, shadows: false);

            var door = BuildUtil.Group("Door", root);
            door.position = new Vector3(DivX, 0, 0);
            door.rotation = Quaternion.Euler(0, 90, 0); // 門的局部 x = 世界 -z：兩片沿 z 滑開
            var left = Solid("Left", door, new Vector3(-0.65f, doorH / 2, 0), new Vector3(1.3f, doorH, 0.12f), art.wallPanel);
            var right = Solid("Right", door, new Vector3(0.65f, doorH / 2, 0), new Vector3(1.3f, doorH, 0.12f), art.wallPanel);
            Box("Stripe", left.transform, new Vector3(0, -0.05f, 0.55f), new Vector3(1f, 0.04f, 0.2f), fm.hazard, shadows: false);
            Box("Stripe", right.transform, new Vector3(0, -0.05f, 0.55f), new Vector3(1f, 0.04f, 0.2f), fm.hazard, shadows: false);
            Box("Stripe", left.transform, new Vector3(0, -0.05f, -0.55f), new Vector3(1f, 0.04f, 0.2f), fm.hazard, shadows: false);
            Box("Stripe", right.transform, new Vector3(0, -0.05f, -0.55f), new Vector3(1f, 0.04f, 0.2f), fm.hazard, shadows: false);
            var sd = door.gameObject.AddComponent<SlidingDoor>();
            sd.left = left.transform;
            sd.right = right.transform;
            sd.openDistance = 1.25f;
            sd.triggerRange = 2.6f;
            Label(root, "ui.area.control", new Vector3(DivX - 0.9f, 3.1f, 0), "sign", 10f);
            Label(root, "ui.area.assembly", new Vector3(DivX + 0.9f, 3.1f, 0), "sign", 10f);

            // 燈：組裝區四盞聚光燈打在整合台；牆面、天花板的裝潢在 BuildBayDecor；中控室的燈在 BuildControlRoom
            for (int i = 0; i < 4; i++)
            {
                var p = Quaternion.Euler(0, i * 90f + 45f, 0) * new Vector3(0, RoomH - 0.3f, -5f) + new Vector3(0, 0, 0.5f);
                var target = new Vector3(0, 1.4f, 0.5f);
                var l = BuildUtil.Light("StandSpot" + i, LightType.Spot, i % 2 == 0 ? new Color(1f, 0.95f, 0.88f) : new Color(0.85f, 0.92f, 1f), 26f, p, Quaternion.LookRotation(target - p), 14f, LightShadows.None, bay);
                l.spotAngle = 42f;
                l.innerSpotAngle = 22f;
            }
        }

        // ---------------- 整合台 ----------------

        static IntegrationStand BuildStand(ArtBuilder.Art art, FacMats fm, Transform bay)
        {
            var go = new GameObject("IntegrationStand");
            go.transform.SetParent(bay, false);
            go.transform.localPosition = new Vector3(0, 0, 0.5f);
            var cyl = HiCylinder();
            var basePlate = MeshObj("Base", go.transform, cyl, art.trim, new Vector3(0, 0.175f, 0), 1f);
            basePlate.transform.localScale = new Vector3(6.2f, 0.25f, 6.2f);
            basePlate.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            basePlate.GetComponent<Renderer>().receiveShadows = true;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.175f, 0);
            col.size = new Vector3(6.0f, 0.35f, 6.0f);
            MeshObj("Top", go.transform, Annulus("Disk_0_3.0", 0f, 3.0f), art.consoleBody, new Vector3(0, 0.352f, 0), 1f).GetComponent<Renderer>().receiveShadows = true;
            MeshObj("Ring", go.transform, Annulus("Ring_3.0_3.12", 3.0f, 3.12f), art.accentGlow, new Vector3(0, 0.353f, 0), 1f);
            MeshObj("FloorRing", go.transform, Annulus("Ring_5.4_5.47", 5.4f, 5.47f), art.accentGlow, new Vector3(0, 0.004f, 0), 1f);
            // 安裝範圍提示：黃黑相間的地面圈
            MeshObj("InstallZone", go.transform, Annulus("Ring_5.47_5.62", 5.47f, 5.62f), fm.hazard, new Vector3(0, 0.003f, 0), 1f);

            // ---- 整合台周邊的精緻化（只加裝飾，不動碰撞與衛星位置）----
            // ---- 展場式打光：頭頂環上一圈聚光燈（冷暖交錯）從四面八方打向展品，台座邊緣有四盞向上的氣氛燈 ----
            float lightY = RoomH - 1.4f;
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                var pos = go.transform.position + new Vector3(Mathf.Cos(a) * 5.6f, lightY, Mathf.Sin(a) * 5.6f);
                var target = go.transform.position + new Vector3(0, 2.8f, 0);
                var sp = BuildUtil.Light("StageSpot" + i, LightType.Spot, i % 2 == 0 ? new Color(1f, 0.93f, 0.82f) : new Color(0.8f, 0.9f, 1f), 40f, pos, Quaternion.LookRotation(target - pos), 15f, LightShadows.None, go.transform);
                sp.spotAngle = 42f;
                sp.innerSpotAngle = 18f;
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f + Mathf.PI / 4f;
                var up = new Vector3(Mathf.Cos(a) * 3.6f, 0.2f, Mathf.Sin(a) * 3.6f);
                Box("Uplight", go.transform, up, new Vector3(0.5f, 0.12f, 0.5f), art.accentGlow, shadows: false);
                BuildUtil.Light("Uplight" + i, LightType.Point, new Color(0.4f, 0.85f, 1f), 2.4f, go.transform.position + up + Vector3.up * 0.4f, Quaternion.identity, 6f, LightShadows.None, go.transform);
            }
            // 頭頂的吊環：環形燈架，三條吊纜接到天花
            const float ringY = RoomH - 1.0f;
            MeshObj("GantryRing", go.transform, Annulus("Ring_5.2_5.7", 5.2f, 5.7f), art.trim, new Vector3(0, ringY, 0), 1f);
            MeshObj("GantryRingTop", go.transform, Annulus("Ring_5.2_5.7", 5.2f, 5.7f), art.trim, new Vector3(0, ringY + 0.12f, 0), 1f);
            MeshObj("GantryGlow", go.transform, Annulus("Ring_5.18_5.21", 5.18f, 5.21f), art.accentGlow, new Vector3(0, ringY - 0.005f, 0), 1f);
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                Box("RingLamp", go.transform, new Vector3(Mathf.Cos(a) * 5.45f, ringY - 0.04f, Mathf.Sin(a) * 5.45f), new Vector3(0.34f, 0.04f, 0.18f), art.lightPanel, Quaternion.Euler(0, -a * Mathf.Rad2Deg - 90f, 0), false);
            }
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + 0.5f;
                Box("RingCable", go.transform, new Vector3(Mathf.Cos(a) * 5.45f, (ringY + RoomH - 0.5f) / 2f + 0.06f, Mathf.Sin(a) * 5.45f), new Vector3(0.04f, RoomH - ringY - 0.5f + 0.12f, 0.04f), art.trim, shadows: false);
            }
            // 台座外緣的警示條與階梯
            MeshObj("SkirtStep", go.transform, Annulus("Ring_3.1_3.5", 3.1f, 3.5f), art.trim, new Vector3(0, 0.354f, 0), 1f);

            var rigGo = new GameObject("Satellite");
            rigGo.transform.SetParent(go.transform, false);
            rigGo.transform.localPosition = new Vector3(0, 0.4f, 0);
            var rig = rigGo.AddComponent<SatelliteRig>();
            rig.fixedScale = 3.0f;
            rig.maxSize = 6.6f;
            rig.bottomAnchor = true;
            rig.pickable = true;

            // 安裝特效：火花 + 擴散光環
            var sparks = new GameObject("Sparks");
            sparks.transform.SetParent(go.transform, false);
            sparks.transform.localPosition = new Vector3(0, 2.3f, 0);
            var ps = sparks.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(0.5f, 0.9f, 1f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = 0;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.25f;
            var psr = sparks.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = art.sparkle;
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.04f;
            psr.lengthScale = 2f;
            psr.shadowCastingMode = ShadowCastingMode.Off;

            var ring = new GameObject("RingFx");
            ring.transform.SetParent(go.transform, false);
            ring.transform.localPosition = new Vector3(0, 0.37f, 0);
            MeshObj("Ring", ring.transform, Annulus("Ring_2.2_2.3", 2.2f, 2.3f), fm.ringFx, Vector3.zero, 1f);

            var stand = go.AddComponent<IntegrationStand>();
            stand.rig = rig;
            stand.sparks = ps;
            stand.ringFx = ring.transform;
            stand.installRange = 5.5f;
            Label(go.transform, "ui.fac.stand", new Vector3(0, 7.4f, 0), "sign", 26f);
            return stand;
        }

        // ---------------- 收貨窗口與輸送帶 ----------------

        /// <summary>輸送帶：從西北角的收貨窗口出來，沿北牆往東走到末端擋板。皮帶面高 BeltY、中心線 z = BeltZ。</summary>
        const float BeltY = 0.86f, BeltZ = BayZ - 0.85f, BeltW = 1.1f, BeltX0 = BayX0 + 0.1f, BeltX1 = 6.3f;
        /// <summary>收貨窗口的外框（x BayX0…BoothX、z BoothZ0…BayZ），開口朝東。</summary>
        const float BoothX = BayX0 + 2f, BoothZ0 = BayZ - 1.7f, BoothH = 2.6f, OpenH = 1.5f;

        static Material _hazardStripe, _steel, _rubber;

        /// <summary>斜紋警示貼圖（黃黑）。</summary>
        static Material HazardStripe(string name, Vector2 tiling)
        {
            var tex = BuildUtil.SavePng("T_HazardStripe", 64, 64, (x, y) =>
                ((x + y) / 16) % 2 == 0 ? new Color(0.95f, 0.72f, 0.1f) : new Color(0.08f, 0.08f, 0.09f));
            var m = BuildUtil.Lit(name, Color.white, 0f, 0.45f, tex);
            m.SetTextureScale("_BaseMap", tiling);
            return m;
        }

        static DeliveryConveyor BuildDelivery(ArtBuilder.Art art, FacMats fm, Transform bay)
        {
            _hazardStripe = HazardStripe("M_HazardStripe", Vector2.one);
            _steel = BuildUtil.Lit("M_Steel", new Color(0.62f, 0.64f, 0.68f), 0.9f, 0.6f);
            _rubber = BuildUtil.Lit("M_Rubber", new Color(0.05f, 0.05f, 0.055f), 0f, 0.2f);
            var booth = BuildUtil.Lit("M_DeliveryBooth", new Color(0.16f, 0.3f, 0.48f), 0.3f, 0.55f);
            var curtain = BuildUtil.Lit("M_StripCurtain", new Color(0.5f, 0.62f, 0.68f), 0.1f, 0.92f);
            var lamp = BuildUtil.Unlit("M_StatusLamp", new Color(0.2f, 1.6f, 0.5f));

            var go = new GameObject("DeliveryConveyor");
            go.transform.SetParent(bay, false);
            var t = go.transform;
            var conv = go.AddComponent<DeliveryConveyor>();

            // ---- 收貨窗口：牆角凸出來的一座機台，開口有黃黑框和塑膠簾 ----
            float bx = (BayX0 + BoothX) / 2, bw = BoothX - BayX0, bz = (BoothZ0 + BayZ) / 2, bd = BayZ - BoothZ0;
            float side = 0.24f, openZ0 = BoothZ0 + side, openZ1 = BayZ - side, openW = openZ1 - openZ0;
            Solid("BoothTop", t, new Vector3(bx, (OpenH + BoothH) / 2, bz), new Vector3(bw, BoothH - OpenH, bd), booth, true);
            Solid("BoothS", t, new Vector3(bx, OpenH / 2, BoothZ0 + side / 2), new Vector3(bw, OpenH, side), booth, true);
            Solid("BoothN", t, new Vector3(bx, OpenH / 2, BayZ - side / 2), new Vector3(bw, OpenH, side), booth, true);
            Box("BoothBase", t, new Vector3(bx, 0.28f, bz), new Vector3(bw, 0.56f, openW), art.wallDark, shadows: false);
            Box("TunnelBack", t, new Vector3(BayX0 + 0.02f, OpenH / 2, bz), new Vector3(0.04f, OpenH, openW), art.wallDark, shadows: false);
            Box("BoothCap", t, new Vector3(bx + 0.02f, BoothH + 0.03f, bz), new Vector3(bw + 0.08f, 0.06f, bd + 0.04f), art.trim, shadows: false);
            Box("BoothStripe", t, new Vector3(BoothX + 0.005f, OpenH + 0.86f, bz), new Vector3(0.02f, 0.04f, bd), art.accentGlow, shadows: false);
            // 開口上方的指示板：三個朝下的箭頭指著出貨口
            Box("SignPlate", t, new Vector3(BoothX + 0.015f, OpenH + 0.45f, bz), new Vector3(0.03f, 0.5f, bd - 0.3f), art.wallDark, shadows: false);
            for (int i = 0; i < 3; i++)
            {
                float az = bz - 0.36f + i * 0.36f;
                Box("ArrowA", t, new Vector3(BoothX + 0.035f, OpenH + 0.45f, az - 0.055f), new Vector3(0.01f, 0.04f, 0.15f), art.accentGlow, Quaternion.Euler(40f, 0, 0), false);
                Box("ArrowB", t, new Vector3(BoothX + 0.035f, OpenH + 0.45f, az + 0.055f), new Vector3(0.01f, 0.04f, 0.15f), art.accentGlow, Quaternion.Euler(-40f, 0, 0), false);
            }
            // 開口的黃黑框
            Box("FrameS", t, new Vector3(BoothX + 0.05f, OpenH / 2, openZ0 - 0.06f), new Vector3(0.1f, OpenH + 0.12f, 0.14f), _hazardStripe, shadows: false);
            Box("FrameN", t, new Vector3(BoothX + 0.05f, OpenH / 2, openZ1 + 0.06f), new Vector3(0.1f, OpenH + 0.12f, 0.14f), _hazardStripe, shadows: false);
            Box("FrameTop", t, new Vector3(BoothX + 0.05f, OpenH + 0.06f, bz), new Vector3(0.1f, 0.14f, openW + 0.26f), _hazardStripe, shadows: false);
            // 窗口裡的暖色燈：讓開口看起來是通往倉庫的通道
            var inside = BuildUtil.Light("TunnelLight", LightType.Point, new Color(1f, 0.72f, 0.4f), 2.2f, new Vector3(BayX0 + 0.5f, 1.2f, bz), Quaternion.identity, 2.4f, LightShadows.None, t);
            inside.renderMode = LightRenderMode.ForcePixel;

            // 塑膠簾：樞紐在開口上緣，零件經過時往東掀
            const int strips = 6;
            var flaps = new Transform[strips];
            float sw = openW / strips;
            for (int i = 0; i < strips; i++)
            {
                var pivot = BuildUtil.Group("Flap" + i, t);
                pivot.localPosition = new Vector3(BoothX - 0.08f, OpenH - 0.01f, openZ0 + sw * (i + 0.5f));
                Box("Strip", pivot, new Vector3(0, -(OpenH - BeltY) / 2, 0), new Vector3(0.012f, OpenH - BeltY - 0.02f, sw - 0.012f), curtain, shadows: false);
                flaps[i] = pivot;
            }

            // 頂上的狀態燈（閒置綠、出貨琥珀）和出貨時旋轉的警示燈
            var cyl = HiCylinder();
            var lampBase = MeshObj("LampBase", t, cyl, art.trim, new Vector3(bx, BoothH + 0.1f, bz), 1f);
            lampBase.transform.localScale = new Vector3(0.32f, 0.04f, 0.32f);
            var dome = BuildUtil.Prim(PrimitiveType.Sphere, "StatusLamp", t, new Vector3(bx, BoothH + 0.22f, bz), Vector3.one * 0.24f, lamp);
            dome.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var spin = BuildUtil.Group("Beacon", t);
            spin.localPosition = new Vector3(bx, BoothH + 0.22f, bz);
            var bl = BuildUtil.Light("BeaconLight", LightType.Spot, new Color(1f, 0.6f, 0.15f), 7f, spin.position, Quaternion.LookRotation(new Vector3(1f, -0.25f, 0f)), 12f, LightShadows.None, spin);
            bl.spotAngle = 55f;
            bl.innerSpotAngle = 20f;
            Label(t, "ui.fac.delivery", new Vector3(BoothX + 0.1f, BoothH + 0.75f, bz), "sign", 22f);

            // ---- 輸送帶 ----
            float len = BeltX1 - BeltX0, cx = (BeltX0 + BeltX1) / 2;
            const float beltTile = 0.25f;
            var beltTex = BuildUtil.SavePng("T_ConveyorBelt", 64, 64, (x, y) =>
            {
                float v = 0.13f + ((x * 7 + y * 13) % 5) * 0.004f;
                if (x % 64 < 6) v = 0.24f;            // 橫向的防滑肋
                else if (x % 64 < 9) v = 0.07f;
                return new Color(v, v, v * 1.05f);
            });
            var beltMat = BuildUtil.Lit("M_ConveyorBelt", Color.white, 0f, 0.3f, beltTex);
            beltMat.SetTextureScale("_BaseMap", new Vector2(len / beltTile, 1f));

            Box("BeltBody", t, new Vector3(cx, (BeltY - 0.3f + BeltY - 0.01f) / 2, BeltZ), new Vector3(len, 0.29f, BeltW + 0.06f), art.consoleBody);
            var belt = Quad("Belt", t, new Vector3(cx, BeltY + 0.001f, BeltZ), Quaternion.Euler(90, 0, 0), new Vector2(len, BeltW - 0.02f), beltMat);
            foreach (float s in new[] { -1f, 1f })
                Box("Rail", t, new Vector3(cx, BeltY, BeltZ + s * (BeltW / 2 + 0.03f)), new Vector3(len, 0.12f, 0.05f), _steel, shadows: false);
            Box("RailGlow", t, new Vector3(cx + 0.4f, BeltY - 0.02f, BeltZ - BeltW / 2 - 0.058f), new Vector3(len - 0.8f, 0.025f, 0.01f), art.accentGlow, shadows: false);
            // 滾筒端蓋：前側一排小圓點
            for (float x = BoothX + 0.4f; x < BeltX1 - 0.2f; x += 0.5f)
                Box("Roller", t, new Vector3(x, BeltY - 0.12f, BeltZ - BeltW / 2 - 0.035f), new Vector3(0.06f, 0.06f, 0.01f), _steel, shadows: false);
            // 腳架
            for (float x = BoothX + 0.6f; x < BeltX1; x += 1.6f)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    Box("Leg", t, new Vector3(x, (BeltY - 0.3f) / 2, BeltZ + s * 0.34f), new Vector3(0.07f, BeltY - 0.3f, 0.07f), _steel);
                    Box("Foot", t, new Vector3(x, 0.01f, BeltZ + s * 0.34f), new Vector3(0.16f, 0.02f, 0.16f), art.trim, shadows: false);
                }
                Box("Brace", t, new Vector3(x, 0.16f, BeltZ), new Vector3(0.05f, 0.05f, 0.68f), _steel, shadows: false);
            }
            // 末端擋板
            Box("EndStop", t, new Vector3(BeltX1 + 0.06f, BeltY + 0.12f, BeltZ), new Vector3(0.12f, 0.36f, BeltW + 0.12f), _hazardStripe);
            Box("Bumper", t, new Vector3(BeltX1 - 0.02f, BeltY + 0.1f, BeltZ), new Vector3(0.04f, 0.18f, BeltW - 0.2f), _rubber, shadows: false);
            // 輸送帶上方：三座檢查拱門（掃描燈）+ 一條長燈條；全是裝飾，沒有碰撞
            foreach (float ax in new[] { -10.5f, -7.5f, -4.5f, -1.5f, 1.5f, 4.5f })
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    Box("ArchPost", t, new Vector3(ax, 1.0f, BeltZ + s * (BeltW / 2 + 0.12f)), new Vector3(0.1f, 2.0f, 0.1f), art.trim, shadows: false);
                    Box("ArchGlow", t, new Vector3(ax, 1.0f, BeltZ + s * (BeltW / 2 + 0.06f)), new Vector3(0.02f, 1.9f, 0.02f), art.accentGlow, shadows: false);
                }
                Box("ArchBeam", t, new Vector3(ax, 2.02f, BeltZ), new Vector3(0.16f, 0.14f, BeltW + 0.76f), art.trim, shadows: false);
                Box("Scanner", t, new Vector3(ax, 1.93f, BeltZ), new Vector3(0.04f, 0.03f, BeltW + 0.5f), lamp, shadows: false);
            }
            // 輸送帶東端之外：靠北牆的貨架一排
            var lmb = MakeLobbyMats(art);
            for (int i = 0; i < 3; i++)
                LobbyShelf(t, new Vector3(BeltX1 + 2.4f + i * 2.4f, 0, BayZ - 0.25f), lmb, art, fm).localRotation = Quaternion.Euler(0, 90f, 0);
            // 地上：輸送帶前的警示線
            var floorStripe = HazardStripe("M_HazardFloor", new Vector2((BeltX1 - BoothX) / 0.3f, 1f));
            Quad("FloorStripe", t, new Vector3((BoothX + BeltX1) / 2 + 0.05f, 0.004f, BeltZ - BeltW / 2 - 0.3f), Quaternion.Euler(90, 0, 0), new Vector2(BeltX1 - BoothX + 0.1f, 0.12f), floorStripe);

            // 看不見的牆：不要走上皮帶（不擋準星）
            var block = new GameObject("Block");
            block.transform.SetParent(t, false);
            block.transform.localPosition = new Vector3(cx, 0.6f, BeltZ);
            block.layer = 2;
            block.AddComponent<BoxCollider>().size = new Vector3(len, 1.2f, BeltW + 0.12f);

            // 零件的父物件轉 180°：局部 +Z 朝南（房間），零件的大面朝玩家
            var items = BuildUtil.Group("Items", t);
            items.localRotation = Quaternion.Euler(0, 180, 0);
            conv.items = items;
            conv.start = items.InverseTransformPoint(new Vector3(BayX0 + 0.25f, BeltY + 0.01f, BeltZ));
            conv.end = items.InverseTransformPoint(new Vector3(BeltX1 - 0.4f, BeltY + 0.01f, BeltZ));
            conv.entry = BoothX + 0.32f - (BayX0 + 0.25f);
            conv.spacing = 0.62f;
            conv.minSpacing = 0.4f;
            conv.speed = 2.4f;
            conv.itemSize = 0.5f;
            conv.belt = belt.GetComponent<Renderer>();
            conv.beltTile = beltTile;
            conv.flaps = flaps;
            conv.beacon = spin;
            conv.beaconLight = bl;
            conv.statusLamp = dome.GetComponent<Renderer>();

            // 皮帶上方的補光
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(BoothX + 2.2f + i * 4.6f, 3.2f, BeltZ - 1.4f);
                var l = BuildUtil.Light("BeltLight" + i, LightType.Spot, new Color(1f, 0.97f, 0.92f), 7f, p, Quaternion.LookRotation(new Vector3(p.x, BeltY, BeltZ) - p), 6f, LightShadows.None, t);
                l.spotAngle = 85f;
                l.innerSpotAngle = 55f;
            }
            return conv;
        }

        // ---------------- 組裝區裝潢 ----------------

        /// <summary>南牆上的長條（護牆板、燈線、分隔線）：在大窗那一段斷開。</summary>
        static void SplitS(Transform t, string name, float y, float z, float h, float d, Material m)
        {
            Box(name + "L", t, new Vector3((BayX0 - WinHalf) / 2, y, z), new Vector3(-WinHalf - BayX0, h, d), m, shadows: false);
            Box(name + "R", t, new Vector3((WinHalf + BayX1) / 2, y, z), new Vector3(BayX1 - WinHalf, h, d), m, shadows: false);
        }

        static void BuildBayDecor(ArtBuilder.Art art, FacMats fm, Transform bay)
        {
            var t = BuildUtil.Group("Decor", bay);
            float bayW = BayX1 - BayX0, depth = BayZ * 2, bayCx = (BayX0 + BayX1) / 2;
            var wainscot = BuildUtil.Lit("M_Wainscot", new Color(0.3f, 0.34f, 0.4f), 0.2f, 0.5f);
            var accentPanel = BuildUtil.Lit("M_BayAccent", new Color(0.16f, 0.3f, 0.48f), 0.3f, 0.55f);
            var line = BuildUtil.Lit("M_FloorLine", new Color(0.9f, 0.76f, 0.22f), 0f, 0.4f);
            const float wy = 1.1f;

            // ---- 牆：下半截深色護牆板、上緣發光條、肋條、水平分割線、頂部飾帶 ----
            Box("WainscotW", t, new Vector3(BayX0 + 0.03f, wy / 2, 0), new Vector3(0.06f, wy, depth), wainscot, shadows: false);
            Box("WainscotN", t, new Vector3(bayCx, wy / 2, BayZ - 0.03f), new Vector3(bayW, wy, 0.06f), wainscot, shadows: false);
            Box("GlowW", t, new Vector3(BayX0 + 0.065f, wy + 0.02f, 0), new Vector3(0.02f, 0.04f, depth), art.accentGlow, shadows: false);
            SplitS(t, "WainscotS", wy / 2, -BayZ + 0.03f, wy, 0.06f, wainscot);
            SplitS(t, "GlowS", wy + 0.02f, -BayZ + 0.065f, 0.04f, 0.02f, art.accentGlow);
            SplitS(t, "SeamS", 3.3f, -BayZ + 0.02f, 0.03f, 0.04f, art.trim);
            Box("UpperBandS", t, new Vector3(bayCx, RoomH - 0.5f, -BayZ + 0.05f), new Vector3(bayW, 1f, 0.1f), art.trim, shadows: false);
            Box("GlowN", t, new Vector3(bayCx, wy + 0.02f, BayZ - 0.065f), new Vector3(bayW, 0.04f, 0.02f), art.accentGlow, shadows: false);
            Box("SeamW", t, new Vector3(BayX0 + 0.02f, 3.3f, 0), new Vector3(0.04f, 0.03f, depth), art.trim, shadows: false);
            Box("SeamN", t, new Vector3(bayCx, 3.3f, BayZ - 0.02f), new Vector3(bayW, 0.03f, 0.04f), art.trim, shadows: false);
            for (int i = -2; i < 7; i++)
            {
                float x = -6.2f + i * 3.25f;
                if (!(x > -WinHalf - 0.1f && x < WinHalf + 0.1f))
                    Box("RibS" + i, t, new Vector3(x, RoomH / 2, -BayZ + 0.1f), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
                Box("RibN" + i, t, new Vector3(x, RoomH / 2, BayZ - 0.1f), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
            }
            foreach (float z in new[] { -5.2f, 4.4f })
                Box("RibW", t, new Vector3(BayX0 + 0.1f, RoomH / 2, z), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
            Box("UpperBandN", t, new Vector3(bayCx, RoomH - 0.5f, BayZ - 0.05f), new Vector3(bayW, 1f, 0.1f), art.trim, shadows: false);
            Box("UpperBandW", t, new Vector3(BayX0 + 0.05f, RoomH - 0.5f, 0), new Vector3(0.1f, 1f, depth), art.trim, shadows: false);

            // ---- 西牆主視覺：遊戲徽章（Art/Textures/Decals/Logo_TradeOff.png，圓外透明），兩側直條飾板 ----
            float ez = -0.4f, ey = 3.3f;
            const string logoPath = "Assets/GoNoGo/Art/Textures/Decals/Logo_TradeOff.png";
            var logoTex = BuildUtil.ConfigureTexture(logoPath, 1024, true, TextureWrapMode.Clamp);
            var logoMat = BuildUtil.Unlit("M_WallLogo", new Color(0.92f, 0.92f, 0.92f), logoTex);
            logoMat.SetFloat("_AlphaClip", 1f);
            logoMat.SetFloat("_Cutoff", 0.5f);
            logoMat.EnableKeyword("_ALPHATEST_ON");
            logoMat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(logoMat);
            // Quad 正面朝 -Z，轉 -90° 後朝東（房間），貼圖不會左右顛倒
            Quad("WallLogo", t, new Vector3(BayX0 + 0.04f, ey, ez), Quaternion.Euler(0, -90f, 0), new Vector2(3.3f, 3.3f), logoMat);
            Box("EmblemPanelA", t, new Vector3(BayX0 + 0.04f, wy + 0.06f + 2.0f, ez + 2.6f), new Vector3(0.05f, 4.0f, 0.5f), accentPanel, shadows: false);
            Box("EmblemPanelB", t, new Vector3(BayX0 + 0.04f, wy + 0.06f + 2.0f, ez - 2.6f), new Vector3(0.05f, 4.0f, 0.5f), accentPanel, shadows: false);
            Box("EmblemGlowA", t, new Vector3(BayX0 + 0.07f, wy + 0.06f + 2.0f, ez + 2.6f), new Vector3(0.01f, 3.6f, 0.04f), art.accentGlow, shadows: false);
            Box("EmblemGlowB", t, new Vector3(BayX0 + 0.07f, wy + 0.06f + 2.0f, ez - 2.6f), new Vector3(0.01f, 3.6f, 0.04f), art.accentGlow, shadows: false);
            Label(t, "ui.area.assembly", new Vector3(BayX0 + 0.3f, ey - 2.0f, ez), "sign", 16f);

            // ---- 天花板：燈板、橋式吊車（停在整合台西邊，不擋衛星） ----
            // 橫樑（大廳那種）：每 3.6 公尺一根，燈板只放在橫樑之間（原本 15 片太亮，改 10 片）
            for (int i = 0; i < 9; i++)
                Box("CeilingBeam", t, new Vector3(BayX0 + 1.9f + i * 3.6f, RoomH - 0.2f, 0), new Vector3(0.5f, 0.4f, depth), art.trim, shadows: false);
            for (int x = 0; x < 8; x++)
                for (int zi = 0; zi < 3; zi++)
                    if ((x + zi) % 2 == 0)
                        Quad("LightPanel", t, new Vector3(BayX0 + 3.7f + x * 3.6f, RoomH - 0.01f, (zi - 1) * 7.8f), Quaternion.Euler(-90, 0, 0), new Vector2(2.4f, 0.8f), art.lightPanel);
            const float runY = 5.35f, runZ = 5.9f;
            foreach (float s in new[] { -1f, 1f })
            {
                Box("Runway", t, new Vector3(bayCx, runY, s * runZ), new Vector3(bayW - 0.1f, 0.3f, 0.24f), art.trim);
                for (float x = BayX0 + 1.5f; x < BayX1; x += 4f)
                    Box("Hanger", t, new Vector3(x, (runY + 0.15f + RoomH) / 2, s * runZ), new Vector3(0.06f, RoomH - runY - 0.15f, 0.06f), _steel, shadows: false);
            }
            const float craneX = -5.4f, trolleyZ = 2.2f;
            Box("Bridge", t, new Vector3(craneX, runY - 0.32f, 0), new Vector3(0.4f, 0.36f, runZ * 2 + 0.2f), fm.hazard);
            foreach (float s in new[] { -1f, 1f })
            {
                Box("EndTruck", t, new Vector3(craneX, runY - 0.22f, s * runZ), new Vector3(0.9f, 0.24f, 0.36f), fm.hazard);
                Box("BridgeStripe", t, new Vector3(craneX, runY - 0.32f, s * (runZ - 0.7f)), new Vector3(0.42f, 0.37f, 0.9f), _hazardStripe, shadows: false);
            }
            Box("Trolley", t, new Vector3(craneX, runY - 0.62f, trolleyZ), new Vector3(0.7f, 0.3f, 0.6f), art.trim);
            var drum = BuildUtil.Prim(PrimitiveType.Cylinder, "HoistDrum", t, new Vector3(craneX, runY - 0.86f, trolleyZ), new Vector3(0.24f, 0.25f, 0.24f), _steel);
            drum.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Box("Cable", t, new Vector3(craneX, (runY - 0.9f + 3.3f) / 2, trolleyZ), new Vector3(0.025f, runY - 0.9f - 3.3f, 0.025f), art.trim, shadows: false);
            Box("HookBlock", t, new Vector3(craneX, 3.15f, trolleyZ), new Vector3(0.22f, 0.3f, 0.16f), fm.hazard);
            Box("Hook", t, new Vector3(craneX, 2.92f, trolleyZ), new Vector3(0.05f, 0.18f, 0.05f), _steel, shadows: false);
            Box("HookTip", t, new Vector3(craneX + 0.05f, 2.84f, trolleyZ), new Vector3(0.1f, 0.04f, 0.05f), _steel, shadows: false);

            // ---- 地板：整合台底下的深色工作區、門口進來的走道線 ----
            var pad = MeshObj("WorkPad", t, Annulus("Disk_0_6.6", 0f, 6.6f), art.floorMatte, new Vector3(0, 0.002f, 0.5f), 1f);
            pad.GetComponent<Renderer>().receiveShadows = true;
            foreach (float s in new[] { -1f, 1f })
                Quad("Walkway", t, new Vector3((3.6f + BayX1) / 2, 0.004f, s * 1.45f), Quaternion.Euler(90, 0, 0), new Vector2(BayX1 - 3.6f, 0.08f), line);

            // ---- 南牆：工作台＋工具板、儀器櫃、氣瓶 ----
            float wz = -BayZ;
            Solid("BenchBase", t, new Vector3(-10.0f, 0.42f, wz + 0.42f), new Vector3(2.8f, 0.84f, 0.7f), art.consoleBody, true);
            Box("BenchTop", t, new Vector3(-10.0f, 0.87f, wz + 0.45f), new Vector3(2.9f, 0.06f, 0.8f), fm.white);
            for (int i = 0; i < 4; i++)
                Box("Drawer", t, new Vector3(-11.05f + i * 0.7f, 0.5f, wz + 0.775f), new Vector3(0.6f, 0.02f, 0.01f), _steel, shadows: false);
            Box("Pegboard", t, new Vector3(-10.0f, 1.75f, wz + 0.04f), new Vector3(2.6f, 1.1f, 0.03f), wainscot, shadows: false);
            var tools = new[] { (0.08f, 0.5f), (0.3f, 0.12f), (0.06f, 0.36f), (0.22f, 0.22f), (0.05f, 0.44f), (0.34f, 0.1f), (0.1f, 0.3f), (0.18f, 0.26f) };
            for (int i = 0; i < tools.Length; i++)
            {
                var (w, h) = tools[i];
                Box("Tool", t, new Vector3(-11.05f + i * 0.3f, 1.85f - (i % 3) * 0.12f, wz + 0.07f), new Vector3(w, h, 0.03f), i % 3 == 0 ? fm.hazard : art.trim, shadows: false);
            }
            Box("BenchLamp", t, new Vector3(-10.0f, 2.38f, wz + 0.2f), new Vector3(2.4f, 0.04f, 0.3f), art.lightPanel, shadows: false);
            for (int i = 0; i < 3; i++)
            {
                float x = 8.2f + i * 0.95f;
                Solid("Cabinet", t, new Vector3(x, 1.05f, wz + 0.32f), new Vector3(0.9f, 2.1f, 0.6f), art.consoleBody, true);
                Box("CabinetDoor", t, new Vector3(x, 1.1f, wz + 0.625f), new Vector3(0.8f, 1.8f, 0.01f), wainscot, shadows: false);
                for (int k = 0; k < 4; k++)
                    Box("Vent", t, new Vector3(x, 0.35f + k * 0.06f, wz + 0.632f), new Vector3(0.6f, 0.02f, 0.01f), art.trim, shadows: false);
                Box("Led", t, new Vector3(x - 0.28f, 1.75f, wz + 0.632f), new Vector3(0.05f, 0.05f, 0.01f), i == 1 ? fm.redGlow : art.accentGlow, shadows: false);
                Box("LedBar", t, new Vector3(x + 0.05f, 1.75f, wz + 0.632f), new Vector3(0.4f, 0.02f, 0.01f), art.accentGlow, shadows: false);
            }
            var bottleMats = new[] { BuildUtil.Lit("M_GasN2", new Color(0.2f, 0.22f, 0.25f), 0.4f, 0.6f), BuildUtil.Lit("M_GasHe", new Color(0.55f, 0.42f, 0.2f), 0.4f, 0.6f) };
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(BayX0 + 0.4f + i * 0.42f, 0.7f, wz + 0.35f);
                BuildUtil.Prim(PrimitiveType.Cylinder, "GasBottle", t, p, new Vector3(0.3f, 0.7f, 0.3f), bottleMats[i % 2], collider: true);
                BuildUtil.Prim(PrimitiveType.Sphere, "BottleCap", t, p + Vector3.up * 0.7f, new Vector3(0.3f, 0.18f, 0.3f), bottleMats[i % 2]);
                BuildUtil.Prim(PrimitiveType.Cylinder, "Valve", t, p + Vector3.up * 0.84f, new Vector3(0.08f, 0.06f, 0.08f), _steel);
            }
            Box("BottleStrap", t, new Vector3(BayX0 + 0.82f, 1.1f, wz + 0.52f), new Vector3(1.3f, 0.05f, 0.02f), fm.hazard, shadows: false);

            BuildFilterWall(t);
            BuildPatchWall(t);
        }

        /// <summary>北牆上半截：整面送風濾網（無塵室的 HEPA 牆），格子和百葉畫在貼圖裡。</summary>
        static void BuildFilterWall(Transform t)
        {
            const int px = 128;
            var tex = BuildUtil.SavePng("T_FilterGrille", px, px, (x, y) =>
            {
                if (x < 4 || y < 4) return new Color(0.42f, 0.45f, 0.5f);          // 面板框
                if (x < 10 || y < 10 || x > px - 7 || y > px - 7) return new Color(0.8f, 0.82f, 0.85f);
                return y % 8 < 2 ? new Color(0.5f, 0.53f, 0.57f) : new Color(0.86f, 0.88f, 0.9f); // 百葉
            });
            const float y0 = 1.3f, y1 = 4.9f, tile = 1.2f;
            float x0 = BayX0 + 1.6f, x1 = BayX1 - 0.1f;
            var m = BuildUtil.Lit("M_FilterGrille", Color.white, 0.1f, 0.35f, tex);
            m.SetTextureScale("_BaseMap", new Vector2((x1 - x0) / tile, (y1 - y0) / tile));
            Quad("FilterWall", t, new Vector3((x0 + x1) / 2, (y0 + y1) / 2, BayZ - 0.065f), Quaternion.identity, new Vector2(x1 - x0, y1 - y0), m);
            Box("FilterFrameTop", t, new Vector3((x0 + x1) / 2, y1 + 0.03f, BayZ - 0.07f), new Vector3(x1 - x0, 0.06f, 0.06f), _steel, shadows: false);
            Box("FilterFrameBottom", t, new Vector3((x0 + x1) / 2, y0 - 0.03f, BayZ - 0.07f), new Vector3(x1 - x0, 0.06f, 0.06f), _steel, shadows: false);
        }

        /// <summary>
        /// 南牆高處一排任務徽章（像太空船組裝廠歷年任務的徽章牆）。圖案是程式畫的虛構徽章：
        /// 外框、星空、行星、軌道，每個配色不同；圓外透明。
        /// </summary>
        static readonly (Color bg, Color border, Color planet)[] PatchPalette =
        {
            (new Color(0.06f, 0.1f, 0.24f), new Color(1f, 0.5f, 0.12f), new Color(0.95f, 0.55f, 0.2f)),
            (new Color(0.12f, 0.05f, 0.2f), new Color(0.85f, 0.8f, 0.95f), new Color(0.75f, 0.35f, 0.9f)),
            (new Color(0.03f, 0.16f, 0.16f), new Color(0.3f, 0.95f, 0.8f), new Color(0.25f, 0.6f, 1f)),
            (new Color(0.2f, 0.04f, 0.04f), new Color(1f, 0.82f, 0.3f), new Color(0.9f, 0.3f, 0.2f)),
            (new Color(0.04f, 0.08f, 0.12f), new Color(0.9f, 0.92f, 0.95f), new Color(0.35f, 0.75f, 0.45f)),
            (new Color(0.08f, 0.12f, 0.3f), new Color(0.95f, 0.25f, 0.3f), new Color(0.85f, 0.85f, 0.9f)),
            (new Color(0.15f, 0.12f, 0.05f), new Color(0.4f, 0.8f, 1f), new Color(0.95f, 0.75f, 0.35f)),
            (new Color(0.02f, 0.02f, 0.05f), new Color(0.95f, 0.7f, 0.15f), new Color(0.5f, 0.85f, 1f)),
        };

        static void BuildPatchWall(Transform t)
        {
            var palette = PatchPalette;
            float[] ribs = { -12.7f, -9.45f, -6.2f, -2.95f, 0.3f, 3.55f, 6.8f, 10.05f, 13.3f };
            var mats = new Material[palette.Length];
            int n = 0;
            for (int b = 0; b + 1 < ribs.Length; b++)
                foreach (float off in new[] { -0.78f, 0.78f })
                {
                    int v = n % palette.Length;
                    if (mats[v] == null) mats[v] = PatchMaterial(v, palette[v]);
                    float x = (ribs[b] + ribs[b + 1]) / 2 + off;
                    n++;
                    if (x > -WinHalf - 0.55f && x < WinHalf + 0.55f) continue;   // 窗戶那一段不掛徽章
                    n--;
                    // Quad 正面朝 -Z，轉 180° 朝北（房間）
                    Quad("Patch" + n, t, new Vector3(x, 4.15f + (n % 2 == 0 ? 0f : 0.12f), -BayZ + 0.04f), Quaternion.Euler(0, 180f, 0), new Vector2(1.05f, 1.05f), mats[v]);
                    n++;
                }
        }

        static Material PatchMaterial(int variant, (Color bg, Color border, Color planet) c)
        {
            const int S = 256;
            var rng = new System.Random(1234 + variant * 31);
            float pcx = 0.35f + (float)rng.NextDouble() * 0.3f, pcy = 0.3f + (float)rng.NextDouble() * 0.25f, pr = 0.14f + (float)rng.NextDouble() * 0.1f;
            float tilt = (float)rng.NextDouble() * 1.2f - 0.6f, orbitA = 0.5f + (float)rng.NextDouble() * 0.15f, orbitB = 0.16f + (float)rng.NextDouble() * 0.1f;
            var stars = new Vector3[7];
            for (int i = 0; i < stars.Length; i++) stars[i] = new Vector3((float)rng.NextDouble() * 1.2f - 0.6f, (float)rng.NextDouble() * 1.2f - 0.6f, 0.012f + (float)rng.NextDouble() * 0.015f);
            bool rings = variant % 3 == 0;
            var tex = BuildUtil.SavePng("T_Patch" + variant, S, S, (x, y) =>
            {
                float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1;
                float r = Mathf.Sqrt(u * u + v * v);
                if (r > 0.98f) return new Color(0, 0, 0, 0);
                if (r > 0.86f) return c.border;
                if (r > 0.82f) return c.bg;
                if (r > 0.8f) return Color.Lerp(c.bg, Color.white, 0.4f);
                // 軌道（傾斜橢圓）：前半圈畫在行星前面
                float cu = u * Mathf.Cos(tilt) + v * Mathf.Sin(tilt), cv = -u * Mathf.Sin(tilt) + v * Mathf.Cos(tilt);
                float e = Mathf.Sqrt(cu * cu / (orbitA * orbitA) + cv * cv / (orbitB * orbitB));
                float du = u - (pcx - 0.5f) * 2 * 0.6f, dv = v - (pcy - 0.5f) * 2 * 0.6f;
                float pd = Mathf.Sqrt(du * du + dv * dv);
                if (Mathf.Abs(e - 1f) < 0.04f && cv < 0) return c.border;
                if (pd < pr) return Color.Lerp(c.planet, Color.white, Mathf.Clamp01((dv + du) * 0.8f) * 0.35f);
                if (rings && Mathf.Abs(Mathf.Sqrt(du * du + dv * dv / 0.12f) - pr * 1.9f) < 0.025f) return Color.Lerp(c.border, Color.white, 0.5f);
                if (Mathf.Abs(e - 1f) < 0.04f) return c.border;
                foreach (var s in stars)
                    if ((u - s.x) * (u - s.x) + (v - s.y) * (v - s.y) < s.z * s.z) return new Color(0.95f, 0.95f, 0.9f);
                return c.bg;
            }, wrap: TextureWrapMode.Clamp);
            var m = BuildUtil.Unlit("M_Patch" + variant, new Color(0.9f, 0.9f, 0.9f), tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- 控制台 ----------------

        /// <summary>
        /// 一張控制台桌子：桌面、螢幕、發光邊條（ConsoleTarget 的 glow）。yaw = 操作員面對的方向。
        /// </summary>
        static ConsoleTarget Console(ArtBuilder.Art art, FacMats fm, Transform parent, string name, Vector3 pos, float yaw,
            string panel, string titleKey, string bodyKey, string phases, Material screen, float width = 1.6f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var t = go.transform;
            go.transform.localScale = Vector3.one * 1.3f;   // 整台放大
            // 操作員站在 -Z 那一側，看向 +Z。桌體、斜面桌板、發光前緣、兩側翼板、雙層螢幕、鍵盤與按鍵列
            Solid("Body", t, new Vector3(0, 0.4f, 0.1f), new Vector3(width, 0.8f, 0.7f), art.consoleBody, true);
            Box("Plinth", t, new Vector3(0, 0.04f, 0.02f), new Vector3(width + 0.12f, 0.08f, 0.82f), art.trim, shadows: false);
            Box("Desk", t, new Vector3(0, 0.82f, 0), new Vector3(width + 0.14f, 0.05f, 0.92f), art.trim);
            var glow = Box("Glow", t, new Vector3(0, 0.85f, -0.43f), new Vector3(width + 0.14f, 0.03f, 0.04f), fm.glow, shadows: false);
            Box("FrontStripe", t, new Vector3(0, 0.55f, -0.255f), new Vector3(width - 0.1f, 0.03f, 0.01f), art.accentGlow, shadows: false);
            Box("Keyboard", t, new Vector3(0, 0.865f, -0.18f), new Vector3(width * 0.6f, 0.02f, 0.26f), art.wallDark, shadows: false);
            for (int i = 0; i < 5; i++)
                Box("Key", t, new Vector3(-width * 0.25f + i * width * 0.125f, 0.88f, -0.05f), new Vector3(0.07f, 0.02f, 0.05f), i % 2 == 0 ? art.accentGlow : fm.redGlow, shadows: false);
            foreach (float sx in new[] { -1f, 1f })
            {
                Box("Wing", t, new Vector3(sx * (width / 2 + 0.07f), 0.55f, 0.1f), new Vector3(0.06f, 1.1f, 0.78f), art.trim);
                Box("WingGlow", t, new Vector3(sx * (width / 2 + 0.105f), 0.96f, 0.1f), new Vector3(0.01f, 0.03f, 0.7f), art.accentGlow, shadows: false);
            }
            Box("Riser", t, new Vector3(0, 1.28f, 0.34f), new Vector3(width, 0.95f, 0.08f), art.consoleBody);
            // Quad 正面朝 -Z（朝操作員），上緣往後仰 10°；上下兩層螢幕，下層是原本的畫面
            if (screen != null)
            {
                Quad("Screen", t, new Vector3(0, 1.1f, 0.285f), Quaternion.Euler(10f, 0, 0), new Vector2(width - 0.15f, 0.5f), screen);
                Quad("ScreenTop", t, new Vector3(0, 1.62f, 0.3f), Quaternion.Euler(6f, 0, 0), new Vector2(width - 0.15f, 0.42f), art.consoleScreens[(System.Array.IndexOf(art.consoleScreens, screen) + 1) % art.consoleScreens.Length]);
            }
            Box("TopBar", t, new Vector3(0, 1.79f, 0.34f), new Vector3(width + 0.04f, 0.04f, 0.1f), art.accentGlow, shadows: false);
            var ct = go.AddComponent<ConsoleTarget>();
            ct.panel = panel;
            ct.titleKey = titleKey;
            ct.bodyKey = bodyKey;
            ct.phases = phases;
            ct.glow = glow.GetComponent<Renderer>();
            Label(t, titleKey, new Vector3(0, 2.05f, 0.2f), "tag", 9f);
            return ct;
        }

        /// <summary>
        /// 組裝工作台：一張厚實的工作桌（操作員站在 -Z 那一側看向 +Z）。桌面中央是發光的組裝墊，
        /// 後面一片掛工具的洞洞板和工作燈，桌上一排小零件盒。按 E 打開組裝面板（ConsoleTarget panel = assemble）。
        /// </summary>
        static Workbench BuildWorkbench(ArtBuilder.Art art, FacMats fm, Transform parent, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * 1.3f;   // 和控制台一樣整台放大
            var t = go.transform;
            const float w = 2.4f;
            // 桌體：兩側櫃腳、中間留空、厚桌板、前緣發光條
            foreach (float sx in new[] { -1f, 1f })
                Solid("Leg", t, new Vector3(sx * (w / 2 - 0.25f), 0.4f, 0.1f), new Vector3(0.5f, 0.8f, 0.8f), art.consoleBody, true);
            Solid("Apron", t, new Vector3(0, 0.62f, 0.4f), new Vector3(w - 1f, 0.36f, 0.2f), art.consoleBody, true);
            Box("Plinth", t, new Vector3(0, 0.04f, 0.1f), new Vector3(w + 0.1f, 0.08f, 0.92f), art.trim, shadows: false);
            Solid("Top", t, new Vector3(0, 0.84f, 0.1f), new Vector3(w + 0.16f, 0.08f, 1.0f), art.trim, true);
            var glow = Box("Glow", t, new Vector3(0, 0.845f, -0.41f), new Vector3(w + 0.16f, 0.035f, 0.04f), fm.glow, shadows: false);
            for (int i = 0; i < 4; i++)
                Box("Drawer", t, new Vector3((i < 2 ? -1 : 1) * (w / 2 - 0.25f), 0.24f + (i % 2) * 0.3f, -0.305f), new Vector3(0.4f, 0.24f, 0.012f), art.wallDark, shadows: false);
            // 組裝墊：桌面中央一塊防靜電墊，四周一圈亮邊，零件就在這上面組
            Box("Mat", t, new Vector3(0, 0.885f, 0.02f), new Vector3(1.0f, 0.012f, 0.62f), art.wallDark, shadows: false);
            Box("MatGlowF", t, new Vector3(0, 0.893f, -0.3f), new Vector3(1.04f, 0.008f, 0.02f), art.accentGlow, shadows: false);
            Box("MatGlowB", t, new Vector3(0, 0.893f, 0.34f), new Vector3(1.04f, 0.008f, 0.02f), art.accentGlow, shadows: false);
            Box("MatGlowL", t, new Vector3(-0.51f, 0.893f, 0.02f), new Vector3(0.02f, 0.008f, 0.66f), art.accentGlow, shadows: false);
            Box("MatGlowR", t, new Vector3(0.51f, 0.893f, 0.02f), new Vector3(0.02f, 0.008f, 0.66f), art.accentGlow, shadows: false);
            // 桌上兩側的小零件盒（一格一格、顏色不同）
            var binMats = new[] { fm.hazard, fm.white, art.accentGlow, fm.red };
            for (int i = 0; i < 4; i++)
            {
                float bx = (i < 2 ? -1 : 1) * (0.78f + (i % 2) * 0.24f);
                Box("Bin", t, new Vector3(bx, 0.93f, 0.2f), new Vector3(0.2f, 0.1f, 0.26f), art.wallDark, shadows: false);
                Box("BinParts", t, new Vector3(bx, 0.975f, 0.2f), new Vector3(0.16f, 0.02f, 0.22f), binMats[i], shadows: false);
            }
            // 後面的洞洞板：掛著幾樣工具（長短不一的條狀物），上面一盞工作燈
            Box("Board", t, new Vector3(0, 1.5f, 0.56f), new Vector3(w, 1.2f, 0.06f), art.consoleBody);
            Box("BoardTrim", t, new Vector3(0, 2.12f, 0.56f), new Vector3(w + 0.06f, 0.05f, 0.1f), art.trim, shadows: false);
            // 背面（朝整合台那一面）：兩條亮邊和一片黃黑警示板，從整合台走過來時看得出這是一座機台，不是一面黑牆
            Box("BackGlowTop", t, new Vector3(0, 2.0f, 0.595f), new Vector3(w - 0.2f, 0.03f, 0.01f), art.accentGlow, shadows: false);
            Box("BackGlowLow", t, new Vector3(0, 1.0f, 0.595f), new Vector3(w - 0.2f, 0.03f, 0.01f), art.accentGlow, shadows: false);
            Box("BackPlate", t, new Vector3(0, 1.5f, 0.595f), new Vector3(1.2f, 0.5f, 0.012f), art.wallDark, shadows: false);
            Box("BackHazard", t, new Vector3(0, 1.2f, 0.6f), new Vector3(1.2f, 0.06f, 0.012f), _hazardStripe, shadows: false);
            Box("BackSkirt", t, new Vector3(0, 0.45f, 0.51f), new Vector3(w - 1.0f, 0.7f, 0.02f), art.consoleBody, shadows: false);
            float[] toolX = { -1.06f, -0.9f, -0.74f, 0.74f, 0.9f, 1.06f };
            float[] toolH = { 0.42f, 0.3f, 0.36f, 0.34f, 0.46f, 0.28f };
            for (int i = 0; i < toolX.Length; i++)
            {
                Box("Tool", t, new Vector3(toolX[i], 1.62f - toolH[i] / 2, 0.52f), new Vector3(0.05f, toolH[i], 0.03f), i % 2 == 0 ? _steel : fm.hazard, shadows: false);
                Box("Hook", t, new Vector3(toolX[i], 1.64f, 0.52f), new Vector3(0.09f, 0.03f, 0.04f), art.trim, shadows: false);
            }
            // 洞洞板中間的螢幕：組裝單（現在組的是什麼、下一個小零件要做什麼事）。畫面由 UI 層畫上去（WorldScreen "bench"）
            Box("ScreenFrame", t, new Vector3(0, 1.5f, 0.535f), new Vector3(1.26f, 0.84f, 0.03f), art.trim, shadows: false);
            var screen = Quad("Screen", t, new Vector3(0, 1.5f, 0.515f), Quaternion.identity, new Vector2(1.2f, 0.78f), fm.screenOff);
            var ws = screen.AddComponent<WorldScreen>();
            ws.id = "bench";
            ws.resolution = new Vector2Int(768, 500);
            ws.target = screen.GetComponent<Renderer>();
            Box("LampArm", t, new Vector3(0, 2.18f, 0.3f), new Vector3(0.05f, 0.05f, 0.56f), art.trim, shadows: false);
            Box("Lamp", t, new Vector3(0, 2.15f, 0.04f), new Vector3(1.3f, 0.04f, 0.14f), art.lightPanel, shadows: false);
            var lamp = BuildUtil.Light("BenchLight", LightType.Spot, new Color(1f, 0.95f, 0.85f), 9f, Vector3.zero, Quaternion.identity, 4f, LightShadows.None, t);
            lamp.transform.localPosition = new Vector3(0, 2.1f, 0.04f);
            lamp.transform.localRotation = Quaternion.Euler(90, 0, 0);
            lamp.spotAngle = 70f;

            // 組裝墊的中心：箱子、裝好的小零件、成品都擺在這上面
            var jig = new GameObject("Jig").transform;
            jig.SetParent(t, false);
            jig.localPosition = new Vector3(0, 0.892f, 0.02f);

            var wb = go.AddComponent<Workbench>();
            wb.jig = jig;
            wb.glow = glow.GetComponent<Renderer>();
            wb.sparks = Sparks(art, t, new Vector3(0, 1.0f, 0.02f), 0.12f);
            Label(t, "ui.fac.bench", new Vector3(0, 2.4f, 0.3f), "tag", 9f);
            return wb;
        }

        /// <summary>裝上零件時噴的火花（不循環，程式呼叫 Emit）。</summary>
        static ParticleSystem Sparks(ArtBuilder.Art art, Transform parent, Vector3 localPos, float radius)
        {
            var sparks = new GameObject("Sparks");
            sparks.transform.SetParent(parent, false);
            sparks.transform.localPosition = localPos;
            var ps = sparks.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.6f), new Color(0.5f, 0.9f, 1f));
            main.gravityModifier = 1.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = 0;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
            var psr = sparks.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = art.sparkle;
            psr.renderMode = ParticleSystemRenderMode.Stretch;
            psr.velocityScale = 0.04f;
            psr.lengthScale = 2f;
            psr.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>
        /// 零件盤：一張長桌上排著 <see cref="GameData.TraySize"/> 個盤子。工作台上放了一箱小零件，盤子裡就擺出小零件
        /// （執行時由 <see cref="CompTray"/> 放模型和名稱）。局部 -Z 是拿零件的人那一面。
        /// </summary>
        static void BuildCompRack(ArtBuilder.Art art, FacMats fm, Transform parent, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * 1.2f;
            var t = go.transform;
            const int n = GameData.TraySize;
            const float pitch = 0.58f, w = pitch * n + 0.2f;
            // 桌體：兩側櫃腳、桌板、前緣發光條、後面一道矮背板
            foreach (float sx in new[] { -1f, 1f })
                Solid("Leg", t, new Vector3(sx * (w / 2 - 0.2f), 0.41f, 0.05f), new Vector3(0.4f, 0.82f, 0.6f), art.consoleBody, true);
            Solid("Shelf", t, new Vector3(0, 0.3f, 0.05f), new Vector3(w - 0.8f, 0.05f, 0.56f), art.wallDark, true);
            Box("Plinth", t, new Vector3(0, 0.04f, 0.05f), new Vector3(w + 0.08f, 0.08f, 0.7f), art.trim, shadows: false);
            Solid("Top", t, new Vector3(0, 0.85f, 0.05f), new Vector3(w + 0.1f, 0.06f, 0.72f), art.trim, true);
            Box("Glow", t, new Vector3(0, 0.852f, -0.315f), new Vector3(w + 0.1f, 0.03f, 0.03f), fm.glow, shadows: false);
            Box("Back", t, new Vector3(0, 1.2f, 0.4f), new Vector3(w + 0.1f, 0.7f, 0.05f), art.consoleBody);
            Box("BackGlow", t, new Vector3(0, 1.5f, 0.37f), new Vector3(w - 0.1f, 0.03f, 0.012f), art.accentGlow, shadows: false);
            Box("BackHazard", t, new Vector3(0, 0.93f, 0.37f), new Vector3(w - 0.1f, 0.05f, 0.012f), _hazardStripe, shadows: false);
            Box("LampArm", t, new Vector3(0, 1.62f, 0.3f), new Vector3(w * 0.7f, 0.04f, 0.26f), art.trim, shadows: false);
            Box("Lamp", t, new Vector3(0, 1.595f, 0.22f), new Vector3(w * 0.66f, 0.015f, 0.12f), art.lightPanel, shadows: false);
            var lamp = BuildUtil.Light("RackLight", LightType.Spot, new Color(1f, 0.95f, 0.85f), 7f, Vector3.zero, Quaternion.identity, 3.5f, LightShadows.None, t);
            lamp.transform.localPosition = new Vector3(0, 1.9f, -0.1f);
            lamp.transform.localRotation = Quaternion.Euler(80, 0, 0);
            lamp.spotAngle = 110f;

            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) / 2f) * pitch;
                var tray = new GameObject("Tray" + i);
                tray.transform.SetParent(t, false);
                tray.transform.localPosition = new Vector3(x, 0.88f, 0.02f);
                var tt = tray.transform;
                // 盤子：深色盤底、四邊矮框、前緣一條小燈（對到時變亮）
                Box("Base", tt, new Vector3(0, 0.01f, 0), new Vector3(0.48f, 0.02f, 0.42f), art.wallDark, shadows: false);
                Box("RimF", tt, new Vector3(0, 0.035f, -0.2f), new Vector3(0.48f, 0.05f, 0.02f), art.consoleBody, shadows: false);
                Box("RimB", tt, new Vector3(0, 0.035f, 0.2f), new Vector3(0.48f, 0.05f, 0.02f), art.consoleBody, shadows: false);
                Box("RimL", tt, new Vector3(-0.23f, 0.035f, 0), new Vector3(0.02f, 0.05f, 0.42f), art.consoleBody, shadows: false);
                Box("RimR", tt, new Vector3(0.23f, 0.035f, 0), new Vector3(0.02f, 0.05f, 0.42f), art.consoleBody, shadows: false);
                var led = Box("Led", tt, new Vector3(0, 0.03f, -0.214f), new Vector3(0.3f, 0.02f, 0.01f), fm.glow, shadows: false);
                var slot = new GameObject("Slot").transform;
                slot.SetParent(tt, false);
                slot.localPosition = new Vector3(0, 0.022f, 0);
                var col = tray.AddComponent<BoxCollider>();
                col.center = new Vector3(0, 0.19f, 0);
                col.size = new Vector3(0.5f, 0.38f, 0.44f);
                var ct = tray.AddComponent<CompTray>();
                ct.index = i;
                ct.slot = slot;
                ct.glow = led.GetComponent<Renderer>();
            }
            Label(t, "ui.fac.rack", new Vector3(0, 1.85f, 0.3f), "tag", 9f);
        }

        /// <summary>牆上的大螢幕：畫面由 UI 層依 id 畫上去（WorldScreen）。</summary>
        static GameObject WallScreen(ArtBuilder.Art art, FacMats fm, Transform parent, string name, string id, Vector3 pos, float yaw, Vector2 size, Vector2Int res)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var t = go.transform;
            // 框：外框 + 內側細發光邊 + 上方燈條 + 下方窄台座（局部 -Z 是看的人那一面）
            // 外框一定要比螢幕面更靠牆（前表面離螢幕 ≥ 1 cm），否則兩層同一深度會閃
            Box("Frame", t, new Vector3(0, 0, 0.05f), new Vector3(size.x + 0.24f, size.y + 0.24f, 0.08f), art.trim, shadows: false);
            Box("GlowTop", t, new Vector3(0, size.y / 2 + 0.1f, -0.01f), new Vector3(size.x + 0.1f, 0.025f, 0.03f), art.accentGlow, shadows: false);
            Box("GlowBottom", t, new Vector3(0, -size.y / 2 - 0.1f, -0.01f), new Vector3(size.x + 0.1f, 0.025f, 0.03f), art.accentGlow, shadows: false);
            Box("Ledge", t, new Vector3(0, -size.y / 2 - 0.2f, -0.08f), new Vector3(size.x * 0.6f, 0.06f, 0.2f), art.trim, shadows: false);
            Box("LampBar", t, new Vector3(0, size.y / 2 + 0.28f, -0.1f), new Vector3(size.x * 0.5f, 0.05f, 0.14f), art.lightPanel, shadows: false);
            foreach (float sx in new[] { -1f, 1f })
                Box("Bracket", t, new Vector3(sx * size.x * 0.25f, size.y / 2 + 0.2f, -0.03f), new Vector3(0.05f, 0.18f, 0.09f), art.trim, shadows: false);
            // Quad 正面朝 -Z：讓它面向看的人（局部 -Z）
            var q = Quad("Screen", t, Vector3.zero, Quaternion.identity, size, fm.screenOff);
            var ws = go.AddComponent<WorldScreen>();
            ws.id = id;
            ws.resolution = res;
            ws.target = q.GetComponent<Renderer>();
            return go;
        }

        static void BuildBayConsoles(ArtBuilder.Art art, FacMats fm, Transform bay)
        {
            // 採購終端：組裝區下單買零件（沒有價格，價格在中控室的型錄）
            var proc = Console(art, fm, bay, "ProcureConsole", new Vector3(6.2f, 0, -4.6f), -52f, "procure", "ui.fac.procure", "ui.fac.procure_body", "design,briefing", art.consoleScreens[0], 2.4f);
            proc.actionKey = "ui.hint.procure";

            // 組裝工作台：買來的零件是一箱小零件，要先在這裡組好才能裝上船。位置和採購終端左右對稱
            BuildWorkbench(art, fm, bay, "AssemblyBench", new Vector3(-6.2f, 0, -4.6f), 52f);
            // 零件盤：在工作台的左前方、和工作台排成 L 形（盤子朝向工作台前面那塊空地）。
            // 小零件要自己走過去拿、拿回來裝。不放在操作的人正後方：兩張桌子面對面時，
            // 第三人稱的鏡頭會被身後那一張擋住、貼到桌面上。
            var benchRot = Quaternion.Euler(0, 52f, 0);
            BuildCompRack(art, fm, bay, "CompRack", new Vector3(-6.2f, 0, -4.6f) + benchRot * new Vector3(-3.2f, 0, -3.2f), 52f - 90f);
            // （環境測試台在採購機制 v2 拿掉了：發射前不再檢查）

            // 東牆（門的南邊）：失敗原因看板
            var fb = WallScreen(art, fm, bay, "FailureBoard", "failures", new Vector3(BayX1 - 0.08f, 3.4f, -6.2f), 90f, new Vector2(6.6f, 4.1f), new Vector2Int(1024, 640));
            var ct = fb.AddComponent<ConsoleTarget>();
            ct.panel = "failures";
            ct.titleKey = "ui.fac.failures";
            ct.bodyKey = "ui.fac.failures_body";
            ct.actionKey = "ui.hint.read";
            fb.AddComponent<BoxCollider>().size = new Vector3(6.8f, 4.3f, 0.12f);

            // 東牆（門的北邊）：儀表板大螢幕，整合台上這艘船現在的數字
            var ds = WallScreen(art, fm, bay, "BayDashboard", "dashboard", new Vector3(BayX1 - 0.08f, 3.4f, 6.2f), 90f, new Vector2(6.6f, 4.1f), new Vector2Int(1024, 640));
            var dt = ds.AddComponent<ConsoleTarget>();
            dt.panel = "dashboard";
            dt.titleKey = "ui.fac.dashboard";
            dt.bodyKey = "ui.fac.dashboard_body";
            dt.actionKey = "ui.hint.read";
            ds.AddComponent<BoxCollider>().size = new Vector3(6.8f, 4.3f, 0.12f);
        }

        // ---------------- 組裝大廳（組裝區南側的擴建） ----------------
        //
        // 組裝區南牆中間開了一個出入口，通往這間大廳：中央是升高的衛星組裝平台（大型衛星展示、桁架吊架、工作燈），
        // 西邊是工作台與零件架，東邊是熱真空艙、振動台、貨架與堆高機，南牆是落地大窗，窗外是地球。
        // 全部只是裝潢，沒有任何互動功能；原本組裝區的控制台、傳送帶、整合台位置完全不動。

        /// <summary>展覽台：圓台、四根立柱框成展櫃、頂環、聚光燈、自轉的展品（0 立方衛星、1 大型衛星、2 火箭模型）。</summary>
        static void Exhibit(Transform parent, Vector3 localPos, int variant, LobbyMats lm, ArtBuilder.Art art, FacMats fm)
        {
            var g = BuildUtil.Group("Exhibit" + variant, parent);
            g.localPosition = localPos;
            BuildUtil.Prim(PrimitiveType.Cylinder, "Step", g, new Vector3(0, 0.06f, 0), new Vector3(2.9f, 0.06f, 2.9f), art.trim);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Plinth", g, new Vector3(0, 0.4f, 0), new Vector3(2.3f, 0.3f, 2.3f), art.consoleBody, collider: true);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Band", g, new Vector3(0, 0.55f, 0), new Vector3(2.34f, 0.025f, 2.34f), lm.asmGlow);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Top", g, new Vector3(0, 0.71f, 0), new Vector3(2.1f, 0.02f, 2.1f), lm.steel);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f + Mathf.PI / 4f;
                var post = Box("Post", g, new Vector3(Mathf.Cos(a) * 1.2f, 2.0f, Mathf.Sin(a) * 1.2f), new Vector3(0.1f, 3.0f, 0.1f), art.trim, shadows: false);
                Box("PostGlow", g, new Vector3(Mathf.Cos(a) * 1.2f, 2.0f, Mathf.Sin(a) * 1.2f), new Vector3(0.03f, 2.8f, 0.03f), lm.asmGlow, shadows: false).transform.localPosition += new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.06f;
            }
            MeshObj("TopRing", g, Annulus("ExRing_1.15_1.3", 1.15f, 1.3f), art.trim, new Vector3(0, 3.5f, 0), 1f);
            MeshObj("TopGlow", g, Annulus("ExRing_1.1_1.14", 1.1f, 1.14f), lm.asmGlow, new Vector3(0, 3.49f, 0), 1f);
            var spot = BuildUtil.Light("Spot", LightType.Spot, new Color(0.85f, 0.95f, 1f), 14f, g.position + new Vector3(0, 5.8f, 0), Quaternion.Euler(90, 0, 0), 9f, LightShadows.None, g);
            spot.spotAngle = 34f;
            spot.innerSpotAngle = 14f;

            var m = BuildUtil.Group("Model", g);
            m.localPosition = new Vector3(0, 1.85f, 0);
            m.localScale = Vector3.one * 0.62f;
            m.gameObject.AddComponent<Spinner>().degreesPerSecond = 22f;
            if (variant == 0)
            {
                // 立方衛星
                Box("Body", m, Vector3.zero, new Vector3(0.55f, 0.55f, 0.9f), art.mli);
                Box("Frame", m, Vector3.zero, new Vector3(0.57f, 0.57f, 0.12f), art.mliSilver);
                foreach (float s in new[] { -1f, 1f })
                {
                    Box("Wing", m, new Vector3(s * 0.8f, 0, 0), new Vector3(1.0f, 0.03f, 0.6f), lm.solar, shadows: false);
                    Box("Spar", m, new Vector3(s * 0.4f, 0, 0), new Vector3(0.3f, 0.03f, 0.03f), lm.steel, shadows: false);
                }
                BuildUtil.Prim(PrimitiveType.Cylinder, "Antenna", m, new Vector3(0, 0.55f, 0.2f), new Vector3(0.03f, 0.3f, 0.03f), lm.steel);
            }
            else if (variant == 1)
            {
                // 大型衛星：金色車身、碟形天線、兩側長太陽能板
                Box("Body", m, Vector3.zero, new Vector3(0.9f, 0.9f, 1.1f), art.mli);
                Box("Band", m, new Vector3(0, 0.3f, 0), new Vector3(0.94f, 0.08f, 1.14f), art.mliSilver);
                BuildUtil.Prim(PrimitiveType.Sphere, "Dish", m, new Vector3(0, 0.75f, 0), new Vector3(0.9f, 0.2f, 0.9f), art.mliSilver);
                BuildUtil.Prim(PrimitiveType.Cylinder, "Mast", m, new Vector3(0, 0.55f, 0), new Vector3(0.05f, 0.2f, 0.05f), lm.steel);
                foreach (float s in new[] { -1f, 1f })
                {
                    Box("Arm", m, new Vector3(s * 0.8f, 0, 0), new Vector3(0.7f, 0.05f, 0.05f), lm.steel, shadows: false);
                    for (int k = 0; k < 3; k++)
                        Box("Wing", m, new Vector3(s * (1.5f + k * 0.62f), 0, 0), new Vector3(0.58f, 0.03f, 0.9f), lm.solar, shadows: false);
                }
            }
            else
            {
                // 火箭模型：白色箭身、橘色環、黑色鼻錐、三片尾翼
                BuildUtil.Prim(PrimitiveType.Cylinder, "Body", m, new Vector3(0, 0, 0), new Vector3(0.5f, 0.85f, 0.5f), art.wallPanel);
                BuildUtil.Prim(PrimitiveType.Cylinder, "Ring", m, new Vector3(0, 0.3f, 0), new Vector3(0.52f, 0.05f, 0.52f), fm.hazard);
                BuildUtil.Prim(PrimitiveType.Sphere, "Nose", m, new Vector3(0, 1.0f, 0), new Vector3(0.5f, 0.7f, 0.5f), lm.dark);
                for (int i = 0; i < 3; i++)
                    Box("Fin", m, new Vector3(Mathf.Cos(i * 2.094f) * 0.32f, -0.65f, Mathf.Sin(i * 2.094f) * 0.32f), new Vector3(0.05f, 0.5f, 0.32f), fm.hazard, Quaternion.Euler(0, -i * 120f, 0), false);
            }
        }

        static void RobotArm(Transform parent, Vector3 pos, float yaw, Material body, Material joint, Material dark)
        {
            var arm = BuildUtil.Group("RobotArm", parent);
            arm.position = pos;
            arm.rotation = Quaternion.Euler(0, yaw, 0);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Base", arm, new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.25f, 0.9f), joint, collider: true);
            Box("Shoulder", arm, new Vector3(0, 0.65f, 0), new Vector3(0.5f, 0.5f, 0.5f), body);
            Box("Arm1", arm, new Vector3(0.3f, 1.35f, 0), new Vector3(0.22f, 1.4f, 0.22f), body, Quaternion.Euler(0, 0, -25f));
            Box("Elbow", arm, new Vector3(0.6f, 1.95f, 0), new Vector3(0.34f, 0.34f, 0.34f), joint);
            Box("Arm2", arm, new Vector3(1.1f, 1.75f, 0), new Vector3(0.18f, 1.1f, 0.18f), body, Quaternion.Euler(0, 0, 70f));
            Box("Claw", arm, new Vector3(1.7f, 1.55f, 0), new Vector3(0.12f, 0.3f, 0.3f), dark);
        }

        static Material _dayDomeMat;

        /// <summary>
        /// 白天的天空球貼圖：上半是天空（地平線偏白、往上漸藍、幾朵雲），下半是遠方的霧氣（和遠處地面接得起來），
        /// 左右可以無縫接起來。球從裡面看（正面剔除），所以往上、往左右看都是天空，不會露出黑色背景。
        /// </summary>
        static Material SkyMaterial()
        {
            if (_dayDomeMat != null) return _dayDomeMat;
            var tex = BuildUtil.SavePng("T_DaySkyDome", 1024, 512, (x, y) =>
            {
                float v = y / 511f;                                  // 0 = 球底，0.5 = 地平線，1 = 頂
                var haze = new Color(0.72f, 0.77f, 0.8f);
                var low = new Color(0.9f, 0.92f, 0.9f);
                var high = new Color(0.36f, 0.62f, 0.92f);
                Color c;
                if (v < 0.5f) c = Color.Lerp(new Color(0.55f, 0.6f, 0.62f), haze, Mathf.SmoothStep(0f, 1f, v * 2f));
                else
                {
                    float h = (v - 0.5f) * 2f;
                    c = Color.Lerp(low, high, Mathf.Pow(h, 0.6f));
                    float n = BuildUtil.TileFbm(x / 1024f * 9f, y / 512f * 5f, 9, 4, 7);
                    float cloud = Mathf.SmoothStep(0.5f, 0.78f, n) * Mathf.Clamp01(1.4f - h * 1.3f) * Mathf.SmoothStep(0.02f, 0.2f, h) * 0.9f;
                    c = Color.Lerp(c, Color.white, cloud);
                }
                c.a = 1f;
                return c;
            }, wrap: TextureWrapMode.Repeat);
            var m = BuildUtil.Unlit("M_DaySkyDome", Color.white, tex);
            m.SetFloat("_Cull", 1f);   // 剔除正面：從球裡面看
            EditorUtility.SetDirty(m);
            return _dayDomeMat = m;
        }

        const string LaunchComplexModel = "Assets/GoNoGo/Art/Models/Room/LaunchComplex.glb";
        static Dictionary<string, Material> _outsideMats;

        static Material OutsideMat(string name, Color c, float metal = 0f, float smooth = 0.3f, Texture tex = null, Vector2? tiling = null)
        {
            var m = BuildUtil.Lit("M_Out_" + name, c, metal, smooth, tex);
            if (tex != null && tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
            return m;
        }

        /// <summary>柏油與混凝土的程序貼圖：細顆粒雜訊 + 偶爾的裂縫 / 接縫。</summary>
        static Texture2D PavementTexture(string name, Color baseC, float grain, bool joints)
        {
            return BuildUtil.SavePng(name, 256, 256, (x, y) =>
            {
                float n = BuildUtil.TileFbm(x / 256f * 10f, y / 256f * 10f, 10, 4, 3) - 0.5f;
                float s = (BuildUtil.Noise(x * 0.9f, y * 0.9f, 5) - 0.5f) * 0.5f;
                float v = 1f + n * grain + s * grain * 0.6f;
                var c = baseC * v;
                if (joints && (x % 128 < 2 || y % 128 < 2)) c *= 0.7f;
                c.a = 1f;
                return c;
            });
        }

        /// <summary>
        /// 窗外的景：Blender 做的發射場（tools/room/build_launch_complex.py → Art/Models/Room/LaunchComplex.glb）——
        /// 灰白大廠房、發射塔與火箭、避雷針塔、燃料球槽、路燈、柏油地面與履帶車道。沒有樹和草（總部和發射台一帶本來就很空曠）。
        /// pos = 窗戶外側的位置，yaw 讓局部 +Z 指向窗外。全部是遠景裝飾，沒有碰撞。
        /// </summary>
        static void LaunchComplexView(Transform parent, Vector3 pos, float yaw, ArtBuilder.Art art)
        {
            var g = BuildUtil.Group("OutsideView", parent);
            g.position = pos;
            g.rotation = Quaternion.Euler(0, yaw, 0);
            var dome = BuildUtil.Prim(PrimitiveType.Sphere, "SkyDome", g, new Vector3(0, 0, 60f), Vector3.one * 600f, SkyMaterial());
            dome.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            dome.GetComponent<Renderer>().receiveShadows = false;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaunchComplexModel);
            if (prefab == null) throw new System.Exception("找不到窗外模型 " + LaunchComplexModel + "（先跑 tools/room/build_launch_complex.py）");
            if (_outsideMats == null)
            {
                _outsideMats = new Dictionary<string, Material>
                {
                    ["M_Ground"] = OutsideMat("Asphalt", Color.white, 0f, 0.15f, PavementTexture("T_Asphalt", new Color(0.24f, 0.25f, 0.27f), 0.55f, false), new Vector2(1f, 1f)),
                    ["M_Road"] = OutsideMat("Road", Color.white, 0f, 0.2f, PavementTexture("T_RoadConcrete", new Color(0.46f, 0.47f, 0.48f), 0.3f, true), new Vector2(1f, 1f)),
                    ["M_Gravel"] = OutsideMat("Gravel", Color.white, 0f, 0.05f, PavementTexture("T_Gravel", new Color(0.52f, 0.48f, 0.42f), 0.9f, false), new Vector2(1f, 1f)),
                    ["M_Line"] = OutsideMat("Line", new Color(0.95f, 0.85f, 0.3f)),
                    ["M_Pad"] = OutsideMat("Pad", new Color(0.62f, 0.62f, 0.6f)),
                    ["M_VabWhite"] = OutsideMat("VabWhite", new Color(0.88f, 0.89f, 0.9f)),
                    ["M_VabGray"] = OutsideMat("VabGray", new Color(0.5f, 0.52f, 0.57f)),
                    ["M_VabDark"] = OutsideMat("VabDark", new Color(0.13f, 0.14f, 0.17f)),
                    ["M_VabGlass"] = OutsideMat("VabGlass", new Color(0.2f, 0.32f, 0.46f), 0.2f, 0.7f),
                    ["M_Steel"] = OutsideMat("Steel", new Color(0.55f, 0.58f, 0.62f), 0.6f, 0.45f),
                    ["M_SteelDark"] = OutsideMat("SteelDark", new Color(0.28f, 0.3f, 0.34f), 0.6f, 0.45f),
                    ["M_RocketWhite"] = OutsideMat("RocketWhite", new Color(0.93f, 0.93f, 0.94f), 0.1f, 0.5f),
                    ["M_RocketOrange"] = OutsideMat("RocketOrange", new Color(0.84f, 0.42f, 0.11f), 0.1f, 0.5f),
                    ["M_RocketBlack"] = OutsideMat("RocketBlack", new Color(0.05f, 0.05f, 0.06f), 0.2f, 0.4f),
                    ["M_Tank"] = OutsideMat("Tank", new Color(0.9f, 0.91f, 0.92f), 0.2f, 0.6f),
                    ["M_Lamp"] = BuildUtil.Unlit("M_Out_Lamp", new Color(3f, 0.3f, 0.2f)),
                };
            }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "LaunchComplexModel";
            model.transform.SetParent(g, false);
            // glTFast 匯入後軸向翻轉：轉 180° 讓 Blender (x, y, z) = Unity (x, z, y)
            model.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] != null ? ms[i].name : "";
                    foreach (var kv in _outsideMats) if (n.StartsWith(kv.Key)) { ms[i] = kv.Value; break; }
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            // 隊伍徽章貼在大廠房主樓正面的壁板上（位置對應 Blender 腳本裡的 vab()）
            Quad("VabLogo", g, new Vector3(-26f, 54f, 84.4f), Quaternion.identity, new Vector2(12f, 12f), BannerLogo());
        }

        /// <summary>
        /// 組裝大廳四周的「沒有功能的裝飾」：南牆的落地大窗、南側的展示平台（太陽能板架、貨箱、工具車）、
        /// 西牆的零件架與工作台、東牆的堆高機與熱真空艙、兩支機械手臂。整合台、傳送帶、控制台在正中央，這些全部沿著牆放。
        /// </summary>
        static void BuildBayRing(ArtBuilder.Art art, FacMats fm, Transform ring, Mesh sphere)
        {
            var lm = MakeLobbyMats(art);
            var wall = BuildUtil.Lit("M_BayWall", new Color(0.13f, 0.15f, 0.19f), 0.25f, 0.5f);
            const float W = BayX0, E = BayX1, Z1 = -BayZ, H = RoomH;

            // ---- 南牆：中間落地大窗，窗外是地球和月亮 ----
            float segL = (-WinHalf - (W - 0.3f)), segR = (E + 0.3f - WinHalf);
            Solid("WallS_L", ring, new Vector3((W - 0.3f - WinHalf) / 2, H / 2, Z1 - 0.15f), new Vector3(segL, H, 0.3f), wall);
            Solid("WallS_R", ring, new Vector3((WinHalf + E + 0.3f) / 2, H / 2, Z1 - 0.15f), new Vector3(segR, H, 0.3f), wall);
            Solid("WallS_Low", ring, new Vector3(0, 0.6f, Z1 - 0.15f), new Vector3(WinHalf * 2, 1.2f, 0.3f), wall);
            Solid("WallS_High", ring, new Vector3(0, (5.6f + H) / 2, Z1 - 0.15f), new Vector3(WinHalf * 2, H - 5.6f, 0.3f), wall);
            var glass = Solid("WindowGlass", ring, new Vector3(0, 3.4f, Z1 - 0.15f), new Vector3(WinHalf * 2, 4.4f, 0.1f), art.trim);
            Object.DestroyImmediate(glass.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(glass.GetComponent<MeshFilter>());
            for (int k = 0; k <= 4; k++)
                Box("Mullion", ring, new Vector3(-WinHalf + k * WinHalf / 2, 3.4f, Z1 + 0.02f), new Vector3(0.14f, 4.4f, 0.16f), art.trim, shadows: false);
            LaunchComplexView(ring, new Vector3(0, 0, Z1 - 0.3f), 180f, art);
            Box("WindowSill", ring, new Vector3(0, 1.25f, Z1 + 0.1f), new Vector3(WinHalf * 2, 0.06f, 0.3f), art.trim, shadows: false);
            Box("SillGlow", ring, new Vector3(0, 1.29f, Z1 + 0.2f), new Vector3(WinHalf * 2 - 0.4f, 0.03f, 0.04f), lm.asmGlow, shadows: false);
            foreach (float sx in new[] { -WinHalf - 0.35f, WinHalf + 0.35f })
            {
                Solid("Pillar", ring, new Vector3(sx, H / 2, Z1 + 0.35f), new Vector3(0.5f, H, 0.5f), art.trim);
                Box("PillarGlow", ring, new Vector3(sx, H / 2, Z1 + 0.62f), new Vector3(0.1f, H - 0.4f, 0.03f), lm.asmGlow, shadows: false);
            }
            Label(ring, "ui.area.assembly", new Vector3(0, 5.3f, Z1 + 0.5f), "sign", 18f);

            // ---- 南側：兩座對稱的展覽台（衛星 + 火箭），窗景從中間透過來 ----
            Exhibit(ring, new Vector3(-3.4f, 0, -9.0f), 1, lm, art, fm);
            Exhibit(ring, new Vector3(3.4f, 0, -9.0f), 2, lm, art, fm);

            // ---- 西牆（南段）：零件架、牆上螢幕 ----
            for (int i = 0; i < 2; i++) LobbyShelf(ring, new Vector3(W + 0.2f, 0, -7.6f - i * 2.9f), lm, art, fm);

            // ---- 東牆（南段）：熱真空艙 ----
            var tv = BuildUtil.Group("ThermalVacChamber", ring);
            tv.position = new Vector3(E - 1.9f, 0, -10.2f);
            var cham = BuildUtil.Prim(PrimitiveType.Cylinder, "Chamber", tv, new Vector3(0, 1.3f, 0), new Vector3(1.7f, 1.7f, 1.7f), lm.steel, collider: true);
            cham.transform.localRotation = Quaternion.Euler(0, 0, 90f);
            cham.transform.localScale = new Vector3(1.7f, 1.7f, 1.7f);
            foreach (float k in new[] { -1.3f, 0f, 1.3f })
                Box("Ring", tv, new Vector3(k, 1.3f, 0), new Vector3(0.12f, 1.8f, 1.8f), art.trim, shadows: false);
            Box("Hatch", tv, new Vector3(-1.75f, 1.3f, 0), new Vector3(0.1f, 1.2f, 1.2f), lm.dark, shadows: false);
            Box("HatchGlow", tv, new Vector3(-1.82f, 1.3f, 0), new Vector3(0.02f, 0.8f, 0.8f), lm.asmGlow, shadows: false);
            foreach (float zz in new[] { -0.6f, 0.6f })
                Solid("Cradle", tv, new Vector3(0, 0.25f, zz), new Vector3(3.0f, 0.5f, 0.3f), lm.dark, true);

            // ---- 兩側牆上的看板（南段） ----
            foreach (float sx in new[] { W + 0.06f, E - 0.06f })
                Quad("WallScreen", ring, new Vector3(sx, 3.7f, sx < 0 ? -9.2f : -9.6f), Quaternion.Euler(0, sx < 0 ? -90f : 90f, 0), new Vector2(2.6f, 1.5f), art.consoleScreens[sx < 0 ? 0 : 2]);
        }

        /// <summary>盆栽：圓盆、土、一根主莖、一圈向外彎的長葉片（像室內的龍舌蘭 / 虎尾蘭），不是一團球。</summary>
        static void Plant(Transform parent, Vector3 pos, Material potMat, Material leaf)
        {
            var g = BuildUtil.Group("Plant", parent);
            g.position = pos;
            BuildUtil.Prim(PrimitiveType.Cylinder, "Pot", g, new Vector3(0, 0.3f, 0), new Vector3(0.62f, 0.3f, 0.62f), potMat, collider: true);
            BuildUtil.Prim(PrimitiveType.Cylinder, "PotRim", g, new Vector3(0, 0.62f, 0), new Vector3(0.7f, 0.03f, 0.7f), potMat);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Soil", g, new Vector3(0, 0.6f, 0), new Vector3(0.56f, 0.02f, 0.56f), BuildUtil.Lit("M_Soil", new Color(0.07f, 0.04f, 0.03f), 0f, 0.2f));
            for (int i = 0; i < 12; i++)
            {
                float yaw = i * 30f + (i % 2) * 12f;
                float tilt = 8f + (i % 4) * 11f;
                float len = 0.9f + (i % 3) * 0.35f;
                var pivot = BuildUtil.Group("Leaf" + i, g);
                pivot.localPosition = new Vector3(0, 0.62f, 0);
                pivot.localRotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(tilt, 0, 0);
                BuildUtil.Prim(PrimitiveType.Sphere, "Blade", pivot, new Vector3(0, len / 2f, 0), new Vector3(0.13f, len, 0.045f), leaf);
            }
        }

        static void BuildControlDecor(ArtBuilder.Art art, FacMats fm, Transform ctrl)
        {
            var lm = MakeLobbyMats(art);
            var g = BuildUtil.Group("ControlDecor", ctrl);
            var leather = BuildUtil.Lit("M_CtlSofa", new Color(0.16f, 0.08f, 0.05f), 0.1f, 0.5f);
            var leaf = BuildUtil.Lit("M_CtlPlant", new Color(0.1f, 0.35f, 0.15f), 0f, 0.5f);
            float nz = 9f * CtrlS - 0.7f, sz = -nz;   // 北、南牆內側

            // 北牆：機櫃一排（正面朝南，一排小燈）
            for (int i = 0; i < 6; i++)
            {
                float x = 13.8f + i * 1.3f;
                Solid("Rack", g, new Vector3(x, 1.6f, nz), new Vector3(1.1f, 3.2f, 0.9f), lm.dark, true);
                for (int k = 0; k < 7; k++)
                    Box("RackLed", g, new Vector3(x, 0.45f + k * 0.42f, nz - 0.46f), new Vector3(0.8f, 0.04f, 0.02f), (i + k) % 3 == 0 ? lm.goGlow : lm.ctlGlow, shadows: false);
            }
            // 北牆再往東：資料櫃（兩台）+ 盆栽
            for (int i = 0; i < 2; i++)
            {
                float x = 21.5f + i * 1.5f;
                Solid("Archive", g, new Vector3(x, 1.0f, nz + 0.1f), new Vector3(1.3f, 2.0f, 0.7f), art.consoleBody, true);
                Box("ArchiveStrip", g, new Vector3(x, 1.6f, nz - 0.26f), new Vector3(1.1f, 0.05f, 0.02f), lm.ctlGlow, shadows: false);
            }
            foreach (var pp in new[] { new Vector3(12.9f, 0, nz + 0.2f), new Vector3(12.9f, 0, sz - 0.2f) })
                Plant(g, pp, lm.steel, leaf);

            // 南牆：休息區（兩張沙發 + 一張茶几，茶几上有全像小地球）
            var lounge = BuildUtil.Group("Lounge", g);
            lounge.position = new Vector3(16.2f, 0, sz + 0.2f);
            Solid("Table", lounge, new Vector3(0, 0.25f, 1.6f), new Vector3(1.8f, 0.5f, 1.0f), art.consoleBody, true);
            Box("TableTop", lounge, new Vector3(0, 0.52f, 1.6f), new Vector3(1.9f, 0.05f, 1.1f), art.trim);
            Box("TableGlow", lounge, new Vector3(0, 0.56f, 1.6f), new Vector3(1.5f, 0.015f, 0.04f), lm.ctlGlow, shadows: false);
            var mini = BuildUtil.Prim(PrimitiveType.Sphere, "MiniGlobe", lounge, new Vector3(0, 0.95f, 1.6f), new Vector3(0.5f, 0.5f, 0.5f), art.earth);
            mini.AddComponent<Spinner>().degreesPerSecond = 18f;
            Solid("SofaA", lounge, new Vector3(0, 0.25f, 0), new Vector3(2.6f, 0.5f, 0.9f), leather, true);
            Box("SofaABack", lounge, new Vector3(0, 0.8f, -0.4f), new Vector3(2.6f, 0.6f, 0.2f), leather, shadows: false);
            Solid("SofaB", lounge, new Vector3(-2.2f, 0.25f, 1.6f), new Vector3(0.9f, 0.5f, 2.2f), leather, true);
            Box("SofaBBack", lounge, new Vector3(-2.6f, 0.8f, 1.6f), new Vector3(0.2f, 0.6f, 2.2f), leather, shadows: false);
            BuildUtil.Light("LoungeLight", LightType.Point, new Color(1f, 0.7f, 0.4f), 2f, new Vector3(16.2f, 2.8f, sz + 1.6f), Quaternion.identity, 8f, LightShadows.None, g);
            for (int i = 0; i < 3; i++)
            {
                float x = 20.6f + i * 1.3f;
                Solid("Locker", g, new Vector3(x, 1.0f, sz - 0.1f), new Vector3(1.15f, 2.0f, 0.7f), art.consoleBody, true);
                Box("LockerLed", g, new Vector3(x, 1.7f, sz + 0.26f), new Vector3(0.9f, 0.05f, 0.02f), i == 1 ? lm.goGlow : lm.ctlGlow, shadows: false);
            }

            // 隔牆（朝中控室這一面）：一排任務徽章，門上方的招牌之外的空白牆都掛滿
            for (int n = 0; n < 8; n++)
            {
                int v = n % PatchPalette.Length;
                float z = (n < 4 ? 1 : -1) * (3.4f + (n % 4) * 2.2f);
                Quad("Patch" + n, g, new Vector3(12.06f, 5.2f + (n % 2) * 0.35f, z), Quaternion.Euler(0, -90f, 0), new Vector2(1.3f, 1.3f), PatchMaterial(v, PatchPalette[v]));
            }
            Box("DividerGlowN", g, new Vector3(12.04f, 4.2f, 7f), new Vector3(0.03f, 0.04f, 8.6f), lm.ctlGlow, shadows: false);
            Box("DividerGlowS", g, new Vector3(12.04f, 4.2f, -7f), new Vector3(0.03f, 0.04f, 8.6f), lm.ctlGlow, shadows: false);

            // 北牆上的兩面旗：站名
            Banner(g, "BannerN1", new Vector3(26f, 5.0f, 9f * CtrlS - 0.06f), CtlColor, null, lm.ctlGlow, 0f);
            Banner(g, "BannerN2", new Vector3(31.5f, 5.0f, 9f * CtrlS - 0.06f), CtlColor, null, lm.ctlGlow, 0f);

            // 以上座標都是以 CtrlX0 = 12 寫的，整組平移到實際的中控室位置
            g.position += new Vector3(CtrlX0 - 12f, 0, 0);
        }

        const string ControlRoomModel = "Assets/GoNoGo/Art/Models/Room/FacilityControlRoom.glb";
        /// <summary>中控室模型的原點（世界座標）：Blender 的 (x, y, z) = 這裡 + Unity (x, z, y)。</summary>
        static readonly Vector3 CtrlOrigin = new Vector3(CtrlX0 + 11f * CtrlS, 0, 0);
        /// <summary>舊座標（放大前的中控室世界座標）→ 放大後。</summary>
        static Vector3 CP(float x, float y, float z) => new Vector3(CtrlX0 + (x - 12f) * CtrlS, y * CtrlS, z * CtrlS);
        /// <summary>隔牆的 x（中控室和組裝區之間）。</summary>
        const float DivX = BayX1 + 0.5f;

        /// <summary>
        /// 中控室：房間、弧形影像牆、一整座弧形中控台都來自 Blender 模型
        /// （tools/room/build_facility_control_room.py → Art/Models/Room/FacilityControlRoom.glb）。
        /// 這裡換上專案材質、加碰撞體，並依物件名稱掛元件：
        /// Screen_&lt;panel&gt; = 中控台上的一台螢幕（ConsoleTarget，準星對到按 E 打開該面板），
        /// Wall_Main = 地球大螢幕，Wall_&lt;id&gt; = 影像牆上的即時資訊（WorldScreen）。
        /// </summary>
        static void BuildControlRoom(ArtBuilder.Art art, FacMats fm, Transform ctrl)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ControlRoomModel);
            if (prefab == null) throw new System.Exception("找不到中控室模型 " + ControlRoomModel + "（先跑 tools/room/build_facility_control_room.py）");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "ControlRoomModel";
            model.transform.SetParent(ctrl, false);
            model.transform.localPosition = CtrlOrigin;
            model.transform.localScale = Vector3.one * CtrlS;
            // glTFast 匯入後 Blender +Y 變成 Unity -Z、X 也翻轉：轉 180° 讓 Blender (x, y, z) = Unity (x, z, y)
            model.transform.localRotation = Quaternion.Euler(0, 180f, 0);

            // 三座中控台各有一個顏色：設計組青、飛行總監琥珀、營運組綠
            var amber = BuildUtil.Unlit("M_AccentAmber", new Color(2.6f, 1.25f, 0.3f));
            var green = BuildUtil.Unlit("M_AccentGreen", new Color(0.35f, 2.4f, 1.0f));
            var glowAmber = BuildUtil.Lit("M_ConsoleGlowAmber", new Color(0.3f, 0.2f, 0.05f), 0f, 0.6f, emission: new Color(0.95f, 0.48f, 0.1f));
            var glowGreen = BuildUtil.Lit("M_ConsoleGlowGreen", new Color(0.05f, 0.25f, 0.12f), 0f, 0.6f, emission: new Color(0.15f, 0.85f, 0.35f));
            var panelMat = BuildUtil.Lit("M_ConsolePanel", new Color(0.045f, 0.05f, 0.06f), 0.2f, 0.78f);
            char GroupOf(string station) => station == "ops_desk" ? 'F'
                : station.StartsWith("manual-") ? 'B'
                : station is "phone" or "notes" or "manual" or "mission" or "dashboard" ? 'D' : 'O';
            Material ScreenFor(char grp, int i) => grp switch
            {
                'D' => art.consoleScreens[i % 2 == 0 ? 0 : 3],
                'F' => art.consoleScreens[2],
                'O' => art.consoleScreens[i % 3 == 2 ? 3 : 1],
                _ => art.consoleScreens[i % art.consoleScreens.Length],
            };
            Material Slot(string mn) =>
                mn.StartsWith("M_GlowRed") ? fm.redGlow
                : mn.StartsWith("M_GlowAmber") ? amber
                : mn.StartsWith("M_GlowGreen") ? green
                : mn.StartsWith("M_Glow") ? art.accentGlow
                : mn.StartsWith("M_Trim") ? art.trim
                : mn.StartsWith("M_Console") ? art.consoleBody
                : mn.StartsWith("M_Panel") ? panelMat
                : mn.StartsWith("M_Paper") ? fm.paper
                : mn.StartsWith("M_Floor") ? art.floorMatte
                : art.wallDark;

            var glows = new Dictionary<string, Renderer>();
            var anchors = new Dictionary<string, Transform>();
            var screens = new List<Renderer>();
            var tops = new Dictionary<string, Renderer>();
            int deco = 0;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Anchor_")) anchors[t.name.Substring(7)] = t;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                Material m = null;
                if (n.StartsWith("Screen_"))
                {
                    string key = n.Substring(7);
                    m = ScreenFor(GroupOf(key), screens.Count);
                    screens.Add(r);
                }
                else if (n == "Wall_Main") m = fm.screenRt;
                else if (n.StartsWith("Wall_"))
                {
                    m = fm.screenOff;
                    var ws = r.gameObject.AddComponent<WorldScreen>();
                    // 儀表板搬到組裝區：中控室影像牆原本的儀表板改顯示任務目標與預算
                    ws.id = n.Substring(5) == "dashboard" ? "goals" : n.Substring(5);
                    ws.resolution = new Vector2Int(1280, 690);
                    ws.target = r;
                }
                else if (n.StartsWith("Deco_"))
                {
                    // Deco_<組>_…：組 = D 設計、F 總監、O 營運、B 後排、W 影像牆
                    m = ScreenFor(n.Length > 5 ? n[5] : 'W', deco++);
                    int ti = n.IndexOf("_Top_");
                    if (ti >= 0) tops[n.Substring(ti + 5)] = r;
                }
                else if (n.StartsWith("Glow_"))
                {
                    string key = n.Substring(5);
                    char g = GroupOf(key);
                    m = g == 'F' ? glowAmber : g == 'O' ? glowGreen : fm.glow;
                    glows[key] = r;
                }
                var mats = r.sharedMaterials;
                bool shadows = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = mats[i] != null ? mats[i].name : "";
                    shadows |= mn.StartsWith("M_Trim") || mn.StartsWith("M_Console");
                    mats[i] = m ?? Slot(mn);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = m == null && shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                // 走路會撞到的：地板、牆、影像牆、三座中控台與端板、總監平台、後排桌（發射台用按鈕上的膠囊碰撞體）
                bool collide = n == "Floor" || (n.StartsWith("Wall") && !n.StartsWith("Wall_"))
                               || n.StartsWith("Pod_") || n == "Podium_F" || n.StartsWith("Fin_") || n.StartsWith("BackPod");
                if (collide) r.gameObject.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            }

            // 中控台的每台螢幕：一個席位
            foreach (var r in screens)
            {
                string key = r.gameObject.name.Substring(7);
                string panel = key.Replace('-', ':');
                if (panel == "dashboard") panel = "goals"; // 儀表板搬到組裝區，這一席改看任務目標與預算
                var ct = r.gameObject.AddComponent<ConsoleTarget>();
                r.gameObject.AddComponent<BoxCollider>();
                ct.panel = panel;
                if (glows.TryGetValue(key, out var g)) ct.glow = g;
                if (panel.StartsWith("ops:"))
                {
                    ct.titleKey = "ops.console." + panel.Substring(4);
                    ct.bodyKey = "ui.fac.ops_console_body";
                    ct.actionKey = "ui.hint.operate";
                    ct.phases = "ops";
                }
                else if (panel.StartsWith("manual:"))
                {
                    // 後排：工程手冊的一章
                    string ch = panel.Substring(7);
                    ct.titleKey = "ui.manual.ch." + ch;
                    ct.bodyKey = "ui.manual.ch." + ch + "_body";
                    ct.actionKey = "ui.hint.read";
                }
                else if (panel == "manual")
                {
                    // 前排設計組的手冊席：目錄與零件代號速查（各章在後排）
                    ct.titleKey = "ui.fac.manual_index";
                    ct.bodyKey = "ui.fac.manual_index_body";
                    ct.actionKey = "ui.hint.read";
                }
                else
                {
                    ct.titleKey = "ui.fac." + panel;
                    ct.bodyKey = "ui.fac." + panel + "_body";
                    if (panel == "mission") ct.phases = "design,briefing";
                    else if (panel == "ops_desk") ct.phases = "ops,report";
                    else if (panel == "failures") ct.actionKey = "ui.hint.read";
                }
                // 標籤放在這席最上面那台螢幕的上方
                var b = tops.TryGetValue(key, out var upper) ? upper.bounds : r.bounds;
                var top = new Vector3(r.bounds.center.x, b.max.y + 0.14f, r.bounds.center.z);
                Label(ctrl, ct.titleKey, top - ctrl.position, "tag", 7f).name = "Label_" + key;
            }

            // 電話：電話席的桌面左邊
            if (anchors.TryGetValue("phone", out var pa))
            {
                var phone = Facing(ctrl, "Phone", pa, ScreenOf(screens, "phone"));
                Box("Base", phone, new Vector3(-0.3f, 0.05f, 0.05f), new Vector3(0.3f, 0.1f, 0.22f), fm.red);
                Box("Handset", phone, new Vector3(-0.3f, 0.13f, 0.05f), new Vector3(0.36f, 0.06f, 0.08f), fm.red);
            }

            // 發射鈕：中控台前方正中央的獨立發射台，每一席都面向它
            if (anchors.TryGetValue("launch", out var la))
            {
                var launch = BuildUtil.Group("LaunchButton", ctrl);
                launch.position = la.position;
                var cyl = HiCylinder();
                MeshObj("HazardRing", launch, Annulus("Ring_0.36_0.46", 0.36f, 0.46f), fm.hazard, new Vector3(0, 0.003f, 0), 1f);
                MeshObj("Guard", launch, Annulus("Ring_0.2_0.27", 0.2f, 0.27f), fm.hazard, new Vector3(0, 0.004f, 0), 1f);
                var btn = MeshObj("Button", launch, cyl, fm.red, new Vector3(0, 0.04f, 0), 1f);
                btn.transform.localScale = new Vector3(0.34f, 0.04f, 0.34f);
                // 一個膠囊碰撞體包住整座台：擋人，也讓準星對到台子任何地方都算
                var cc = launch.gameObject.AddComponent<CapsuleCollider>();
                cc.center = new Vector3(0, -0.45f, 0);
                cc.radius = 0.62f;
                cc.height = 1.2f;
                var lt = launch.gameObject.AddComponent<ConsoleTarget>();
                lt.panel = "launch";
                lt.titleKey = "ui.fac.launch";
                lt.bodyKey = "ui.fac.launch_body";
                lt.actionKey = "ui.hint.launch";
                lt.phases = "design";
                lt.glow = btn.GetComponent<Renderer>();
                Label(launch, "ui.fac.launch", new Vector3(0, 0.75f, 0), "sign", 16f);
                BuildUtil.Light("LaunchGlow", LightType.Point, new Color(1f, 0.25f, 0.15f), 2.5f, la.position + Vector3.up * 0.5f, Quaternion.identity, 3.5f, LightShadows.None, ctrl);
                var spot = BuildUtil.Light("LaunchSpot", LightType.Spot, new Color(1f, 0.82f, 0.75f), 30f, new Vector3(la.position.x, 6.6f * CtrlS, la.position.z), Quaternion.Euler(90, 0, 0), 9f * CtrlS, LightShadows.None, ctrl);
                spot.spotAngle = 28f;
                spot.innerSpotAngle = 14f;
            }

            // 燈：中控台上方暖色主光、後排補光、影像牆的藍光溢出
            var key0 = BuildUtil.Light("ConsoleKey", LightType.Spot, new Color(1f, 0.88f, 0.72f), 42f * CtrlS * CtrlS, CP(21.5f, 6.6f, 0), Quaternion.Euler(90, 0, 0), 12f * CtrlS, LightShadows.None, ctrl);
            key0.spotAngle = 120f; key0.innerSpotAngle = 70f;
            var key1 = BuildUtil.Light("BackFill", LightType.Spot, new Color(0.85f, 0.9f, 1f), 18f * CtrlS * CtrlS, CP(15.5f, 6.6f, 0), Quaternion.Euler(90, 0, 0), 10f * CtrlS, LightShadows.None, ctrl);
            key1.spotAngle = 120f; key1.innerSpotAngle = 60f;
            BuildUtil.Light("ScreenSpill", LightType.Point, new Color(0.35f, 0.6f, 1f), 14f * CtrlS * CtrlS, CP(29f, 3.4f, 0), Quaternion.identity, 16f * CtrlS, LightShadows.None, ctrl);
        }

        static Renderer ScreenOf(List<Renderer> screens, string key) => screens.Find(s => s.gameObject.name == "Screen_" + key);

        /// <summary>放在錨點上、局部 +Z 朝向螢幕（操作員面對的方向）的空物件。</summary>
        static Transform Facing(Transform parent, string name, Transform anchor, Renderer screen)
        {
            var t = BuildUtil.Group(name, parent);
            t.position = anchor.position;
            var fwd = screen != null ? screen.bounds.center - anchor.position : Vector3.right;
            fwd.y = 0;
            t.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            return t;
        }

        // ---------------- 大廳（任務整備室） ----------------
        //
        // 北邊是升高的指揮台：一圈弧形的大控制台（按 E 看房間碼、設定、兩組名單），上方浮著慢慢轉的全像地球，後面是整面牆的任務螢幕。
        // 西邊是組裝區的隊伍區（潔淨室：白、青、警示黃，吊架、零件架、衛星展示台、機械手臂），
        // 東邊是中控室的（深色、琥珀色，監控桌、機櫃、天線）。各有一顆「加入 / 準備」的按鈕和一面名單牆。
        // 地板上沒有圈圈：位置靠兩邊的拱門、地毯和燈光區分。

        static readonly Color AsmColor = new Color(0.22f, 0.78f, 0.96f);
        static readonly Color CtlColor = new Color(1f, 0.66f, 0.15f);

        const float LX = 13f, LZ0 = 34f, LZ1 = 58f, LH = 6.5f;

        sealed class LobbyMats
        {
            public Material asmGlow, ctlGlow, goGlow, asmFloor, ctlFloor, runway, solar, asmBtn, ctlBtn, steel, dark, lamp;
        }

        static LobbyMats MakeLobbyMats(ArtBuilder.Art art)
        {
            return new LobbyMats
            {
                asmGlow = BuildUtil.Unlit("M_LobbyCyanGlow", new Color(0.18f, 0.8f, 1.35f)),
                ctlGlow = BuildUtil.Unlit("M_LobbyAmberGlow", new Color(1.7f, 0.42f, 0.04f)),
                goGlow = BuildUtil.Unlit("M_LobbyGreenGlow", new Color(0.3f, 1.4f, 0.5f)),
                asmFloor = BuildUtil.Lit("M_LobbyAsmFloor", new Color(0.66f, 0.74f, 0.8f), 0.15f, 0.85f),
                ctlFloor = BuildUtil.Lit("M_LobbyCtlFloor", new Color(0.07f, 0.075f, 0.09f), 0.3f, 0.8f),
                runway = BuildUtil.Lit("M_LobbyRunway", new Color(0.06f, 0.08f, 0.13f), 0.4f, 0.85f),
                solar = BuildUtil.Lit("M_LobbySolar", new Color(0.03f, 0.06f, 0.2f), 0.6f, 0.8f, emission: new Color(0.01f, 0.025f, 0.09f)),
                asmBtn = BuildUtil.Lit("M_LobbyBtnAsm", AsmColor, 0.1f, 0.6f, emission: AsmColor * 0.5f),
                ctlBtn = BuildUtil.Lit("M_LobbyBtnCtl", CtlColor, 0.1f, 0.6f, emission: CtlColor * 0.5f),
                steel = BuildUtil.Lit("M_LobbySteel", new Color(0.55f, 0.58f, 0.62f), 0.8f, 0.55f),
                dark = BuildUtil.Lit("M_LobbyDark", new Color(0.04f, 0.045f, 0.055f), 0.5f, 0.6f),
                lamp = art.lightPanel,
            };
        }

        static void BuildLobby(ArtBuilder.Art art, FacMats fm, Transform lobby, Mesh sphere)
        {
            var lm = MakeLobbyMats(art);
            float cz = (LZ0 + LZ1) / 2, len = LZ1 - LZ0;

            // ---- 殼：地板、天花板、牆 ----
            Solid("Floor", lobby, new Vector3(0, -0.1f, cz), new Vector3(LX * 2, 0.2f, len), art.floor);
            Solid("Ceiling", lobby, new Vector3(0, LH + 0.1f, cz), new Vector3(LX * 2, 0.2f, len), art.wallPanel);
            Solid("WallS", lobby, new Vector3(0, LH / 2, LZ0 - 0.15f), new Vector3(LX * 2, LH, 0.3f), art.wallPanel);
            Solid("WallW", lobby, new Vector3(-LX - 0.15f, LH / 2, cz), new Vector3(0.3f, LH, len), art.wallDark);
            Solid("WallE", lobby, new Vector3(LX + 0.15f, LH / 2, cz), new Vector3(0.3f, LH, len), art.wallDark);
            // 北牆：中間整塊放大螢幕，兩側是落地大窗，窗外是地球和月亮
            Solid("WallN_Mid", lobby, new Vector3(0, LH / 2, LZ1 + 0.15f), new Vector3(12f, LH, 0.3f), art.wallDark);
            foreach (float sx in new[] { -1f, 1f })
            {
                float w = LX - 6f, cx = sx * (6f + w / 2);
                Solid("WallN_Low", lobby, new Vector3(cx, 0.55f, LZ1 + 0.15f), new Vector3(w, 1.1f, 0.3f), art.wallPanel);
                Solid("WallN_High", lobby, new Vector3(cx, (4.9f + LH) / 2, LZ1 + 0.15f), new Vector3(w, LH - 4.9f, 0.3f), art.wallPanel);
                var glass = Solid("WindowGlass", lobby, new Vector3(cx, 3f, LZ1 + 0.15f), new Vector3(w, 3.8f, 0.1f), art.trim);
                Object.DestroyImmediate(glass.GetComponent<MeshRenderer>());
                Object.DestroyImmediate(glass.GetComponent<MeshFilter>());
                for (int k = 0; k <= 3; k++)
                    Box("Mullion", lobby, new Vector3(sx * (6f + k * w / 3), 3f, LZ1 - 0.02f), new Vector3(0.14f, 3.8f, 0.16f), art.trim, shadows: false);
            }
            LaunchComplexView(lobby, new Vector3(0, 0, LZ1 + 0.3f), 0f, art);

            // ---- 天花板：橫樑、燈條、燈板 ----
            for (int i = 0; i < 5; i++)
            {
                float z = LZ0 + 3f + i * 5.6f;
                Box("Beam", lobby, new Vector3(0, LH - 0.2f, z), new Vector3(LX * 2, 0.4f, 0.55f), art.trim, shadows: false);
            }
            for (int x = -1; x <= 1; x++)
                for (int z = 0; z < 3; z++)
                    Quad("LightPanel", lobby, new Vector3(x * 8f, LH - 0.41f, LZ0 + 6.4f + z * 7.4f), Quaternion.Euler(-90, 0, 0), new Vector2(2.2f, 0.8f), art.lightPanel);
            BuildUtil.Light("LobbyFill", LightType.Point, new Color(0.9f, 0.94f, 1f), 2f, new Vector3(0, 4.8f, cz), Quaternion.identity, 30f, LightShadows.None, lobby);

            // ---- 牆面與地面導引 ----
            BuildLobbyEntrance(art, lm, lobby);
            Box("Runway", lobby, new Vector3(0, 0.012f, 41.5f), new Vector3(4.4f, 0.024f, 15f), lm.runway, shadows: false);
            Box("RunwayEdgeW", lobby, new Vector3(-2.2f, 0.03f, 41.5f), new Vector3(0.08f, 0.02f, 15f), lm.asmGlow, shadows: false);
            Box("RunwayEdgeE", lobby, new Vector3(2.2f, 0.03f, 41.5f), new Vector3(0.08f, 0.02f, 15f), lm.ctlGlow, shadows: false);
            foreach (float sx in new[] { -1f, 1f })
            {
                // 兩邊牆的下半截護牆與直立燈柱
                Box("Wainscot", lobby, new Vector3(sx * (LX - 0.1f), 0.5f, cz), new Vector3(0.2f, 1f, len - 1f), art.trim, shadows: false);
            }

            BuildLobbyCommand(art, fm, lm, lobby, sphere);
            BuildAssemblySide(art, fm, lm, lobby);
            BuildControlSide(art, fm, lm, lobby);
        }

        /// <summary>入口那面（南牆）：深色直立牆板、兩面隊伍旗、任務名稱。</summary>
        static void BuildLobbyEntrance(ArtBuilder.Art art, LobbyMats lm, Transform lobby)
        {
            for (int i = -11; i <= 11; i++)
            {
                if (Mathf.Abs(i) >= 3 && Mathf.Abs(i) <= 5) continue;
                Box("Slat", lobby, new Vector3(i * 1.1f, 3.2f, LZ0 + 0.03f), new Vector3(0.5f, 5.6f, 0.08f), art.wallDark, shadows: false);
            }
            Banner(lobby, "BannerA", new Vector3(-4.4f, 3.2f, LZ0 + 0.05f), AsmColor, null, lm.asmGlow);
            Banner(lobby, "BannerC", new Vector3(4.4f, 3.2f, LZ0 + 0.05f), CtlColor, null, lm.ctlGlow);
            Label(lobby, "ui.lobby.window", new Vector3(0, 5.6f, LZ0 + 0.3f), "sign", 16f);
        }

        /// <summary>北邊的指揮台：弧形大控制台 + 全像地球 + 整面牆的任務螢幕。</summary>
        static void BuildLobbyCommand(ArtBuilder.Art art, FacMats fm, LobbyMats lm, Transform lobby, Mesh sphere)
        {
            // 升高的平台和階梯
            Solid("DeckStep1", lobby, new Vector3(0, 0.08f, 47.9f), new Vector3(12f, 0.16f, 0.9f), art.trim);
            Solid("DeckStep2", lobby, new Vector3(0, 0.2f, 48.7f), new Vector3(12f, 0.4f, 0.7f), art.trim);
            Solid("Deck", lobby, new Vector3(0, 0.225f, 53.7f), new Vector3(12f, 0.45f, 9.7f), art.consoleBody);
            Box("DeckEdgeGlow", lobby, new Vector3(0, 0.47f, 49.05f), new Vector3(11.8f, 0.03f, 0.08f), lm.goGlow, shadows: false);
            Box("DeckInlayL", lobby, new Vector3(-4.2f, 0.47f, 54f), new Vector3(0.06f, 0.02f, 8.4f), lm.asmGlow, shadows: false);
            Box("DeckInlayR", lobby, new Vector3(4.2f, 0.47f, 54f), new Vector3(0.06f, 0.02f, 8.4f), lm.ctlGlow, shadows: false);

            // 弧形控制台（操作員站在南側，面向北）
            var cons = BuildUtil.Group("LobbyConsole", lobby);
            cons.position = Vector3.zero;
            var center = new Vector3(0, 0.45f, 55.8f);
            const float R = 3.6f;
            Renderer midGlow = null;
            for (int i = 0; i < 7; i++)
            {
                float ang = (-60f + i * 20f) * Mathf.Deg2Rad;
                var pos = center + new Vector3(Mathf.Sin(ang) * R, 0, -Mathf.Cos(ang) * R);
                var seg = BuildUtil.Group("Seg" + i, cons);
                seg.position = pos;
                seg.rotation = Quaternion.LookRotation(center - pos, Vector3.up);
                Solid("Body", seg, new Vector3(0, 0.5f, 0.1f), new Vector3(1.55f, 1f, 0.7f), art.consoleBody, true);
                Box("Desk", seg, new Vector3(0, 1.02f, 0), new Vector3(1.7f, 0.05f, 0.9f), art.trim);
                var gl = Box("Glow", seg, new Vector3(0, 1.05f, -0.4f), new Vector3(1.7f, 0.03f, 0.04f), i % 2 == 0 ? lm.asmGlow : lm.ctlGlow, shadows: false);
                if (i == 3) midGlow = gl.GetComponent<Renderer>();
                Box("Riser", seg, new Vector3(0, 1.4f, 0.32f), new Vector3(1.55f, 0.7f, 0.08f), art.consoleBody);
                Quad("Screen", seg, new Vector3(0, 1.42f, 0.275f), Quaternion.Euler(12f, 0, 0), new Vector2(1.4f, 0.6f), art.consoleScreens[i % art.consoleScreens.Length]);
            }
            var ct = cons.gameObject.AddComponent<ConsoleTarget>();
            ct.panel = "lobby";
            ct.titleKey = "ui.fac.lobby";
            ct.bodyKey = "ui.fac.lobby_body";
            ct.phases = "lobby";
            ct.glow = midGlow;
            Label(cons, "ui.fac.lobby", new Vector3(0, 2.4f, 52.4f), "tag", 12f);

            // 全像地球：控制台中央的投影座 → 一道光 → 慢慢自轉的地球和兩圈軌道
            var holo = BuildUtil.Group("Hologram", lobby);
            holo.position = new Vector3(0, 0, 55.6f);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Projector", holo, new Vector3(0, 0.5f, 0), new Vector3(0.9f, 0.04f, 0.9f), art.trim);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Beam", holo, new Vector3(0, 1.6f, 0), new Vector3(0.05f, 1.1f, 0.05f), lm.asmGlow);
            var earth = Planet("HoloEarth", holo, sphere, art.earth, art.atmosphere, new Vector3(0, 3.3f, 0), 1.9f, out _);
            var sp = earth.AddComponent<Spinner>();
            sp.degreesPerSecond = 14f;
            var r1 = MeshObj("OrbitRing1", holo, Annulus("LobbyHolo_1.35_1.39", 1.35f, 1.39f), lm.asmGlow, new Vector3(0, 3.3f, 0), 1f);
            r1.transform.localRotation = Quaternion.Euler(28f, 0, 12f);
            r1.AddComponent<Spinner>().degreesPerSecond = 25f;
            var r2 = MeshObj("OrbitRing2", holo, Annulus("LobbyHolo_1.6_1.63", 1.6f, 1.63f), lm.ctlGlow, new Vector3(0, 3.3f, 0), 1f);
            r2.transform.localRotation = Quaternion.Euler(-35f, 0, -20f);
            r2.AddComponent<Spinner>().degreesPerSecond = -18f;
            BuildUtil.Light("HoloLight", LightType.Point, new Color(0.5f, 0.85f, 1f), 3.5f, new Vector3(0, 3.3f, 55.0f), Quaternion.identity, 9f, LightShadows.None, lobby);

            // 後牆：任務大螢幕與兩側光柱
            WallScreen(art, fm, lobby, "LobbyScreen", "lobby", new Vector3(0, 3.9f, LZ1 - 0.08f), 0f, new Vector2(10f, 4.2f), new Vector2Int(1280, 540));
            
            foreach (float sx in new[] { -1f, 1f })
                Box("ScreenTower", lobby, new Vector3(sx * 5.8f, 3.2f, LZ1 - 0.15f), new Vector3(0.22f, 6.2f, 0.22f), sx < 0 ? lm.asmGlow : lm.ctlGlow, shadows: false);
        }

        /// <summary>隊伍區共用的：地毯、邊線、兩根柱子 + 橫樑的拱門、隊名看板、名單牆、區域燈。</summary>
        static void TeamZone(ArtBuilder.Art art, FacMats fm, Transform lobby, float side, string tag, Material glow, Material floorMat,
            Color col, string screenId, string nameKey, Material edge)
        {
            const float ZA = 37f, ZB = 49f, ArchX = 5.8f;
            float zc = (ZA + ZB) / 2, mid = side * (ArchX + LX) / 2, w = LX - ArchX;
            var g = BuildUtil.Group("Team_" + tag, lobby);
            Box("Carpet", g, new Vector3(mid, 0.015f, zc), new Vector3(w, 0.03f, ZB - ZA), floorMat, shadows: false);
            Box("EdgeIn", g, new Vector3(side * ArchX, 0.035f, zc), new Vector3(0.1f, 0.02f, ZB - ZA), glow, shadows: false);
            Box("EdgeS", g, new Vector3(mid, 0.035f, ZA), new Vector3(w, 0.02f, 0.14f), edge, shadows: false);
            Box("EdgeN", g, new Vector3(mid, 0.035f, ZB), new Vector3(w, 0.02f, 0.14f), edge, shadows: false);
            foreach (float z in new[] { ZA, ZB })
            {
                Solid("Post", g, new Vector3(side * ArchX, 3.1f, z), new Vector3(0.4f, 6.2f, 0.4f), art.trim);
                Box("PostGlow", g, new Vector3(side * (ArchX - 0.22f), 3.1f, z), new Vector3(0.05f, 6f, 0.14f), glow, shadows: false);
            }
            Box("Lintel", g, new Vector3(side * ArchX, 5.9f, zc), new Vector3(0.5f, 0.5f, ZB - ZA + 0.5f), art.trim, shadows: false);
            
            Label(g, nameKey, new Vector3(side * (ArchX - 0.4f), 5.1f, zc), "team", 22f);
            BuildUtil.Light("Glow_" + tag, LightType.Point, col, 3f, new Vector3(side * 9.5f, 4.2f, zc), Quaternion.identity, 11f, LightShadows.None, g);
            // 名單牆：面向房間內側（西牆朝 +x = yaw -90、東牆朝 -x = yaw 90）
            WallScreen(art, fm, g, "TeamBoard_" + tag, screenId, new Vector3(side * (LX - 0.08f), 3.3f, zc), side * 90f, new Vector2(6.4f, 3.2f), new Vector2Int(1280, 640));
        }

        /// <summary>組裝區（西）：潔淨室風格 —— 淺色地板、警示黃條、吊架與工作燈、零件架、衛星展示台、機械手臂、方形按鈕。</summary>
        static void BuildAssemblySide(ArtBuilder.Art art, FacMats fm, LobbyMats lm, Transform lobby)
        {
            TeamZone(art, fm, lobby, -1f, "Asm", lm.asmGlow, lm.asmFloor, AsmColor, "team_assembly", "ui.area.assembly", fm.hazard);
            var g = BuildUtil.Group("AsmDecor", lobby);

            // 吊架：兩根橫跨的桁架 + 工作燈 + 吊纜
            foreach (float z in new[] { 40f, 46f })
            {
                Box("TrussTop", g, new Vector3(-9.4f, 5.7f, z), new Vector3(7.4f, 0.12f, 0.12f), lm.steel, shadows: false);
                Box("TrussBot", g, new Vector3(-9.4f, 5.3f, z), new Vector3(7.4f, 0.12f, 0.12f), lm.steel, shadows: false);
                for (int k = 0; k < 8; k++)
                    Box("TrussStrut", g, new Vector3(-12.8f + k * 0.95f, 5.5f, z), new Vector3(0.06f, 0.5f, 0.06f), lm.steel, Quaternion.Euler(0, 0, k % 2 == 0 ? 35f : -35f), shadows: false);
                foreach (float x in new[] { -11.5f, -9.4f, -7.3f })
                {
                    Box("Cable", g, new Vector3(x, 4.7f, z), new Vector3(0.03f, 1.2f, 0.03f), lm.dark, shadows: false);
                    Box("WorkLamp", g, new Vector3(x, 4.05f, z), new Vector3(0.7f, 0.1f, 0.35f), lm.lamp, shadows: false);
                }
            }

            // 衛星展示台：金色車身 + 藍色太陽能板，慢慢自轉
            Solid("SatPlinth", g, new Vector3(-9.4f, 0.4f, 43f), new Vector3(1.4f, 0.8f, 1.4f), art.consoleBody, true);
            Box("SatPlinthTop", g, new Vector3(-9.4f, 0.82f, 43f), new Vector3(1.5f, 0.04f, 1.5f), lm.asmGlow, shadows: false);
            var sat = BuildUtil.Group("DisplaySat", g);
            sat.position = new Vector3(-9.4f, 1.7f, 43f);
            sat.gameObject.AddComponent<Spinner>().degreesPerSecond = 16f;
            Box("Body", sat, Vector3.zero, new Vector3(0.7f, 0.7f, 0.7f), art.mli);
            Box("Dish", sat, new Vector3(0, 0.5f, 0), new Vector3(0.45f, 0.06f, 0.45f), art.mliSilver);
            foreach (float sx in new[] { -1f, 1f })
            {
                Box("Arm", sat, new Vector3(sx * 0.6f, 0, 0), new Vector3(0.5f, 0.05f, 0.05f), lm.steel, shadows: false);
                Box("Wing", sat, new Vector3(sx * 1.35f, 0, 0), new Vector3(1.0f, 0.04f, 0.65f), lm.solar, shadows: false);
            }
            BuildUtil.Light("SatLight", LightType.Point, new Color(0.7f, 0.9f, 1f), 2f, new Vector3(-9.4f, 2.6f, 43f), Quaternion.identity, 5f, LightShadows.None, g);

            // 零件架（南、北各一座，靠西牆）：木箱、藍白零件盒
            LobbyShelf(g, new Vector3(-12.3f, 0, 38.6f), lm, art, fm);
            LobbyShelf(g, new Vector3(-12.3f, 0, 47.4f), lm, art, fm);

            // 機械手臂：底座 + 兩節臂 + 夾爪，警示黃
            var arm = BuildUtil.Group("RobotArm", g);
            arm.position = new Vector3(-7.4f, 0, 47.6f);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Base", arm, new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.25f, 0.9f), lm.steel, collider: true);
            Box("Shoulder", arm, new Vector3(0, 0.65f, 0), new Vector3(0.5f, 0.5f, 0.5f), fm.hazard);
            Box("Arm1", arm, new Vector3(0.3f, 1.35f, 0), new Vector3(0.22f, 1.4f, 0.22f), fm.hazard, Quaternion.Euler(0, 0, -25f));
            Box("Elbow", arm, new Vector3(0.6f, 1.95f, 0), new Vector3(0.34f, 0.34f, 0.34f), lm.steel);
            Box("Arm2", arm, new Vector3(1.1f, 1.75f, 0), new Vector3(0.18f, 1.1f, 0.18f), fm.hazard, Quaternion.Euler(0, 0, 70f));
            Box("Claw", arm, new Vector3(1.7f, 1.55f, 0), new Vector3(0.12f, 0.3f, 0.3f), lm.dark);

            // 方形按鈕：警示黃條紋的基座 + 青色大按鈕
            var bp = BuildUtil.Group("JoinButtonAsm", g);
            bp.position = new Vector3(-6.9f, 0, 43f);
            Solid("Base", bp, new Vector3(0, 0.45f, 0), new Vector3(1f, 0.9f, 1f), art.consoleBody, true);
            Box("StripeA", bp, new Vector3(0, 0.3f, 0), new Vector3(1.04f, 0.08f, 1.04f), fm.hazard, shadows: false);
            Box("StripeB", bp, new Vector3(0, 0.6f, 0), new Vector3(1.04f, 0.08f, 1.04f), fm.hazard, shadows: false);
            Box("Frame", bp, new Vector3(0, 0.93f, 0), new Vector3(0.8f, 0.05f, 0.8f), art.trim);
            var cap = Box("Cap", bp, new Vector3(0, 1.0f, 0), new Vector3(0.62f, 0.1f, 0.62f), lm.asmBtn);
            var jb = bp.gameObject.AddComponent<LobbyButton>();
            jb.team = Station.Assembly;
            jb.teamColor = AsmColor;
            jb.cap = cap.GetComponent<Renderer>();
            Label(bp, "ui.lobby.join.assembly", new Vector3(0, 1.7f, 0), "team", 9f);
        }

        /// <summary>控制室（東）：深色風格 —— 暗地板、琥珀色光、監控桌與椅子、機櫃、天線、圓形按鈕。</summary>
        static void BuildControlSide(ArtBuilder.Art art, FacMats fm, LobbyMats lm, Transform lobby)
        {
            TeamZone(art, fm, lobby, 1f, "Ctl", lm.ctlGlow, lm.ctlFloor, CtlColor, "team_control", "ui.area.control", lm.ctlGlow);
            var g = BuildUtil.Group("CtlDecor", lobby);

            // 兩排監控桌（螢幕朝南：從入口就看得到），各配一張椅子
            for (int row = 0; row < 2; row++)
                for (int k = 0; k < 2; k++)
                {
                    float x = 8.0f + k * 2.9f, z = row == 0 ? 38.6f : 47.4f;
                    var d = BuildUtil.Group("MonitorDesk", g);
                    d.position = new Vector3(x, 0, z);
                    Solid("Desk", d, new Vector3(0, 0.38f, 0), new Vector3(2.4f, 0.76f, 0.9f), art.consoleBody, true);
                    Box("DeskTop", d, new Vector3(0, 0.78f, 0), new Vector3(2.5f, 0.05f, 1f), art.trim);
                    Box("DeskGlow", d, new Vector3(0, 0.81f, -0.46f), new Vector3(2.5f, 0.03f, 0.04f), lm.ctlGlow, shadows: false);
                    for (int m = 0; m < 3; m++)
                    {
                        Box("Stand", d, new Vector3(-0.8f + m * 0.8f, 1.0f, 0.15f), new Vector3(0.06f, 0.4f, 0.06f), lm.dark, shadows: false);
                        Quad("Monitor", d, new Vector3(-0.8f + m * 0.8f, 1.35f, 0.1f), Quaternion.Euler(0, (m - 1) * 12f, 0), new Vector2(0.72f, 0.45f), art.consoleScreens[(row * 2 + k + m) % art.consoleScreens.Length]);
                    }
                    var chair = BuildUtil.Group("Chair", d);
                    chair.localPosition = new Vector3(0, 0, 1.1f);
                    BuildUtil.Prim(PrimitiveType.Cylinder, "Post", chair, new Vector3(0, 0.25f, 0), new Vector3(0.08f, 0.25f, 0.08f), lm.dark);
                    Box("Seat", chair, new Vector3(0, 0.5f, 0), new Vector3(0.55f, 0.08f, 0.55f), lm.dark);
                    Box("Back", chair, new Vector3(0, 0.85f, 0.25f), new Vector3(0.55f, 0.7f, 0.08f), lm.dark);
                }

            // 機櫃：靠東牆，一排小燈
            foreach (float z in new[] { 37.4f, 40f, 46f, 48.6f })
            {
                Solid("Rack", g, new Vector3(12.2f, 1.5f, z), new Vector3(1.1f, 3f, 1.7f), lm.dark, true);
                for (int i = 0; i < 7; i++)
                    Box("RackLed", g, new Vector3(11.63f, 0.4f + i * 0.4f, z), new Vector3(0.03f, 0.05f, 1.3f), i % 3 == 0 ? lm.goGlow : lm.ctlGlow, shadows: false);
            }

            // 天線：桿子 + 拋物面碟 + 饋源
            var ant = BuildUtil.Group("Antenna", g);
            ant.position = new Vector3(6.9f, 0, 36.6f);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Mast", ant, new Vector3(0, 0.9f, 0), new Vector3(0.14f, 0.9f, 0.14f), lm.steel, collider: true);
            var dish = BuildUtil.Group("Dish", ant);
            dish.localPosition = new Vector3(0, 2f, 0);
            dish.localRotation = Quaternion.Euler(-50f, 0, 0);
            dish.gameObject.AddComponent<Spinner>().axis = Vector3.forward;
            BuildUtil.Prim(PrimitiveType.Sphere, "Bowl", dish, Vector3.zero, new Vector3(1.8f, 0.3f, 1.8f), art.wallPanel);
            Box("Feed", dish, new Vector3(0, 0.55f, 0), new Vector3(0.05f, 0.9f, 0.05f), lm.steel, shadows: false);
            Box("FeedTip", dish, new Vector3(0, 1.0f, 0), new Vector3(0.14f, 0.1f, 0.14f), lm.ctlGlow, shadows: false);

            // 圓形按鈕：深色基座 + 琥珀色光環 + 圓形大按鈕
            var bp = BuildUtil.Group("JoinButtonCtl", g);
            bp.position = new Vector3(6.9f, 0, 43f);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Base", bp, new Vector3(0, 0.45f, 0), new Vector3(0.95f, 0.45f, 0.95f), art.consoleBody, collider: true);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Band", bp, new Vector3(0, 0.55f, 0), new Vector3(0.99f, 0.03f, 0.99f), lm.ctlGlow);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Collar", bp, new Vector3(0, 0.93f, 0), new Vector3(0.82f, 0.03f, 0.82f), art.trim);
            var cap = BuildUtil.Prim(PrimitiveType.Cylinder, "Cap", bp, new Vector3(0, 1.0f, 0), new Vector3(0.66f, 0.07f, 0.66f), lm.ctlBtn);
            var jb = bp.gameObject.AddComponent<LobbyButton>();
            jb.team = Station.Control;
            jb.teamColor = CtlColor;
            jb.cap = cap.GetComponent<Renderer>();
            Label(bp, "ui.lobby.join.control", new Vector3(0, 1.7f, 0), "team", 9f);
        }

        /// <summary>零件架：兩根立柱、三層板、幾個零件盒。</summary>
        static Transform LobbyShelf(Transform parent, Vector3 pos, LobbyMats lm, ArtBuilder.Art art, FacMats fm)
        {
            var s = BuildUtil.Group("Shelf", parent);
            s.position = pos;
            Solid("Back", s, new Vector3(0, 1.4f, 0), new Vector3(0.1f, 2.8f, 2.2f), lm.steel, true);
            foreach (float z in new[] { -1.05f, 1.05f })
                Box("Upright", s, new Vector3(0.45f, 1.4f, z), new Vector3(0.08f, 2.8f, 0.08f), lm.steel, shadows: false);
            for (int level = 0; level < 3; level++)
            {
                float y = 0.5f + level * 0.95f;
                Box("Board", s, new Vector3(0.45f, y, 0), new Vector3(0.9f, 0.05f, 2.2f), lm.steel, shadows: false);
                for (int k = 0; k < 3; k++)
                {
                    var mat = (level + k) % 3 == 0 ? fm.hazard : (level + k) % 3 == 1 ? art.wallPanel : art.consoleBody;
                    Box("Crate", s, new Vector3(0.45f, y + 0.22f, -0.7f + k * 0.7f), new Vector3(0.6f, 0.38f, 0.55f), mat, shadows: false);
                }
            }
            return s;
        }

        static Material _bannerLogo;
        /// <summary>隊伍徽章（和組裝區西牆同一張圖，圓外透明）。</summary>
        static Material BannerLogo()
        {
            if (_bannerLogo != null) return _bannerLogo;
            var tex = BuildUtil.ConfigureTexture("Assets/GoNoGo/Art/Textures/Decals/Logo_TradeOff.png", 1024, true, TextureWrapMode.Clamp);
            var m = BuildUtil.Unlit("M_BannerLogo", new Color(0.92f, 0.92f, 0.92f), tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(m);
            return _bannerLogo = m;
        }

        /// <summary>牆上的旗子：深藍旗面、兩側隊色邊條、上緣發光、隊伍徽章；nameKey 為空就不掛字。yaw 180 = 朝北（掛在南牆），0 = 朝南（掛在北牆）。</summary>
        static void Banner(Transform parent, string name, Vector3 pos, Color col, string nameKey, Material glow, float yaw = 180f)
        {
            var mat = BuildUtil.Lit("M_Banner_" + name, new Color(0.04f, 0.07f, 0.17f), 0f, 0.3f, emission: col * 0.05f);
            var edge = BuildUtil.Lit("M_BannerEdge_" + name, col * 0.7f, 0f, 0.3f, emission: col * 0.4f);
            var g = BuildUtil.Group(name, parent);
            g.position = pos;
            float f = yaw == 0f ? -1f : 1f;
            Box("Cloth", g, Vector3.zero, new Vector3(2.2f, 4.4f, 0.05f), mat, shadows: false);
            foreach (float sx in new[] { -1f, 1f })
                Box("Edge", g, new Vector3(sx * 1.07f, 0, f * 0.01f), new Vector3(0.06f, 4.4f, 0.06f), edge, shadows: false);
            Box("Top", g, new Vector3(0, 2.25f, 0), new Vector3(2.3f, 0.1f, 0.09f), glow, shadows: false);
            Box("Bottom", g, new Vector3(0, -2.2f, 0), new Vector3(2.2f, 0.06f, 0.07f), edge, shadows: false);
            Quad("Logo", g, new Vector3(0, 0.75f, f * 0.04f), Quaternion.Euler(0, yaw, 0), new Vector2(1.9f, 1.9f), BannerLogo());
            if (!string.IsNullOrEmpty(nameKey)) Label(g, nameKey, new Vector3(0, -1.2f, f * 0.2f), "sign", 16f);
        }

        // ---------------- 營運縮景（給大螢幕） ----------------

        static OpsView BuildDiorama(ArtBuilder.Art art, Mesh sphere, Light sun, RenderTexture rt)
        {
            var root = BuildUtil.Group("OpsDiorama");
            root.position = DioramaPos;
            var sunFwd = sun.transform.forward;

            var planet = MeshObj("Planet", root, sphere, art.earth, Vector3.zero, 2f);
            var atmo = MeshObj("Atmosphere", root, sphere, art.atmosphere, Vector3.zero, 2f * 1.025f);
            var stationRoot = BuildUtil.Group("Stations", planet.transform);

            var marker = BuildUtil.Group("StationMarker", root).gameObject;
            BuildUtil.Prim(PrimitiveType.Cylinder, "Pad", marker.transform, Vector3.zero, new Vector3(0.02f, 0.002f, 0.02f), art.stationGlow);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Beam", marker.transform, new Vector3(0, 0.014f, 0), new Vector3(0.0025f, 0.014f, 0.0025f), art.stationGlow);
            marker.SetActive(false);

            var satAnchor = BuildUtil.Group("SatelliteAnchor", root);
            var orbitLine = Line("OrbitLine", art.orbitLine, 0.006f);
            orbitLine.transform.SetParent(root, false);
            var linkLine = Line("LinkLine", art.linkLine, 0.004f);
            linkLine.transform.SetParent(root, false);

            var storm = new GameObject("SolarStorm");
            storm.transform.SetParent(root, false);
            storm.transform.position = DioramaPos - sunFwd * 7f;
            storm.transform.rotation = Quaternion.LookRotation(sunFwd);
            var ps = storm.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.2f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 800;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(3.5f, 3.5f, 0.1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = storm.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = art.storm;
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.lengthScale = 8f;
            pr.velocityScale = 0.05f;
            pr.shadowCastingMode = ShadowCastingMode.Off;

            var pivot = BuildUtil.Group("CameraPivot", root);
            var pivotSpin = pivot.gameObject.AddComponent<Spin>();
            pivotSpin.axis = Vector3.up;
            pivotSpin.degreesPerSecond = 1.2f;
            var cam = BuildUtil.Camera("DioramaCamera", Vector3.zero, Quaternion.identity, 40f, 0.02f, 60f);
            cam.gameObject.tag = "Untagged";
            Object.DestroyImmediate(cam.GetComponent<AudioListener>());
            cam.transform.SetParent(pivot, false);
            cam.transform.localPosition = new Vector3(0f, 1.05f, -3.7f);
            cam.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.1f, 0) - cam.transform.localPosition);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.004f, 0.006f, 0.012f);
            cam.targetTexture = rt;
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;

            var view = new GameObject("OpsView").AddComponent<OpsView>();
            view.transform.SetParent(root, false);
            view.planet = planet.transform;
            view.planetRenderer = planet.GetComponent<Renderer>();
            view.earthMaterial = art.earth;
            view.moonMaterial = art.moon;
            view.atmosphere = atmo;
            view.satelliteAnchor = satAnchor;
            view.orbitLine = orbitLine;
            view.linkLine = linkLine;
            view.stationRoot = stationRoot;
            view.stationMarkerPrefab = marker;
            view.stormParticles = ps;
            view.sun = sun;
            view.cameraPivot = pivot;
            view.cam = cam;
            return view;
        }

        // ---------------- 標籤 ----------------

        static WorldLabel Label(Transform parent, string key, Vector3 localPos, string style, float maxDistance)
        {
            var go = new GameObject("Label_" + key);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<WorldLabel>();
            l.key = key;
            l.offset = Vector3.zero;
            l.style = style;
            l.maxDistance = maxDistance;
            return l;
        }
    }
}
