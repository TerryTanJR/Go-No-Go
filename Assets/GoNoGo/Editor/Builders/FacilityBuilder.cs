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
    /// Facility 是一個場景裡的三個區域：組裝區（x -11…11）、中控室（x 12…34，中間一道自動門）、
    /// 大廳（z 34…50，多人才用，開始任務時傳送過去）。營運時中控室大螢幕顯示遠處（y -400）的地球縮景。
    /// </summary>
    public static partial class SceneBuilder
    {
        const float RoomH = 6f;
        const float BayX0 = -11f, BayX1 = 11f, CtrlX0 = 12f, CtrlX1 = 34f, RoomZ = 9f;
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
            var racks = BuildRacks(art, fm, bay);
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
            dir.opsSpawn = Spawn("OpsSpawn", new Vector3(22.5f, 0.05f, -1.6f), 90);
            dir.lobbySpawn = Spawn("LobbySpawn", new Vector3(0, 0.05f, 38f), 0);
            dir.playerCamera = cam;
            dir.stand = stand;
            dir.racks = racks;
            dir.areaSplitX = 11.5f;
            dir.lobbyMinZ = 30f;

            Probe(bay, new Vector3(0, 2.5f, 0), new Vector3(22, RoomH, 18));
            Probe(ctrl, new Vector3(23, 2.5f, 0), new Vector3(22, RoomH, 18), 0.6f);

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
            float bayW = BayX1 - BayX0, ctrlW = CtrlX1 - CtrlX0, depth = RoomZ * 2;
            float bayCx = (BayX0 + BayX1) / 2, ctrlCx = (CtrlX0 + CtrlX1) / 2;
            Solid("Floor", bay, new Vector3(bayCx, -0.1f, 0), new Vector3(bayW, 0.2f, depth), art.floor);
            Solid("Floor", ctrl, new Vector3(ctrlCx, -0.1f, 0), new Vector3(ctrlW, 0.2f, depth), art.floorMatte);
            Solid("DoorSill", root, new Vector3(11.5f, -0.1f, 0), new Vector3(1f, 0.2f, 2.6f), art.trim);
            Solid("Ceiling", bay, new Vector3(bayCx, RoomH + 0.1f, 0), new Vector3(bayW + 2, 0.2f, depth + 2), art.wallPanel);
            Solid("Ceiling", ctrl, new Vector3(ctrlCx, RoomH + 0.1f, 0), new Vector3(ctrlW + 2, 0.2f, depth + 2), art.wallDark);

            // 組裝區：白色無塵室
            Solid("WallW", bay, new Vector3(BayX0 - 0.15f, RoomH / 2, 0), new Vector3(0.3f, RoomH, depth), art.wallPanel);
            Solid("WallN", bay, new Vector3(bayCx, RoomH / 2, RoomZ + 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), art.wallPanel);
            Solid("WallS", bay, new Vector3(bayCx, RoomH / 2, -RoomZ - 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), art.wallPanel);
            // 中控室：深色
            Solid("WallE", ctrl, new Vector3(CtrlX1 + 0.15f, RoomH / 2, 0), new Vector3(0.3f, RoomH, depth), art.wallDark);
            Solid("WallN", ctrl, new Vector3(ctrlCx, RoomH / 2, RoomZ + 0.15f), new Vector3(ctrlW + 0.6f, RoomH, 0.3f), art.wallDark);
            Solid("WallS", ctrl, new Vector3(ctrlCx, RoomH / 2, -RoomZ - 0.15f), new Vector3(ctrlW + 0.6f, RoomH, 0.3f), art.wallDark);

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

            // 牆上的細節：腳邊發光條、肋條、天花板燈板
            Box("GlowW", bay, new Vector3(BayX0 + 0.02f, 0.9f, 0), new Vector3(0.04f, 0.05f, depth), art.accentGlow, shadows: false);
            Box("GlowN", bay, new Vector3(bayCx, 0.9f, RoomZ - 0.02f), new Vector3(bayW, 0.05f, 0.04f), art.accentGlow, shadows: false);
            Box("GlowS", bay, new Vector3(bayCx, 0.9f, -RoomZ + 0.02f), new Vector3(bayW, 0.05f, 0.04f), art.accentGlow, shadows: false);
            for (int i = 0; i < 6; i++)
            {
                float x = BayX0 + 1.8f + i * 3.7f;
                Box("RibN" + i, bay, new Vector3(x, RoomH / 2, RoomZ - 0.1f), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
                Box("RibS" + i, bay, new Vector3(x, RoomH / 2, -RoomZ + 0.1f), new Vector3(0.2f, RoomH, 0.2f), art.trim, shadows: false);
            }
            Box("UpperBandN", bay, new Vector3(bayCx, RoomH - 0.5f, RoomZ - 0.05f), new Vector3(bayW, 1f, 0.1f), art.trim, shadows: false);
            Box("UpperBandS", bay, new Vector3(bayCx, RoomH - 0.5f, -RoomZ + 0.05f), new Vector3(bayW, 1f, 0.1f), art.trim, shadows: false);
            for (int x = -2; x <= 2; x++)
                for (int z = -1; z <= 1; z++)
                    Quad("LightPanel", bay, new Vector3(x * 4.2f, RoomH - 0.01f, z * 5.4f), Quaternion.Euler(-90, 0, 0), new Vector2(2.6f, 0.9f), art.lightPanel);
            for (int x = 0; x < 4; x++)
                Quad("LightStrip", ctrl, new Vector3(CtrlX0 + 3f + x * 5.3f, RoomH - 0.01f, 0), Quaternion.Euler(-90, 0, 0), new Vector2(0.3f, 14f), art.accentGlow);
            Box("GlowN", ctrl, new Vector3(ctrlCx, 0.9f, RoomZ - 0.02f), new Vector3(ctrlW, 0.05f, 0.04f), art.accentGlow, shadows: false);
            Box("GlowS", ctrl, new Vector3(ctrlCx, 0.9f, -RoomZ + 0.02f), new Vector3(ctrlW, 0.05f, 0.04f), art.accentGlow, shadows: false);
            for (int i = 0; i < 6; i++)
            {
                float x = CtrlX0 + 1.5f + i * 3.8f;
                Box("RibN" + i, ctrl, new Vector3(x, RoomH / 2, RoomZ - 0.1f), new Vector3(0.2f, RoomH, 0.3f), art.trim, shadows: false);
                Box("RibS" + i, ctrl, new Vector3(x, RoomH / 2, -RoomZ + 0.1f), new Vector3(0.2f, RoomH, 0.3f), art.trim, shadows: false);
            }

            // 燈：組裝區四盞聚光燈打在整合台、零件架上方補光；中控室暖色頂燈
            for (int i = 0; i < 4; i++)
            {
                var p = Quaternion.Euler(0, i * 90f + 45f, 0) * new Vector3(0, RoomH - 0.3f, -5f) + new Vector3(0, 0, 0.5f);
                var target = new Vector3(0, 1.4f, 0.5f);
                var l = BuildUtil.Light("StandSpot" + i, LightType.Spot, i % 2 == 0 ? new Color(1f, 0.95f, 0.88f) : new Color(0.85f, 0.92f, 1f), 26f, p, Quaternion.LookRotation(target - p), 14f, LightShadows.None, bay);
                l.spotAngle = 42f;
                l.innerSpotAngle = 22f;
            }
            for (int i = 0; i < 2; i++)
            {
                var l = BuildUtil.Light("CtrlSpot" + i, LightType.Spot, new Color(1f, 0.86f, 0.68f), 30f, new Vector3(CtrlX0 + 6f + i * 10f, RoomH - 0.3f, 0), Quaternion.Euler(90, 0, 0), 12f, LightShadows.None, ctrl);
                l.spotAngle = 110f;
                l.innerSpotAngle = 70f;
            }
            BuildUtil.Light("ScreenSpill", LightType.Point, new Color(0.35f, 0.6f, 1f), 6f, new Vector3(CtrlX1 - 3f, 3.2f, 0), Quaternion.identity, 12f, LightShadows.None, ctrl);
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

        // ---------------- 零件架 ----------------

        static PartRack[] BuildRacks(ArtBuilder.Art art, FacMats fm, Transform bay)
        {
            return new[]
            {
                Rack(art, fm, bay, "Rack_Payload", "instrument", "ui.fac.rack.payload", new Vector3(BayX0 + 0.55f, 0, 0), 90, 5.4f),
                Rack(art, fm, bay, "Rack_Power", "solar,battery", "ui.fac.rack.power", new Vector3(-4.2f, 0, RoomZ - 0.55f), 180, 4.6f),
                Rack(art, fm, bay, "Rack_Comms", "antenna", "ui.fac.rack.comms", new Vector3(3.6f, 0, RoomZ - 0.55f), 180, 3.2f),
                Rack(art, fm, bay, "Rack_Avionics", "computer,adcs", "ui.fac.rack.avionics", new Vector3(-4.2f, 0, -RoomZ + 0.55f), 0, 4.6f),
                Rack(art, fm, bay, "Rack_Structure", "propulsion,shielding", "ui.fac.rack.structure", new Vector3(3.6f, 0, -RoomZ + 0.55f), 0, 4.6f),
            };
        }

        static PartRack Rack(ArtBuilder.Art art, FacMats fm, Transform parent, string name, string categories, string signKey, Vector3 pos, float yaw, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var rack = go.AddComponent<PartRack>();
            rack.categories = categories;
            rack.width = width;
            rack.depth = 0.7f;
            rack.shelfHeights = new[] { 0.32f, 0.86f, 1.4f };
            rack.itemSize = 0.5f;
            var t = go.transform;
            // 淺色架子：深色零件放在上面才看得清楚
            Solid("Back", t, new Vector3(0, 1.0f, -0.4f), new Vector3(width + 0.3f, 2.0f, 0.08f), fm.white);
            for (int s = 0; s < rack.shelfHeights.Length; s++)
            {
                Box("Shelf" + s, t, new Vector3(0, rack.shelfHeights[s] - 0.03f, 0), new Vector3(width + 0.2f, 0.05f, 0.75f), fm.white);
                Box("ShelfEdge" + s, t, new Vector3(0, rack.shelfHeights[s] - 0.03f, 0.38f), new Vector3(width + 0.2f, 0.05f, 0.02f), art.accentGlow, shadows: false);
            }
            Box("ShelfGlow", t, new Vector3(0, 1.98f, 0.0f), new Vector3(width + 0.2f, 0.04f, 0.7f), art.accentGlow, shadows: false);
            Solid("PostL", t, new Vector3(-width / 2 - 0.15f, 1.0f, 0), new Vector3(0.08f, 2.0f, 0.75f), art.trim);
            Solid("PostR", t, new Vector3(width / 2 + 0.15f, 1.0f, 0), new Vector3(0.08f, 2.0f, 0.75f), art.trim);
            // 看不見的牆，避免走進層板裡
            var block = new GameObject("Block");
            block.transform.SetParent(t, false);
            block.transform.localPosition = new Vector3(0, 1f, -0.05f);
            block.layer = 2; // 不擋準星
            block.AddComponent<BoxCollider>().size = new Vector3(width + 0.3f, 2f, 0.7f);
            Label(t, signKey, new Vector3(0, 2.35f, 0), "sign", 22f);
            var l = BuildUtil.Light("RackLight", LightType.Spot, new Color(1f, 0.97f, 0.92f), 6f, Vector3.zero, Quaternion.identity, 5f, LightShadows.None, t);
            l.transform.localPosition = new Vector3(0, 2.9f, 1.4f);
            l.transform.localRotation = Quaternion.LookRotation(new Vector3(0, 0.9f, 0) - l.transform.localPosition);
            l.spotAngle = 80f;
            l.innerSpotAngle = 50f;
            return rack;
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
            // 回報台：把台上的設計送到中控室
            var rep = Console(art, fm, bay, "ReportConsole", new Vector3(4.2f, 0, -3.2f), -45f, "report", "ui.fac.report", "ui.fac.report_body", "design", art.consoleScreens[0], 1.3f);
            rep.actionKey = "ui.hint.report";
            // 環境測試台（振動／熱真空）：和回報台對稱，在整合台的另一邊
            var test = Console(art, fm, bay, "TestConsole", new Vector3(-4.2f, 0, -3.2f), 45f, "test", "ui.fac.test", "ui.fac.test_body", "design", art.consoleScreens[2], 1.3f);
            test.actionKey = "ui.hint.test";
            // 組裝區的手冊：嚴格分工時只有代號與用途（bay_manual）
            Console(art, fm, bay, "BayManual", new Vector3(8.2f, 0, 5.6f), 45f, "bay_manual", "ui.fac.manual", "ui.fac.manual_body", "", fm.paper, 1.1f);

            // 東牆（門的南邊）：失敗原因看板
            var fb = WallScreen(art, fm, bay, "FailureBoard", "failures", new Vector3(BayX1 - 0.08f, 2.4f, -5.2f), 90f, new Vector2(3.4f, 2.1f), new Vector2Int(1024, 640));
            var ct = fb.AddComponent<ConsoleTarget>();
            ct.panel = "failures";
            ct.titleKey = "ui.fac.failures";
            ct.bodyKey = "ui.fac.failures_body";
            ct.actionKey = "ui.hint.read";
            fb.AddComponent<BoxCollider>().size = new Vector3(3.5f, 2.2f, 0.12f);
        }

        static void BuildControlRoom(ArtBuilder.Art art, FacMats fm, Transform ctrl)
        {
            // 東牆大螢幕（營運時顯示地球縮景）
            var main = new GameObject("MainScreen");
            main.transform.SetParent(ctrl, false);
            main.transform.localPosition = new Vector3(CtrlX1 - 0.1f, 3.2f, 0);
            main.transform.localRotation = Quaternion.Euler(0, 90, 0);
            Box("Frame", main.transform, new Vector3(0, 0, 0.04f), new Vector3(10.4f, 5.9f, 0.06f), art.trim, shadows: false);
            Quad("Screen", main.transform, Vector3.zero, Quaternion.identity, new Vector2(10.2f, 5.74f), fm.screenRt);
            Box("Ticker", main.transform, new Vector3(0, 3.1f, 0), new Vector3(10.4f, 0.18f, 0.05f), art.accentGlow, shadows: false);

            Console(art, fm, ctrl, "Dashboard", new Vector3(19.5f, 0, -1.6f), 90f, "dashboard", "ui.fac.dashboard", "ui.fac.dashboard_body", "", art.consoleScreens[1], 1.8f);
            Console(art, fm, ctrl, "MissionConsole", new Vector3(19.5f, 0, -5.2f), 90f, "mission", "ui.fac.mission", "ui.fac.mission_body", "design,briefing", art.consoleScreens[2], 1.8f);
            var phone = Console(art, fm, ctrl, "PhoneDesk", new Vector3(19.5f, 0, 2.6f), 90f, "phone", "ui.fac.phone", "ui.fac.phone_body", "", null, 1.4f);
            // 紅色電話
            Box("Phone", phone.transform, new Vector3(0, 0.86f, 0.05f), new Vector3(0.36f, 0.1f, 0.24f), fm.red);
            Box("Handset", phone.transform, new Vector3(0, 0.95f, 0.05f), new Vector3(0.42f, 0.07f, 0.09f), fm.red);
            Console(art, fm, ctrl, "Notes", new Vector3(19.5f, 0, 5.8f), 90f, "notes", "ui.fac.notes", "ui.fac.notes_body", "", fm.paper, 1.4f);
            Console(art, fm, ctrl, "ControlManual", new Vector3(14.6f, 0, 7.4f), 0f, "manual", "ui.fac.manual", "ui.fac.manual_body", "", fm.paper, 1.2f);
            Console(art, fm, ctrl, "OpsDesk", new Vector3(25.5f, 0, 0), 90f, "ops_desk", "ui.fac.ops_desk", "ui.fac.ops_desk_body", "ops,report", art.consoleScreens[3], 2.4f);

            // 四座營運控制台：北牆兩座、南牆兩座
            string[] ids = { OpsConsoles.Power, OpsConsoles.Payload, OpsConsoles.Attitude, OpsConsoles.Comms };
            for (int i = 0; i < 4; i++)
            {
                bool north = i < 2;
                var pos = new Vector3(i % 2 == 0 ? 27.5f : 31f, 0, north ? RoomZ - 1.2f : -RoomZ + 1.2f);
                var c = Console(art, fm, ctrl, "Ops_" + ids[i], pos, north ? 0f : 180f, "ops:" + ids[i], "ops.console." + ids[i], "ui.fac.ops_console_body", "ops", art.consoleScreens[i], 1.6f);
                c.actionKey = "ui.hint.operate";
            }

            // 發射按鈕：圓柱底座＋紅色按鈕＋黃黑框
            var launch = new GameObject("LaunchButton");
            launch.transform.SetParent(ctrl, false);
            launch.transform.localPosition = new Vector3(29.5f, 0, 0);
            var cyl = HiCylinder();
            var pillar = MeshObj("Pillar", launch.transform, cyl, art.consoleBody, new Vector3(0, 0.5f, 0), 1f);
            pillar.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
            pillar.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            var pc = launch.AddComponent<CapsuleCollider>();
            pc.center = new Vector3(0, 0.6f, 0);
            pc.radius = 0.4f;
            pc.height = 1.2f;
            MeshObj("Guard", launch.transform, Annulus("Ring_0.25_0.34", 0.25f, 0.34f), fm.hazard, new Vector3(0, 1.005f, 0), 1f);
            var btn = MeshObj("Button", launch.transform, cyl, fm.red, new Vector3(0, 1.05f, 0), 1f);
            btn.transform.localScale = new Vector3(0.4f, 0.05f, 0.4f);
            MeshObj("FloorRing", launch.transform, Annulus("Ring_1.1_1.16", 1.1f, 1.16f), fm.redGlow, new Vector3(0, 0.004f, 0), 1f);
            var lt = launch.AddComponent<ConsoleTarget>();
            lt.panel = "launch";
            lt.titleKey = "ui.fac.launch";
            lt.bodyKey = "ui.fac.launch_body";
            lt.actionKey = "ui.hint.launch";
            lt.phases = "design";
            lt.glow = btn.GetComponent<Renderer>();
            Label(launch.transform, "ui.fac.launch", new Vector3(0, 1.7f, 0), "sign", 10f);
            BuildUtil.Light("LaunchGlow", LightType.Point, new Color(1f, 0.25f, 0.15f), 1.5f, launch.transform.position + Vector3.up * 1.4f, Quaternion.identity, 3f, LightShadows.None, ctrl);

            // 南牆：失敗原因看板；北牆：儀表板大螢幕
            var fb = WallScreen(art, fm, ctrl, "FailureBoard", "failures", new Vector3(16.5f, 2.6f, -RoomZ + 0.08f), 180f, new Vector2(3.6f, 2.25f), new Vector2Int(1024, 640));
            var ft = fb.AddComponent<ConsoleTarget>();
            ft.panel = "failures";
            ft.titleKey = "ui.fac.failures";
            ft.bodyKey = "ui.fac.failures_body";
            ft.actionKey = "ui.hint.read";
            fb.AddComponent<BoxCollider>().size = new Vector3(3.7f, 2.3f, 0.12f);
            WallScreen(art, fm, ctrl, "DashboardWall", "dashboard", new Vector3(17f, 3.0f, RoomZ - 0.08f), 0f, new Vector2(5f, 2.8f), new Vector2Int(1280, 720));
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
