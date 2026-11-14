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
        const float RoomH = 6f;
        const float BayX0 = -9f, BayX1 = 11f, CtrlX0 = 12f, CtrlX1 = 34f, RoomZ = 9f;
        /// <summary>組裝區南北牆（中控室和隔牆還是 ±RoomZ）。</summary>
        const float BayZ = 8f;
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
            BuildControlRoom(art, fm, ctrl);
            BuildLobby(art, fm, lobby);
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
            var cam = BuildUtil.Camera("Main Camera", new Vector3(15, 1.6f, 0), Quaternion.Euler(0, 90, 0), 62f, 0.05f, 160f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.03f);

            var dir = new GameObject("FacilityDirector").AddComponent<FacilityDirector>();
            dir.assemblySpawn = Spawn("AssemblySpawn", new Vector3(0, 0.05f, -5.2f), 0);
            dir.controlSpawn = Spawn("ControlSpawn", new Vector3(15, 0.05f, 0), 90);
            dir.opsSpawn = Spawn("OpsSpawn", new Vector3(16.5f, 0.05f, 0), 90);
            dir.lobbySpawn = Spawn("LobbySpawn", new Vector3(0, 0.05f, 38f), 0);
            dir.playerCamera = cam;
            dir.stand = stand;
            dir.delivery = delivery;
            dir.areaSplitX = 11.5f;
            dir.lobbyMinZ = 30f;

            Probe(bay, new Vector3((BayX0 + BayX1) / 2, 2.5f, 0), new Vector3(BayX1 - BayX0, RoomH, BayZ * 2));
            Probe(ctrl, new Vector3(23, 3.5f, 0), new Vector3(22, 7f, 18), 0.6f);

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
            Solid("DoorSill", root, new Vector3(11.5f, -0.1f, 0), new Vector3(1f, 0.2f, 2.6f), art.trim);
            Solid("Ceiling", bay, new Vector3(bayCx, RoomH + 0.1f, 0), new Vector3(bayW + 2, 0.2f, depth + 2), art.wallPanel);

            // 組裝區：白色無塵室
            Solid("WallW", bay, new Vector3(BayX0 - 0.15f, RoomH / 2, 0), new Vector3(0.3f, RoomH, depth), art.wallPanel);
            Solid("WallN", bay, new Vector3(bayCx, RoomH / 2, BayZ + 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), art.wallPanel);
            Solid("WallS", bay, new Vector3(bayCx, RoomH / 2, -BayZ - 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), art.wallPanel);

            // 中間的隔牆（x 11…12），門洞 z -1.3…1.3、高 2.7
            const float doorHalf = 1.3f, doorH = 2.7f;
            float seg = RoomZ - doorHalf;
            Solid("DividerN", root, new Vector3(11.5f, RoomH / 2, doorHalf + seg / 2), new Vector3(1f, RoomH, seg), art.trim);
            Solid("DividerS", root, new Vector3(11.5f, RoomH / 2, -doorHalf - seg / 2), new Vector3(1f, RoomH, seg), art.trim);
            Solid("DividerTop", root, new Vector3(11.5f, (RoomH + doorH) / 2, 0), new Vector3(1f, RoomH - doorH, doorHalf * 2), art.trim);
            Box("DoorFrameGlow", root, new Vector3(10.98f, doorH + 0.04f, 0), new Vector3(0.04f, 0.06f, doorHalf * 2), art.accentGlow, shadows: false);
            Box("DoorFrameGlow2", root, new Vector3(12.02f, doorH + 0.04f, 0), new Vector3(0.04f, 0.06f, doorHalf * 2), art.accentGlow, shadows: false);

            var door = BuildUtil.Group("Door", root);
            door.position = new Vector3(11.5f, 0, 0);
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
            Label(root, "ui.area.control", new Vector3(10.6f, 3.1f, 0), "sign", 10f);
            Label(root, "ui.area.assembly", new Vector3(12.4f, 3.1f, 0), "sign", 10f);

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
            basePlate.transform.localScale = new Vector3(2.8f, 0.175f, 2.8f);
            basePlate.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            basePlate.GetComponent<Renderer>().receiveShadows = true;
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.175f, 0);
            col.size = new Vector3(2.6f, 0.35f, 2.6f);
            MeshObj("Top", go.transform, Annulus("Disk_0_1.3", 0f, 1.3f), art.consoleBody, new Vector3(0, 0.352f, 0), 1f).GetComponent<Renderer>().receiveShadows = true;
            MeshObj("Ring", go.transform, Annulus("Ring_1.3_1.4", 1.3f, 1.4f), art.accentGlow, new Vector3(0, 0.353f, 0), 1f);
            MeshObj("FloorRing", go.transform, Annulus("Ring_2.5_2.56", 2.5f, 2.56f), art.accentGlow, new Vector3(0, 0.004f, 0), 1f);
            // 安裝範圍提示：黃黑相間的地面圈
            MeshObj("InstallZone", go.transform, Annulus("Ring_2.56_2.66", 2.56f, 2.66f), fm.hazard, new Vector3(0, 0.003f, 0), 1f);

            var rigGo = new GameObject("Satellite");
            rigGo.transform.SetParent(go.transform, false);
            rigGo.transform.localPosition = new Vector3(0, 0.4f, 0);
            var rig = rigGo.AddComponent<SatelliteRig>();
            rig.fixedScale = 2f;
            rig.maxSize = 4.6f;
            rig.bottomAnchor = true;
            rig.pickable = true;

            // 安裝特效：火花 + 擴散光環
            var sparks = new GameObject("Sparks");
            sparks.transform.SetParent(go.transform, false);
            sparks.transform.localPosition = new Vector3(0, 1.4f, 0);
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
            MeshObj("Ring", ring.transform, Annulus("Ring_0.9_1.0", 0.9f, 1.0f), fm.ringFx, Vector3.zero, 1f);

            var stand = go.AddComponent<IntegrationStand>();
            stand.rig = rig;
            stand.sparks = ps;
            stand.ringFx = ring.transform;
            stand.installRange = 2.6f;
            Label(go.transform, "ui.fac.stand", new Vector3(0, 3.6f, 0), "sign", 22f);
            return stand;
        }

        // ---------------- 收貨窗口與輸送帶 ----------------

        /// <summary>輸送帶：從西北角的收貨窗口出來，沿北牆往東走到末端擋板。皮帶面高 BeltY、中心線 z = BeltZ。</summary>
        const float BeltY = 0.86f, BeltZ = 7.15f, BeltW = 0.8f, BeltX0 = BayX0 + 0.1f, BeltX1 = 6.3f;
        /// <summary>收貨窗口的外框（x BayX0…BoothX、z BoothZ0…BayZ），開口朝東。</summary>
        const float BoothX = -7.4f, BoothZ0 = 6.3f, BoothH = 2.6f, OpenH = 1.5f;

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
            conv.speed = 1.6f;
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
            Box("WainscotS", t, new Vector3(bayCx, wy / 2, -BayZ + 0.03f), new Vector3(bayW, wy, 0.06f), wainscot, shadows: false);
            Box("GlowW", t, new Vector3(BayX0 + 0.065f, wy + 0.02f, 0), new Vector3(0.02f, 0.04f, depth), art.accentGlow, shadows: false);
            Box("GlowN", t, new Vector3(bayCx, wy + 0.02f, BayZ - 0.065f), new Vector3(bayW, 0.04f, 0.02f), art.accentGlow, shadows: false);
            Box("GlowS", t, new Vector3(bayCx, wy + 0.02f, -BayZ + 0.065f), new Vector3(bayW, 0.04f, 0.02f), art.accentGlow, shadows: false);
            Box("SeamW", t, new Vector3(BayX0 + 0.02f, 3.3f, 0), new Vector3(0.04f, 0.03f, depth), art.trim, shadows: false);
            Box("SeamN", t, new Vector3(bayCx, 3.3f, BayZ - 0.02f), new Vector3(bayW, 0.03f, 0.04f), art.trim, shadows: false);
            Box("SeamS", t, new Vector3(bayCx, 3.3f, -BayZ + 0.02f), new Vector3(bayW, 0.03f, 0.04f), art.trim, shadows: false);
            for (int i = 0; i < 6; i++)
            {
                float x = -6.2f + i * 3.25f;
                Box("RibN" + i, t, new Vector3(x, RoomH / 2, BayZ - 0.1f), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
                Box("RibS" + i, t, new Vector3(x, RoomH / 2, -BayZ + 0.1f), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
            }
            foreach (float z in new[] { -5.2f, 4.4f })
                Box("RibW", t, new Vector3(BayX0 + 0.1f, RoomH / 2, z), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
            Box("UpperBandN", t, new Vector3(bayCx, RoomH - 0.5f, BayZ - 0.05f), new Vector3(bayW, 1f, 0.1f), art.trim, shadows: false);
            Box("UpperBandS", t, new Vector3(bayCx, RoomH - 0.5f, -BayZ + 0.05f), new Vector3(bayW, 1f, 0.1f), art.trim, shadows: false);
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
            for (int x = -2; x <= 2; x++)
                for (int z = -1; z <= 1; z++)
                    Quad("LightPanel", t, new Vector3(bayCx + x * 4.2f, RoomH - 0.01f, z * 5f), Quaternion.Euler(-90, 0, 0), new Vector2(2.6f, 0.9f), art.lightPanel);
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
            var pad = MeshObj("WorkPad", t, Annulus("Disk_0_3.4", 0f, 3.4f), art.floorMatte, new Vector3(0, 0.002f, 0.5f), 1f);
            pad.GetComponent<Renderer>().receiveShadows = true;
            foreach (float s in new[] { -1f, 1f })
                Quad("Walkway", t, new Vector3((3.6f + BayX1) / 2, 0.004f, s * 1.45f), Quaternion.Euler(90, 0, 0), new Vector2(BayX1 - 3.6f, 0.08f), line);

            // ---- 南牆：工作台＋工具板、儀器櫃、氣瓶 ----
            float wz = -BayZ;
            Solid("BenchBase", t, new Vector3(-5.2f, 0.42f, wz + 0.42f), new Vector3(2.8f, 0.84f, 0.7f), art.consoleBody, true);
            Box("BenchTop", t, new Vector3(-5.2f, 0.87f, wz + 0.45f), new Vector3(2.9f, 0.06f, 0.8f), fm.white);
            for (int i = 0; i < 4; i++)
                Box("Drawer", t, new Vector3(-6.25f + i * 0.7f, 0.5f, wz + 0.775f), new Vector3(0.6f, 0.02f, 0.01f), _steel, shadows: false);
            Box("Pegboard", t, new Vector3(-5.2f, 1.75f, wz + 0.04f), new Vector3(2.6f, 1.1f, 0.03f), wainscot, shadows: false);
            var tools = new[] { (0.08f, 0.5f), (0.3f, 0.12f), (0.06f, 0.36f), (0.22f, 0.22f), (0.05f, 0.44f), (0.34f, 0.1f), (0.1f, 0.3f), (0.18f, 0.26f) };
            for (int i = 0; i < tools.Length; i++)
            {
                var (w, h) = tools[i];
                Box("Tool", t, new Vector3(-6.25f + i * 0.3f, 1.85f - (i % 3) * 0.12f, wz + 0.07f), new Vector3(w, h, 0.03f), i % 3 == 0 ? fm.hazard : art.trim, shadows: false);
            }
            Box("BenchLamp", t, new Vector3(-5.2f, 2.38f, wz + 0.2f), new Vector3(2.4f, 0.04f, 0.3f), art.lightPanel, shadows: false);
            for (int i = 0; i < 3; i++)
            {
                float x = 4.6f + i * 0.95f;
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
        static void BuildPatchWall(Transform t)
        {
            var palette = new[]
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
            float[] ribs = { -6.2f, -2.95f, 0.3f, 3.55f, 6.8f, 10.05f };
            var mats = new Material[palette.Length];
            int n = 0;
            for (int b = 0; b + 1 < ribs.Length; b++)
                foreach (float off in new[] { -0.78f, 0.78f })
                {
                    int v = n % palette.Length;
                    if (mats[v] == null) mats[v] = PatchMaterial(v, palette[v]);
                    float x = (ribs[b] + ribs[b + 1]) / 2 + off;
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
            // 操作員站在 -Z 那一側，看向 +Z
            Solid("Body", t, new Vector3(0, 0.38f, 0.1f), new Vector3(width, 0.76f, 0.6f), art.consoleBody, true);
            Box("Desk", t, new Vector3(0, 0.78f, 0), new Vector3(width + 0.1f, 0.05f, 0.8f), art.trim);
            var glow = Box("Glow", t, new Vector3(0, 0.81f, -0.38f), new Vector3(width + 0.1f, 0.025f, 0.04f), fm.glow, shadows: false);
            Box("Riser", t, new Vector3(0, 1.1f, 0.33f), new Vector3(width, 0.6f, 0.08f), art.consoleBody);
            // Quad 正面朝 -Z（朝操作員），上緣往後仰 10°
            if (screen != null) Quad("Screen", t, new Vector3(0, 1.12f, 0.285f), Quaternion.Euler(10f, 0, 0), new Vector2(width - 0.15f, 0.5f), screen);
            var ct = go.AddComponent<ConsoleTarget>();
            ct.panel = panel;
            ct.titleKey = titleKey;
            ct.bodyKey = bodyKey;
            ct.phases = phases;
            ct.glow = glow.GetComponent<Renderer>();
            Label(t, titleKey, new Vector3(0, 1.75f, 0.2f), "tag", 9f);
            return ct;
        }

        /// <summary>牆上的大螢幕：畫面由 UI 層依 id 畫上去（WorldScreen）。</summary>
        static GameObject WallScreen(ArtBuilder.Art art, FacMats fm, Transform parent, string name, string id, Vector3 pos, float yaw, Vector2 size, Vector2Int res)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            Box("Frame", go.transform, new Vector3(0, 0, 0.04f), new Vector3(size.x + 0.16f, size.y + 0.16f, 0.06f), art.trim, shadows: false);
            // Quad 正面朝 -Z：讓它面向看的人（局部 -Z）
            var q = Quad("Screen", go.transform, Vector3.zero, Quaternion.identity, size, fm.screenOff);
            var ws = go.AddComponent<WorldScreen>();
            ws.id = id;
            ws.resolution = res;
            ws.target = q.GetComponent<Renderer>();
            return go;
        }

        static void BuildBayConsoles(ArtBuilder.Art art, FacMats fm, Transform bay)
        {
            // 採購終端：組裝區下單買零件（沒有價格，價格在中控室的型錄）
            var proc = Console(art, fm, bay, "ProcureConsole", new Vector3(4.2f, 0, -3.2f), -45f, "procure", "ui.fac.procure", "ui.fac.procure_body", "design,briefing", art.consoleScreens[0], 1.3f);
            proc.actionKey = "ui.hint.procure";
            // 環境測試台（振動／熱真空）：和回報台對稱，在整合台的另一邊
            var test = Console(art, fm, bay, "TestConsole", new Vector3(-4.2f, 0, -3.2f), 45f, "test", "ui.fac.test", "ui.fac.test_body", "design", art.consoleScreens[2], 1.3f);
            test.actionKey = "ui.hint.test";

            // 東牆（門的南邊）：失敗原因看板
            var fb = WallScreen(art, fm, bay, "FailureBoard", "failures", new Vector3(BayX1 - 0.08f, 2.4f, -5.2f), 90f, new Vector2(3.4f, 2.1f), new Vector2Int(1024, 640));
            var ct = fb.AddComponent<ConsoleTarget>();
            ct.panel = "failures";
            ct.titleKey = "ui.fac.failures";
            ct.bodyKey = "ui.fac.failures_body";
            ct.actionKey = "ui.hint.read";
            fb.AddComponent<BoxCollider>().size = new Vector3(3.5f, 2.2f, 0.12f);

            // 東牆（門的北邊）：儀表板大螢幕，整合台上這艘船現在的數字
            var ds = WallScreen(art, fm, bay, "BayDashboard", "dashboard", new Vector3(BayX1 - 0.08f, 2.4f, 5.2f), 90f, new Vector2(3.4f, 2.1f), new Vector2Int(1024, 640));
            var dt = ds.AddComponent<ConsoleTarget>();
            dt.panel = "dashboard";
            dt.titleKey = "ui.fac.dashboard";
            dt.bodyKey = "ui.fac.dashboard_body";
            dt.actionKey = "ui.hint.read";
            ds.AddComponent<BoxCollider>().size = new Vector3(3.5f, 2.2f, 0.12f);
        }

        const string ControlRoomModel = "Assets/GoNoGo/Art/Models/Room/FacilityControlRoom.glb";
        /// <summary>中控室模型的原點（世界座標）：Blender 的 (x, y, z) = 這裡 + Unity (x, z, y)。</summary>
        static readonly Vector3 CtrlOrigin = new Vector3((CtrlX0 + CtrlX1) / 2, 0, 0);

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
                var spot = BuildUtil.Light("LaunchSpot", LightType.Spot, new Color(1f, 0.82f, 0.75f), 30f, new Vector3(la.position.x, 6.6f, la.position.z), Quaternion.Euler(90, 0, 0), 9f, LightShadows.None, ctrl);
                spot.spotAngle = 28f;
                spot.innerSpotAngle = 14f;
            }

            // 燈：中控台上方暖色主光、後排補光、影像牆的藍光溢出
            var key0 = BuildUtil.Light("ConsoleKey", LightType.Spot, new Color(1f, 0.88f, 0.72f), 42f, new Vector3(21.5f, 6.6f, 0), Quaternion.Euler(90, 0, 0), 12f, LightShadows.None, ctrl);
            key0.spotAngle = 120f; key0.innerSpotAngle = 70f;
            var key1 = BuildUtil.Light("BackFill", LightType.Spot, new Color(0.85f, 0.9f, 1f), 18f, new Vector3(15.5f, 6.6f, 0), Quaternion.Euler(90, 0, 0), 10f, LightShadows.None, ctrl);
            key1.spotAngle = 120f; key1.innerSpotAngle = 60f;
            BuildUtil.Light("ScreenSpill", LightType.Point, new Color(0.35f, 0.6f, 1f), 14f, new Vector3(29f, 3.4f, 0), Quaternion.identity, 16f, LightShadows.None, ctrl);
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

        // ---------------- 大廳 ----------------

        static void BuildLobby(ArtBuilder.Art art, FacMats fm, Transform lobby)
        {
            const float X = 8f, Z0 = 34f, Z1 = 50f, H = 5f;
            float cz = (Z0 + Z1) / 2;
            Solid("Floor", lobby, new Vector3(0, -0.1f, cz), new Vector3(X * 2, 0.2f, Z1 - Z0), art.floor);
            Solid("Ceiling", lobby, new Vector3(0, H + 0.1f, cz), new Vector3(X * 2, 0.2f, Z1 - Z0), art.wallPanel);
            Solid("WallN", lobby, new Vector3(0, H / 2, Z1 + 0.15f), new Vector3(X * 2, H, 0.3f), art.wallDark);
            Solid("WallS", lobby, new Vector3(0, H / 2, Z0 - 0.15f), new Vector3(X * 2, H, 0.3f), art.wallPanel);
            Solid("WallW", lobby, new Vector3(-X - 0.15f, H / 2, cz), new Vector3(0.3f, H, Z1 - Z0), art.wallPanel);
            Solid("WallE", lobby, new Vector3(X + 0.15f, H / 2, cz), new Vector3(0.3f, H, Z1 - Z0), art.wallPanel);
            Box("Glow", lobby, new Vector3(0, 0.9f, Z1 - 0.02f), new Vector3(X * 2, 0.05f, 0.04f), art.accentGlow, shadows: false);
            for (int x = -1; x <= 1; x++)
                for (int z = 0; z < 3; z++)
                    Quad("LightPanel", lobby, new Vector3(x * 5f, H - 0.01f, Z0 + 3f + z * 5f), Quaternion.Euler(-90, 0, 0), new Vector2(2.4f, 0.9f), art.lightPanel);
            BuildUtil.Light("LobbyFill", LightType.Point, new Color(0.95f, 0.97f, 1f), 5f, new Vector3(0, 4f, cz), Quaternion.identity, 16f, LightShadows.None, lobby);

            WallScreen(art, fm, lobby, "LobbyScreen", "lobby", new Vector3(0, 2.6f, Z1 - 0.08f), 0f, new Vector2(6f, 3.4f), new Vector2Int(1280, 720));
            var c = Console(art, fm, lobby, "LobbyConsole", new Vector3(0, 0, Z1 - 3.5f), 0f, "lobby", "ui.fac.lobby", "ui.fac.lobby_body", "lobby", art.consoleScreens[0], 2f);
            c.actionKey = "ui.hint.use";
            // 兩個站位的地標
            Label(lobby, "ui.area.assembly", new Vector3(-4.5f, 2.2f, 42f), "sign", 12f);
            Label(lobby, "ui.area.control", new Vector3(4.5f, 2.2f, 42f), "sign", 12f);
            MeshObj("PadA", lobby, Annulus("Ring_1.1_1.2", 1.1f, 1.2f), art.accentGlow, new Vector3(-4.5f, 0.004f, 42f), 1f);
            MeshObj("PadC", lobby, Annulus("Ring_1.1_1.2", 1.1f, 1.2f), fm.hazard, new Vector3(4.5f, 0.004f, 42f), 1f);
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
