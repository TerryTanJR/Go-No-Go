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
            dir.opsSpawn = Spawn("OpsSpawn", new Vector3(16.5f, 0.05f, 0), 90);
            dir.lobbySpawn = Spawn("LobbySpawn", new Vector3(0, 0.05f, 38f), 0);
            dir.playerCamera = cam;
            dir.stand = stand;
            dir.racks = racks;
            dir.areaSplitX = 11.5f;
            dir.lobbyMinZ = 30f;

            Probe(bay, new Vector3(0, 2.5f, 0), new Vector3(22, RoomH, 18));
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
            float bayW = BayX1 - BayX0, depth = RoomZ * 2;
            float bayCx = (BayX0 + BayX1) / 2;
            // 中控室的地板、牆、天花板在 Blender 模型裡（BuildControlRoom）
            Solid("Floor", bay, new Vector3(bayCx, -0.1f, 0), new Vector3(bayW, 0.2f, depth), art.floor);
            Solid("DoorSill", root, new Vector3(11.5f, -0.1f, 0), new Vector3(1f, 0.2f, 2.6f), art.trim);
            Solid("Ceiling", bay, new Vector3(bayCx, RoomH + 0.1f, 0), new Vector3(bayW + 2, 0.2f, depth + 2), art.wallPanel);

            // 組裝區：白色無塵室
            Solid("WallW", bay, new Vector3(BayX0 - 0.15f, RoomH / 2, 0), new Vector3(0.3f, RoomH, depth), art.wallPanel);
            Solid("WallN", bay, new Vector3(bayCx, RoomH / 2, RoomZ + 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), art.wallPanel);
            Solid("WallS", bay, new Vector3(bayCx, RoomH / 2, -RoomZ - 0.15f), new Vector3(bayW + 0.6f, RoomH, 0.3f), art.wallPanel);

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

            // 燈：組裝區四盞聚光燈打在整合台、零件架上方補光；中控室的燈在 BuildControlRoom
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
            // 採購終端：組裝區下單買零件（沒有價格，價格在中控室的型錄）
            var proc = Console(art, fm, bay, "ProcureConsole", new Vector3(4.2f, 0, -3.2f), -45f, "procure", "ui.fac.procure", "ui.fac.procure_body", "design,briefing", art.consoleScreens[0], 1.3f);
            proc.actionKey = "ui.hint.procure";
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
