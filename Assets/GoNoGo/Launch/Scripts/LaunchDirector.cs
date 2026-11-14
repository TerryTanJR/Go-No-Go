using System;
using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using GoNoGo.Presentation;
using Unity.Cinemachine;
using UnityEngine;

namespace GoNoGo.Launch
{
    /// <summary>
    /// 發射導演：用一張「提示表」（幾秒做什麼）演出整段發射，結果在按下發射時就由核心決定好了。
    /// 五種結果（正常、低軌、整流罩故障、異常爆炸、速度不足）共用前半段，後半段分支。
    /// 沒有用 Timeline 是因為分支：五條 Timeline 會重複大部分內容，提示表只要一份。
    /// 鏡頭全部是 Cinemachine 相機，切換用優先權，混合由 Brain 處理。
    /// <para>
    /// 爬升段是一個連續的長焦追蹤鏡頭：天空隨高度變黑、星星浮現，助推器分離、主引擎關機、節間分離、
    /// 第二節點火都在同一個鏡頭裡發生。天空全黑之後才換到太空場景，換的那一格鏡頭構圖一模一樣（同方向、同大小），
    /// 再慢慢移到上節後方，地球從畫面下方出現。
    /// </para>
    /// </summary>
    public sealed class LaunchDirector : MonoBehaviour
    {
        [Header("場景")]
        public Transform rocketAnchor;
        public Transform spaceAnchor;
        public Light sun;
        public Vector3 padSunEuler = new Vector3(8f, -60f, 0f);
        public Vector3 spaceSunEuler = new Vector3(25f, -120f, 0f);
        [Tooltip("太空中的陽光是白的（發射台是黃昏的暖色）")]
        public Color spaceSunColor = new Color(1f, 0.97f, 0.93f);
        public float spaceSunIntensity = 2.4f;
        public Light engineLight;
        public Light flashLight;
        public ParticleSystem[] padSmoke;
        public ParticleSystem fireball;
        public ParticleSystem debris;
        public GameObject padSet;
        public GameObject spaceSet;
        [Tooltip("發射塔與導焰槽的煙：跟著火箭高度縮放（Electron 18 m 旁邊不能站一座 Falcon 的塔）")]
        public Transform padScaled;

        [Header("鏡頭")]
        public CinemachineBrain brain;
        public CinemachineCamera camWide, camEngine, camTower, camLong, camSpace, camSide, camDeploy;

        [Header("聲音")]
        public AudioSource countdown, roar, rumble;

        const float StartT = -12f;
        // 爬升段的時間表（秒，T+）。動畫時間是壓縮過的，遙測顯示的是真實量級
        const float BoosterSepT = 13f;   // 有助推器的火箭才有
        const float MecoT = 18f;         // 主引擎關機
        const float SepT = 19f;          // 節間分離
        const float IgnT = 20.5f;        // 第二節點火
        const float SwapT = 24f;         // 天空已全黑：換到太空場景
        const float FairingT = 31f;
        const float UpperSpeed = 30f;    // 太空段上節的畫面速度（m/s）
        const float TransferFov = 50f;   // 換場那一格的視角：太窄的話鏡頭得放到幾公里外，會鑽進地球
        const float CallLead = 0.08f;    // 倒數數字從開口到聽得出來的延遲

        RocketParts _rocket;
        TrailRenderer _trail;
        GameObject _payload;
        Transform _firstStageFree;
        float _T;
        float _pathAlt;     // 發射台段飛過的距離（公尺）：尾焰在稀薄空氣裡變長
        float _pathVel;
        float _accel;       // 目前的加速度（m/s²）：分離出去的物體依它算相對運動
        bool _inSpace, _exploded, _fairingOpen, _deployed, _falling, _skipping, _done;
        float _deployT, _fairingT, _fallT;
        float _fallAlt, _fallVel;
        Vector3 _fallPivotLocal;      // 上節中心（root 的區域座標）：翻滾繞這一點，不繞火箭底部
        CinemachineFollow _sideFollow;
        bool _fairingFree;            // 整流罩打開後脫離箭體，各自往外飄
        Vector3 _fairingVelL, _fairingVelR;
        Transform _fallFocus;         // 不跟著翻滾的追蹤點：鏡頭跟著它，畫面的上下才不會跟著箭體甩
        Vector3 _sideOffset0;
        float _shake;
        float _endT;
        float _orbitAltKm;
        float _camK;                  // 太空鏡頭距離倍率（小火箭鏡頭靠近一點）
        readonly List<Cue> _cues = new List<Cue>();
        int _nextCue;
        LaunchOutcome _outcome;
        string _reason;

        // 飛行方位：垂直於長焦鏡頭的視線，火箭在畫面裡由左往右轉向，分離過程看得到側面
        Quaternion _yaw;
        Vector3 _yawRight;
        float _rollDeg;
        float _firstLen;              // 上節底部在 root 上的高度
        Vector3 _upperPos;            // 太空段：上節底部的世界座標（俯仰繞這一點轉）

        // 長焦鏡頭：自己算瞄準點與視角（像真的追蹤攝影師一樣邊追邊變焦）
        Transform _longFocus;
        Transform _upperFocus;        // 上節＋整流罩的中段：太空鏡頭瞄準這裡，整流罩分離時兩半都在畫面裡
        CinemachineRotationComposer _longAim;
        float _longFov0, _frameBase, _frameUpper;
        Vector3 _transferOffset;

        // 分離出去的助推器、第一節
        // 用「相對火箭」的位置與速度來算：它們跟火箭走同一條彎曲的重力轉向路徑，只有相對運動。
        // 如果給它們一個固定的世界速度，火箭轉向時兩者的方向差（每秒幾度 × 200 m/s）會在幾秒內拉開上百公尺，
        // 小火箭的畫面裡上節直接被甩出去
        sealed class FreeBody
        {
            public Transform t;
            public Vector3 rel, relV;
            public float keep;        // 保留火箭加速度的比例：相對運動慢一點，分離過程才看得完整
            public Vector3 axis;
            public float spin;
        }
        readonly List<FreeBody> _free = new List<FreeBody>();

        // 天空變黑
        Material _skyInst, _starsInst, _starsShared;
        float _sky0Exposure, _sky0Thickness, _fog0, _starsBright, _starsT;
        Color _amb0Sky, _amb0Eq, _amb0Ground, _sun0Color;
        Quaternion _sun0Rot;
        float _sun0Intensity;
        EnvironmentOverride _spaceEnv;
        ReflectionProbe _padProbe;
        float _padProbe0;

        // 倒數錄音
        float[] _calls;
        bool _audioClock;

        struct Cue
        {
            public float t;
            public Action act;
            public Cue(float t, Action a) { this.t = t; act = a; }
        }

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            LaunchFeed.DirectorActive = true;
            LaunchFeed.Reset();
            LaunchFeed.T = StartT;
        }

        void OnDestroy()
        {
            LaunchFeed.DirectorActive = false;
            LaunchFeed.SkipRequested -= Skip;
            if (_skyInst != null) Destroy(_skyInst);
            if (_starsInst != null) Destroy(_starsInst);
        }

        void Start()
        {
            GameSession.EnsureScenario();
            var s = GameSession.State;
            if (s.Result == null) s.Launch(); // 直接開這個場景測試時
            _outcome = s.Result.Launch.Outcome;
            _reason = s.Result.Launch.ReasonCode ?? "";
            var orbit = s.Data.Orbit(s.Design.OrbitId);
            _orbitAltKm = (float)Math.Min(orbit?.AltitudeKm ?? 500, 1200);

            var kit = LaunchKit.Load();
            _rocket = RocketBuilder.Build(s.Design.LauncherId, kit, rocketAnchor);
            _firstLen = _rocket.upperStage.localPosition.y;
            // 整流罩沿上節的 X 軸分成兩半，而太空側面鏡頭正好在 +X 那一側：其中一半會直直飛向鏡頭、擋住整個畫面。
            // 上節繞自己的軸轉 90°，兩半改成在畫面上下方向分開；衛星反向轉回來，展開鏡頭看到的方向不變
            _rocket.upperStage.localRotation = Quaternion.Euler(0f, 90f, 0f);
            _rocket.payloadMount.localRotation = Quaternion.Euler(0f, -90f, 0f);
            BuildPayload(s);

            _longFocus = new GameObject("LongFocus").transform;
            _longFocus.position = _rocket.root.position;
            foreach (var cam in new[] { camWide, camEngine, camTower })
                if (cam != null) cam.Target.TrackingTarget = _rocket.root;
            if (camLong != null) camLong.Target.TrackingTarget = _longFocus;
            _upperFocus = new GameObject("UpperFocus").transform;
            foreach (var cam in new[] { camSpace, camSide })
                if (cam != null) cam.Target.TrackingTarget = _upperFocus;
            if (camDeploy != null) camDeploy.Target.TrackingTarget = _payload != null ? _payload.transform : _rocket.upperStage;
            // 鏡頭距離跟著火箭大小（Electron 18 m、Falcon Heavy 70 m）
            float k = _rocket.height / 70f;
            if (padScaled != null) padScaled.localScale = Vector3.one * Mathf.Clamp(k * 1.05f, 0.3f, 1f);
            ScaleCamOffset(camWide, Mathf.Max(0.45f, k));
            // 長焦鏡頭在 1.6 km 外：18 m 的 Electron 只剩幾個像素，用更窄的視角補回來
            if (camLong != null) camLong.Lens.FieldOfView *= Mathf.Clamp(k * 1.3f, 0.4f, 1f);
            // 塔邊鏡頭瞄準箭身中段，而不是底部
            var towerAim = camTower != null ? camTower.GetComponent<CinemachineRotationComposer>() : null;
            if (towerAim != null) towerAim.TargetOffset = new Vector3(0, _rocket.height * 0.35f, 0);
            ScaleCamOffset(camTower, k);
            ScaleCamOffset(camEngine, k);
            // 太空段要看得到整個上節、地球和分離出去的東西：比貼身追蹤再拉遠一些
            ScaleCamFollow(camSpace, Mathf.Max(0.35f, k) * 1.7f);
            ScaleCamFollow(camSide, Mathf.Max(0.35f, k) * 1.7f);
            _camK = Mathf.Max(0.35f, k);
            ScaleCamFollow(camDeploy, _camK);

            SetupFlightAzimuth();
            SetupLongCam();

            sun.transform.rotation = Quaternion.Euler(padSunEuler);
            if (spaceSet != null) spaceSet.SetActive(false);
            if (engineLight != null) engineLight.intensity = 0;
            if (flashLight != null) flashLight.intensity = 0;
            foreach (var ps in padSmoke) if (ps != null) { var em = ps.emission; em.enabled = false; }
            SetupSky();

            BuildCues();
            _T = StartT;
            Cut(camWide);
            LaunchFeed.SkipRequested += Skip;
            if (countdown != null && countdown.clip != null)
            {
                var bank = AudioBank.Load();
                _calls = bank != null && bank.countdownCalls != null && bank.countdownCalls.Length >= 2 ? bank.countdownCalls : null;
                // 從 T−12 對應的錄音位置開始播：念「十」的那一刻剛好是 T−10
                double start = _calls != null ? LaunchClock.AudioFromT(StartT, _calls, CallLead) : 42.5;
                countdown.time = Mathf.Clamp((float)start, 0, countdown.clip.length - 1);
                countdown.Play();
                _audioClock = _calls != null;
            }
        }

        /// <summary>
        /// 火箭往「長焦鏡頭的右方」轉向，整個爬升都是側面。太空場景整組轉到同一個方位，
        /// 所以換場那一格火箭的世界方向完全一樣；太空鏡頭的偏移量也跟著轉，原本調好的構圖不變。
        /// </summary>
        void SetupFlightAzimuth()
        {
            var toPad = camLong != null ? rocketAnchor.position - camLong.transform.position : Vector3.forward;
            toPad.y = 0;
            var heading = toPad.sqrMagnitude > 1f ? new Vector3(toPad.z, 0, -toPad.x).normalized : Vector3.forward;
            _yaw = Quaternion.LookRotation(heading, Vector3.up);
            _yawRight = _yaw * Vector3.right;
            _rollDeg = Mathf.DeltaAngle(0f, _yaw.eulerAngles.y);
            if (spaceSet != null) spaceSet.transform.rotation = _yaw;
            foreach (var cam in new[] { camSpace, camSide, camDeploy })
            {
                var f = cam != null ? cam.GetComponent<CinemachineFollow>() : null;
                if (f != null) f.FollowOffset = _yaw * f.FollowOffset;
            }
        }

        void SetupLongCam()
        {
            if (camLong == null) return;
            _longAim = camLong.GetComponent<CinemachineRotationComposer>();
            _longFov0 = camLong.Lens.FieldOfView;
            // 一開始的取景高度（公尺）：之後距離變遠就把視角收窄，火箭在畫面裡維持一樣大
            float d0 = Vector3.Distance(camLong.transform.position, rocketAnchor.position);
            _frameBase = 2f * d0 * Mathf.Tan(_longFov0 * 0.5f * Mathf.Deg2Rad);
            // 分離後只追上節：上節＋整流罩佔畫面約 1/3
            _frameUpper = Mathf.Max((_rocket.height - _firstLen) * 3.2f, 12f);
        }

        void SetupSky()
        {
            // 天空盒、霧、環境光都只改執行期的複本，不能改到場景與材質資產
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_Exposure") && sky.HasProperty("_AtmosphereThickness"))
            {
                _skyInst = new Material(sky) { name = sky.name + " (runtime)" };
                _sky0Exposure = _skyInst.GetFloat("_Exposure");
                _sky0Thickness = _skyInst.GetFloat("_AtmosphereThickness");
                RenderSettings.skybox = _skyInst;
            }
            _fog0 = RenderSettings.fogDensity;
            _amb0Sky = RenderSettings.ambientSkyColor;
            _amb0Eq = RenderSettings.ambientEquatorColor;
            _amb0Ground = RenderSettings.ambientGroundColor;
            _sun0Rot = sun.transform.rotation;
            _sun0Color = sun.color;
            _sun0Intensity = sun.intensity;
            _spaceEnv = spaceSet != null ? spaceSet.GetComponent<EnvironmentOverride>() : null;
            _starsShared = _spaceEnv != null ? _spaceEnv.skybox : null;
            _padProbe = padSet != null ? padSet.GetComponentInChildren<ReflectionProbe>(true) : null;
            if (_padProbe != null) _padProbe0 = _padProbe.intensity;
        }

        void BuildPayload(GameState s)
        {
            _payload = SatelliteBuilder.Build(s.Design, s.Data, s.Evaluation, _rocket.payloadMount);
            // 縮到整流罩裝得下：直徑 0.8 × 罩寬
            var rs = _payload.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float fit = _rocket.diameter * 0.75f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            _payload.transform.localScale = Vector3.one * Mathf.Min(1f, fit);
            _payload.transform.localPosition = new Vector3(0, b.size.y * 0.5f * Mathf.Min(1f, fit), 0);
            // 太陽能板發射時收起來
            foreach (var p in _payload.GetComponentsInChildren<SatPart>())
                if (p.category == PartCategory.Solar) p.transform.localScale = new Vector3(0.12f, 1f, 1f);
        }

        static void ScaleCamOffset(CinemachineCamera cam, float k)
        {
            if (cam == null) return;
            var p = cam.transform.position;
            cam.transform.position = new Vector3(p.x * Mathf.Max(0.3f, k), p.y * Mathf.Max(0.3f, k), p.z * Mathf.Max(0.3f, k));
        }

        static void ScaleCamFollow(CinemachineCamera cam, float k)
        {
            if (cam == null) return;
            var f = cam.GetComponent<CinemachineFollow>();
            if (f != null) f.FollowOffset *= k;
        }

        // ---------------- 提示表 ----------------

        void BuildCues()
        {
            _cues.Clear();
            _cues.Add(new Cue(-10f, () => Say("ui.launch.call.go")));
            _cues.Add(new Cue(-4.5f, () => Cut(camEngine)));
            _cues.Add(new Cue(-3f, Ignition));
            _cues.Add(new Cue(0f, Liftoff));
            _cues.Add(new Cue(0.6f, () => Cut(camTower)));
            _cues.Add(new Cue(3.5f, () => Say("ui.launch.call.tower")));
            _cues.Add(new Cue(5.5f, () => { Blend(camLong, 1.2f); Say("ui.launch.call.pitch"); }));

            bool anomaly = _outcome == LaunchOutcome.Failure && _reason == "launch.anomaly";
            if (anomaly)
            {
                _cues.Add(new Cue(10f, Explode));
                _cues.Add(new Cue(11.2f, () => Say("ui.launch.call.anomaly")));
                _endT = 16f;
                _cues.Add(new Cue(_endT, Finish));
                return;
            }

            _cues.Add(new Cue(10f, () => { Stage("ui.launch.stage.maxq"); Say("ui.launch.call.maxq"); }));
            if (HasBoosters) _cues.Add(new Cue(BoosterSepT, BoosterSeparation));
            _cues.Add(new Cue(MecoT, Meco));
            _cues.Add(new Cue(SepT, StageSeparation));
            _cues.Add(new Cue(IgnT, SecondStageIgnition));
            _cues.Add(new Cue(SwapT, GoToSpace));
            // 換場後從「仰望上節」慢慢移到上節後上方：地球從畫面下方出現
            _cues.Add(new Cue(SwapT + 0.6f, () => Blend(camSpace, 3.5f)));
            _cues.Add(new Cue(FairingT - 3f, () => Blend(camSide, 2.5f)));

            switch (_outcome)
            {
                case LaunchOutcome.FairingFailure:
                    _cues.Add(new Cue(FairingT, () => { Stage("ui.launch.stage.fairing"); Say("ui.launch.call.fairing_fail"); Sfx.Alert(); }));
                    _cues.Add(new Cue(FairingT + 4f, () => { Stage("ui.launch.stage.anomaly"); Say("ui.launch.call.underspeed"); StartFall(); }));
                    _endT = FairingT + 10f;
                    break;
                case LaunchOutcome.Failure: // 太重或到不了：上節燒完還是速度不足
                    _cues.Add(new Cue(FairingT, OpenFairing));
                    _cues.Add(new Cue(FairingT + 3f, () => { Stage("ui.launch.stage.anomaly"); Say("ui.launch.call.underspeed"); StartFall(); }));
                    _endT = FairingT + 9f;
                    break;
                default:
                    _cues.Add(new Cue(FairingT, OpenFairing));
                    _cues.Add(new Cue(FairingT + 4.5f, () =>
                    {
                        Stage("ui.launch.stage.orbit");
                        Say(_outcome == LaunchOutcome.LowerOrbit ? "ui.launch.call.lower" : "ui.launch.call.orbit");
                        SetUpperThrust(false);
                    }));
                    _cues.Add(new Cue(FairingT + 6.5f, Deploy));
                    _endT = FairingT + 13f;
                    break;
            }
            _cues.Add(new Cue(_endT, Finish));
        }

        bool HasBoosters
        {
            get
            {
                foreach (Transform c in _rocket.firstStage) if (c.name.StartsWith("Booster")) return true;
                return false;
            }
        }

        void Say(string key) { if (!_skipping) LaunchFeed.Say(key); }
        static void Stage(string key) => LaunchFeed.StageKey = key;

        void Cut(CinemachineCamera cam) => Activate(cam, 0f);
        void Blend(CinemachineCamera cam, float seconds) => Activate(cam, seconds);

        void Activate(CinemachineCamera cam, float blend)
        {
            if (cam == null) return;
            if (brain != null)
                brain.DefaultBlend = blend <= 0f || _skipping
                    ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
                    : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blend);
            cam.Prioritize();
        }

        // ---------------- 事件 ----------------

        void Ignition()
        {
            Stage("ui.launch.stage.ignition");
            Say("ui.launch.call.ignition");
            foreach (var pr in _rocket.firstStagePlumes) pr.enabled = true;
            foreach (var ps in padSmoke) if (ps != null) { var em = ps.emission; em.enabled = true; ps.Play(); }
            if (!_skipping && roar != null && roar.clip != null)
            {
                var bank = AudioBank.Load();
                roar.time = Mathf.Clamp((bank != null ? bank.padIgnitionAt : 8f) - 1f, 0, roar.clip.length - 1);
                roar.Play();
            }
        }

        void Liftoff()
        {
            StartTrail();
            Stage("ui.launch.stage.ascent");
            Say("ui.launch.call.liftoff");
            if (!_skipping && rumble != null && rumble.clip != null) rumble.Play();
        }

        /// <summary>爬升尾跡：掛在第一節底部，世界座標拖出一道由細變粗、慢慢變淡的煙。</summary>
        void StartTrail()
        {
            var kit = LaunchKit.Load();
            if (kit == null || kit.trail == null || _trail != null) return;
            var go = new GameObject("AscentTrail");
            go.transform.SetParent(_rocket.root, false);
            go.transform.localPosition = new Vector3(0, -_rocket.diameter * 0.8f, 0);
            _trail = go.AddComponent<TrailRenderer>();
            _trail.sharedMaterial = kit.trail;
            _trail.time = 9f;
            _trail.minVertexDistance = Mathf.Max(0.5f, _rocket.diameter * 0.4f);
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.numCapVertices = 4;
            _trail.widthMultiplier = _rocket.diameter;
            _trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(0.15f, 3.2f), new Keyframe(1f, 10f));
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.86f, 0.62f), 0f), new GradientColorKey(new Color(0.95f, 0.93f, 0.9f), 0.08f), new GradientColorKey(new Color(0.82f, 0.82f, 0.86f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.04f), new GradientAlphaKey(0.55f, 0.5f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = g;
            _trail.emitting = true;
        }

        void Explode()
        {
            _exploded = true;
            Stage("ui.launch.stage.anomaly");
            var at = _rocket.root.position + _rocket.root.up * _rocket.height * 0.5f;
            if (fireball != null) { fireball.transform.position = at; fireball.Play(); }
            if (debris != null) { debris.transform.position = at; debris.Play(); }
            if (flashLight != null) { flashLight.transform.position = at; flashLight.intensity = 400f; flashLight.range = _rocket.height * 6f; }
            foreach (var r in _rocket.root.GetComponentsInChildren<Renderer>()) r.enabled = false;
            // 尾跡停在爆炸點，留在空中慢慢散掉（不要跟著隱藏）
            if (_trail != null) { _trail.emitting = false; _trail.enabled = true; }
            if (engineLight != null) engineLight.intensity = 0;
            _shake = 3f;
            if (!_skipping) Sfx.Stamp();
        }

        /// <summary>助推器分離：熄火、往外推開、頂端往外倒，慢慢落到後面（長焦鏡頭裡看得到整個過程）。</summary>
        void BoosterSeparation()
        {
            Say("ui.launch.call.boostersep");
            var up = _rocket.root.up;
            var boosters = new List<Transform>();
            foreach (Transform c in _rocket.firstStage) if (c.name.StartsWith("Booster")) boosters.Add(c);
            foreach (var b in boosters)
            {
                foreach (var pr in _rocket.firstStagePlumes) if (pr != null && pr.transform.IsChildOf(b)) pr.enabled = false;
                var outward = Vector3.ProjectOnPlane(b.position - _rocket.root.position, up).normalized;
                b.SetParent(rocketAnchor, true);
                _free.Add(new FreeBody
                {
                    t = b,
                    rel = b.position - _rocket.root.position,
                    relV = outward * _rocket.diameter * 1.2f,
                    keep = 0.55f,
                    axis = Vector3.Cross(up, outward), // 繞這個軸轉正角度：頂端往外倒
                    spin = 9f,
                });
            }
            if (!_skipping) Sfx.Whoosh();
        }

        void Meco()
        {
            Stage("ui.launch.stage.staging");
            Say("ui.launch.call.meco");
            foreach (var pr in _rocket.firstStagePlumes) if (pr != null) pr.enabled = false;
            if (_trail != null) _trail.emitting = false;
            if (engineLight != null) engineLight.intensity = 0;
            if (!_skipping && roar != null) roar.volume *= 0.35f;
        }

        /// <summary>節間分離：第一節被推開一點，先一起滑行；第二節點火後才真正拉開距離。</summary>
        void StageSeparation()
        {
            Say("ui.launch.call.stagesep");
            var up = _rocket.root.up;
            _firstStageFree = _rocket.firstStage;
            _firstStageFree.SetParent(rocketAnchor, true);
            _free.Add(new FreeBody { t = _firstStageFree, rel = _firstStageFree.position - _rocket.root.position, relV = -up * 2.5f, keep = 0f, axis = _rocket.root.right, spin = 3f });
            if (!_skipping) Sfx.Whoosh();
        }

        void SecondStageIgnition()
        {
            Say("ui.launch.call.ses");
            SetUpperThrust(true);
        }

        /// <summary>
        /// 換到太空場景。天空這時已經全黑、星星也亮了（兩邊的天空一樣），
        /// 長焦鏡頭換成「同方向、同大小」的近距離位置，所以畫面看起來是同一個鏡頭。
        /// </summary>
        void GoToSpace()
        {
            _inSpace = true;
            // 換場前先記下鏡頭相對上節的方向
            var dir = camLong != null ? (camLong.transform.position - _rocket.upperStage.position).normalized : -_rocket.root.forward;
            float dist = _frameUpper * 0.5f / Mathf.Tan(TransferFov * 0.5f * Mathf.Deg2Rad);
            _transferOffset = dir * dist;

            // 火箭等一下會瞬間移動 2 萬公尺：尾跡要先清掉，否則會畫出一條橫跨天空的直線
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); _trail.enabled = false; }
            foreach (var fb in _free) if (fb.t != null) fb.t.gameObject.SetActive(false);
            _free.Clear();
            _firstStageFree = null;
            if (padSet != null) padSet.SetActive(false);
            foreach (var ps in padSmoke) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ApplyDarkness(1f);
            if (spaceSet != null) spaceSet.SetActive(true);
            // EnvironmentOverride 會換上原本的星空（全亮）：改回執行期複本，從 0 慢慢亮起來
            if (_starsInst != null) RenderSettings.skybox = _starsInst;
            _starsT = 0f;

            // 世界方向不變；上節放在原本太空鏡頭調好的位置（root 在錨點、俯仰 80° 時的上節位置）
            var rot = _rocket.root.rotation;
            _rocket.root.SetParent(spaceAnchor, false);
            _rocket.root.rotation = rot;
            _upperPos = spaceAnchor.TransformPoint(Quaternion.Euler(80f, 0, 0) * new Vector3(0, _firstLen, 0));
            _rocket.root.position = _upperPos - rot * new Vector3(0, _firstLen, 0);
            if (engineLight != null) engineLight.intensity = 0;
            // 火箭瞬間移動了約 2 萬公尺：所有鏡頭的阻尼狀態都要作廢，否則鏡頭會從發射台一路「飛」上來
            foreach (var cam in new[] { camLong, camSpace, camSide, camDeploy })
                if (cam != null) cam.PreviousStateIsValid = false;
            UpdateLongCam();
            if (!_skipping && countdown != null) countdown.Stop();
        }

        void SetUpperThrust(bool on)
        {
            foreach (var pr in _rocket.upperPlumes) pr.enabled = on;
        }

        void OpenFairing()
        {
            Stage("ui.launch.stage.fairing");
            Say("ui.launch.call.fairing");
            _fairingOpen = true;
            _fairingT = _T;
            if (!_skipping) Sfx.Whoosh();
        }

        void StartFall()
        {
            _falling = true;
            _fallT = _T;
            _fallAlt = LaunchFeed.AltitudeKm;
            _fallVel = LaunchFeed.VelocityKms;
            SetUpperThrust(false);

            // 以上節的幾何中心當翻滾支點；繞 root 底部轉的話上節會甩出一個 40 m 的大弧，鏡頭追不上
            Bounds b = default; bool any = false;
            foreach (var r in _rocket.upperStage.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            var center = any ? b.center : _rocket.upperStage.position;
            _fallPivotLocal = _rocket.root.InverseTransformPoint(center);

            // 鏡頭改追上節中心的固定點，並在三秒內往後拉，讓整節翻滾都留在畫面裡、地球一直在下方
            if (camSide != null)
            {
                _fallFocus = new GameObject("FallFocus").transform;
                _fallFocus.SetParent(spaceAnchor, false);
                _fallFocus.position = center;
                camSide.Target.TrackingTarget = _fallFocus;
                _sideFollow = camSide.GetComponent<CinemachineFollow>();
                if (_sideFollow != null) _sideOffset0 = _sideFollow.FollowOffset;
            }
        }

        void Deploy()
        {
            Stage("ui.launch.stage.deploy");
            Say("ui.launch.call.deploy");
            _deployed = true;
            _deployT = _T;
            if (_payload != null)
            {
                _payload.transform.SetParent(spaceAnchor, true);
            }
            // 硬切的同時把已經飄走的整流罩收掉：它們往外飄的路徑會穿過展開鏡頭，擋住半個畫面
            if (_fairingFree)
            {
                _rocket.fairingLeft.gameObject.SetActive(false);
                _rocket.fairingRight.gameObject.SetActive(false);
            }
            // 上節同時退到展開鏡頭後方（鏡頭在衛星後方 5.5×k）：硬切看不出瞬移，畫面就像分離已經完成
            var back = _rocket.root.up * (6f + 8f * _camK);
            _upperPos -= back;
            _rocket.root.position -= back;
            // 直接切：從側面鏡頭混合過來時，鏡頭路徑會擦過上節箭體，有一秒整個畫面被箭身擋住
            Cut(camDeploy);
            if (!_skipping) Sfx.Confirm();
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            LaunchFeed.Finished = true;
        }

        void Skip()
        {
            if (_done) return;
            _skipping = true;
            if (countdown != null) countdown.Stop();
            if (roar != null) roar.Stop();
            if (rumble != null) rumble.Stop();
            // 用小步長快轉到結尾，讓所有分離、展開都正確發生
            int guard = 0;
            while (!_done && guard++ < 4000) Step(0.02f);
            _skipping = false;
        }

        // ---------------- 每幀 ----------------

        void Update()
        {
            if (_done)
            {
                Animate(Time.deltaTime);
                return;
            }
            Step(Time.deltaTime);
        }

        void Step(float dt)
        {
            float prev = _T;
            _T += dt;
            // 倒數期間時間跟著錄音走：數字在念出來的那一刻跳（錄音停住或差太多就改回用實際時間）
            if (_audioClock && !_skipping && _T < 1f && countdown != null && countdown.isPlaying)
            {
                float at = (float)LaunchClock.TFromAudio(countdown.time, _calls, CallLead);
                if (Mathf.Abs(at - _T) < 1.5f) _T = Mathf.Max(prev, at);
            }
            LaunchFeed.T = _T;
            while (_nextCue < _cues.Count && _cues[_nextCue].t <= _T)
            {
                var c = _cues[_nextCue++];
                c.act();
            }
            Animate(_T - prev);
            UpdateTelemetry();
        }

        static float Ease(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>
        /// 整段飛行的姿態：塔邊鏡頭之後先滾轉到飛行方位，再逐漸俯仰（重力轉向），換場時約 62°，太空中轉到 80°。
        /// 最後等於「太空場景的方位 × Euler(80, 0, 0)」，跟原本調好的太空鏡頭一致。
        /// </summary>
        Quaternion FlightRotation(float t)
        {
            float roll = Ease(5.5f, 10f, t) * _rollDeg;
            float pitch = 62f * Ease(4f, SwapT, t) + 18f * Ease(SwapT, SwapT + 5f, t);
            return Quaternion.AngleAxis(pitch, _yawRight) * Quaternion.AngleAxis(roll, Vector3.up);
        }

        void Animate(float dt)
        {
            float t = _T;
            // ---- 發射台段：火箭往上加速、轉向 ----
            if (!_inSpace && !_exploded && t > 0)
            {
                _accel = t < MecoT ? Mathf.Lerp(4f, 14f, Mathf.Clamp01(t / 14f)) : t < IgnT ? 0f : 8f;
                _pathVel += _accel * dt;
                _pathAlt += _pathVel * dt;
                _rocket.root.rotation = FlightRotation(t);
                _rocket.root.position += _rocket.root.up * _pathVel * dt;
                // 分離出去的物體：只保留一部分火箭的加速度，所以慢慢落後
                foreach (var fb in _free)
                {
                    fb.relV -= _rocket.root.up * _accel * (1f - fb.keep) * dt;
                    fb.rel += fb.relV * dt;
                    fb.t.position = _rocket.root.position + fb.rel;
                    fb.t.Rotate(fb.axis, fb.spin * dt, Space.World);
                }
            }

            // ---- 推力、尾焰、火光 ----
            float throttle = t < -3f ? 0f : t < 0f ? Mathf.SmoothStep(0, 1, (t + 3f) / 1.5f) : 1f;
            if (_exploded || _inSpace || t >= MecoT) throttle = 0f;
            LaunchFeed.Throttle = _inSpace || t >= MecoT ? (UpperOn ? 1f : 0f) : throttle;
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 18f, 0.3f);
            foreach (var pr in _rocket.firstStagePlumes)
            {
                if (pr == null || !pr.enabled) continue;
                var s = pr.transform.localScale;
                float len = Mathf.Lerp(0.2f, 1f, throttle) * (1f + Mathf.Clamp01(_pathAlt / 1500f) * 1.5f);
                pr.transform.localScale = new Vector3(s.x, s.x * 9f * len * flicker, s.z);
                // 用 MaterialPropertyBlock：直接改 sharedMaterial 會在 Editor 裡改到材質資產本身
                pr.GetPropertyBlock(_mpb);
                _mpb.SetFloat(ThrottleId, throttle);
                pr.SetPropertyBlock(_mpb);
            }
            if (engineLight != null && !_inSpace && !_exploded && t < MecoT)
            {
                engineLight.transform.position = _rocket.root.position - _rocket.root.up * 2f;
                engineLight.intensity = throttle * 900f * flicker * Mathf.Max(0.3f, _rocket.height / 70f);
                engineLight.range = _rocket.height * 3f;
            }
            if (flashLight != null && flashLight.intensity > 0) flashLight.intensity = Mathf.Max(0, flashLight.intensity - dt * 300f);

            // 發射台的煙：升空後幾秒漸弱
            foreach (var ps in padSmoke)
            {
                if (ps == null || _inSpace) continue;
                var em = ps.emission;
                em.rateOverTimeMultiplier = throttle * Mathf.Clamp01(1f - (t - 6f) / 8f) * 70f;
            }

            if (!_inSpace) UpdateSky(dt);
            else FadeInStars(dt);

            // ---- 太空段 ----
            if (_inSpace)
            {
                var stageMove = Vector3.zero;
                if (!_falling)
                {
                    // 繼續俯仰到 80°，繞上節底部轉（第一節已經沒了）
                    if (UpperOn) stageMove = _rocket.root.up * UpperSpeed * dt;
                    _upperPos += stageMove;
                    var rot = FlightRotation(t);
                    _rocket.root.rotation = rot;
                    _rocket.root.position = _upperPos - rot * new Vector3(0, _firstLen, 0);
                }
                if (_fairingFree)
                {
                    // 兩半保有箭體原本的速度（只是上節還在加速，所以慢慢落後）：不然一脫離就以 30 m/s 往後飛出畫面
                    _rocket.fairingLeft.position += _fairingVelL * dt + stageMove * 0.9f;
                    _rocket.fairingRight.position += _fairingVelR * dt + stageMove * 0.9f;
                    _rocket.fairingLeft.Rotate(Vector3.forward, 14f * dt, Space.Self);
                    _rocket.fairingRight.Rotate(Vector3.forward, -14f * dt, Space.Self);
                }
                else if (_fairingOpen && t - _fairingT >= 2.5f)
                {
                    // 打開完成：兩半脫離上節（不然上節翻滾時會把它們一起甩到鏡頭前），保持往外的速度
                    _fairingFree = true;
                    float sp = _rocket.fairingRadius * 1.4f;
                    var up = _rocket.root.up;
                    _fairingVelL = Vector3.ProjectOnPlane(_rocket.fairingLeft.position - _rocket.upperStage.position, up).normalized * sp - up * sp * 0.3f;
                    _fairingVelR = Vector3.ProjectOnPlane(_rocket.fairingRight.position - _rocket.upperStage.position, up).normalized * sp - up * sp * 0.3f;
                    _rocket.fairingLeft.SetParent(spaceAnchor, true);
                    _rocket.fairingRight.SetParent(spaceAnchor, true);
                }
                else if (_fairingOpen)
                {
                    float f = Mathf.Clamp01((t - _fairingT) / 2.5f);
                    float ang = f * f * 75f;
                    _rocket.fairingLeft.localRotation = Quaternion.Euler(0, 0, ang);
                    _rocket.fairingRight.localRotation = Quaternion.Euler(0, 0, -ang);
                    // 兩半繞底部鉸鏈往外倒，同時往外飄開
                    float fr = _rocket.fairingRadius;
                    float drift = f * f * 2.5f * fr;
                    _rocket.fairingLeft.localPosition = new Vector3(-fr - drift, _rocket.fairingLeft.localPosition.y, 0);
                    _rocket.fairingRight.localPosition = new Vector3(fr + drift, _rocket.fairingRight.localPosition.y, 0);
                }
                if (_falling)
                {
                    // 速度不足：機頭慢慢往下、開始翻滾
                    float f = t - _fallT;
                    var pivot = _rocket.root.TransformPoint(_fallPivotLocal);
                    _rocket.root.RotateAround(pivot, _rocket.root.right, 9f * dt);
                    _rocket.root.position += Vector3.down * f * 2f * dt;
                    if (_fallFocus != null) _fallFocus.position = _rocket.root.TransformPoint(_fallPivotLocal);
                    if (_sideFollow != null)
                        _sideFollow.FollowOffset = _sideOffset0 * Mathf.Lerp(1f, 1.2f, Mathf.SmoothStep(0f, 1f, f / 3f));
                }
                if (_deployed && _payload != null)
                {
                    _payload.transform.position += _rocket.root.up * 1.2f * dt;
                    _payload.transform.Rotate(Vector3.up, 4f * dt, Space.Self);
                    foreach (var p in _payload.GetComponentsInChildren<SatPart>())
                        if (p.category == PartCategory.Solar)
                        {
                            float e = Mathf.SmoothStep(0.12f, 1f, Mathf.Clamp01((t - _deployT - 1f) / 3f));
                            p.transform.localScale = new Vector3(e, 1f, 1f);
                        }
                }
            }

            if (_upperFocus != null)
                _upperFocus.position = _rocket.upperStage.position + _rocket.upperStage.up * (_rocket.height - _firstLen) * 0.45f;
            UpdateLongCam();

            // ---- 鏡頭震動 ----
            float want = 0f;
            if (!_inSpace && t > -3f) want = throttle * Mathf.Lerp(2.2f, 0.6f, Mathf.Clamp01(t / 10f));
            _shake = Mathf.Max(want, _shake - dt * 1.2f);
            foreach (var cam in new[] { camWide, camEngine, camTower, camLong })
            {
                if (cam == null) continue;
                var n = cam.GetComponent<CinemachineBasicMultiChannelPerlin>();
                // 長焦變焦後視角很窄，同樣的晃動角度在畫面上會被放大：依視角比例縮小
                if (n != null) n.AmplitudeGain = _shake * (cam == camLong ? 0.35f * Mathf.Clamp01(cam.Lens.FieldOfView / Mathf.Max(0.01f, _longFov0)) : 1f);
            }
        }

        /// <summary>
        /// 長焦追蹤鏡頭的瞄準點與變焦。分離前瞄準火箭底部；分離後先把兩節都框進來，
        /// 第二節點火後轉去追上節並推近。太空段鏡頭跟著上節移動，維持換場那一格的方向與距離。
        /// </summary>
        void UpdateLongCam()
        {
            if (camLong == null || _longFocus == null) return;
            Vector3 focus;
            float frame;
            if (_inSpace)
            {
                focus = _rocket.upperStage.position;
                frame = _frameUpper;
                camLong.transform.position = focus + _transferOffset;
            }
            else if (_firstStageFree == null)
            {
                focus = _rocket.root.position;
                frame = _frameBase;
            }
            else
            {
                var tip = _rocket.root.position + _rocket.root.up * _rocket.height;
                var bottom = _firstStageFree.position;
                float w = Ease(IgnT + 0.8f, IgnT + 3.2f, _T);
                focus = Vector3.Lerp(bottom, _rocket.upperStage.position, w);
                // 瞄準點在畫面中線偏下（0.2），上方有 70% 的畫面：兩節的總長要放得下
                frame = Mathf.Lerp(Mathf.Max(_frameBase, Vector3.Distance(tip, bottom) * 1.75f), _frameUpper, w);
            }
            _longFocus.position = focus;
            float dist = Mathf.Max(1f, Vector3.Distance(camLong.transform.position, focus));
            camLong.Lens.FieldOfView = Mathf.Clamp(2f * Mathf.Atan(frame * 0.5f / dist) * Mathf.Rad2Deg, 0.05f, 90f);
            if (_longAim != null)
            {
                // 視角越窄，旋轉阻尼造成的落後在畫面上越明顯；分離之後完全不留阻尼
                float d = _inSpace ? 0f : 0.25f * Mathf.Clamp01(camLong.Lens.FieldOfView / Mathf.Max(0.01f, _longFov0)) * (1f - Ease(MecoT - 1f, SepT, _T));
                _longAim.Damping = new Vector2(d, d);
            }
        }

        /// <summary>天空隨（顯示的）高度變黑：大氣變薄、霧散掉、陽光從黃昏暖色變成太空的白光，最後星星浮現。</summary>
        void UpdateSky(float dt)
        {
            float dark = Ease(3f, 75f, LaunchFeed.AltitudeKm);
            ApplyDarkness(dark);
            // 天空幾乎全黑時換成星空，但亮度先保持 0：程序星空的星點大小是角度，長焦（視角 1～2°）下
            // 每顆星會被放大成一團模糊的光斑；真實的追蹤攝影也拍不到星星（曝光是對著明亮的火箭）。
            // 換到太空、鏡頭拉開之後才讓星星亮起來
            if (dark > 0.97f && _starsShared != null && _starsInst == null)
            {
                _starsInst = new Material(_starsShared) { name = _starsShared.name + " (runtime)" };
                _starsBright = _starsShared.GetFloat("_Brightness");
                _starsInst.SetFloat("_Brightness", 0f);
                RenderSettings.skybox = _starsInst;
            }
        }

        void FadeInStars(float dt)
        {
            if (_starsInst == null || _starsT >= 4f) return;
            _starsT += dt;
            _starsInst.SetFloat("_Brightness", _starsBright * Mathf.SmoothStep(0f, 1f, (_starsT - 0.5f) / 3.5f));
        }

        void ApplyDarkness(float dark)
        {
            float lit = (1f - dark) * (1f - dark);
            if (_skyInst != null)
            {
                _skyInst.SetFloat("_Exposure", _sky0Exposure * lit);
                _skyInst.SetFloat("_AtmosphereThickness", Mathf.Lerp(_sky0Thickness, 0.3f, dark));
            }
            RenderSettings.fogDensity = _fog0 * lit;
            if (_spaceEnv != null)
            {
                RenderSettings.ambientSkyColor = Color.Lerp(_amb0Sky, _spaceEnv.ambientSky, dark);
                RenderSettings.ambientEquatorColor = Color.Lerp(_amb0Eq, _spaceEnv.ambientEquator, dark);
                RenderSettings.ambientGroundColor = Color.Lerp(_amb0Ground, _spaceEnv.ambientGround, dark);
            }
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Slerp(_sun0Rot, _yaw * Quaternion.Euler(spaceSunEuler), dark);
                sun.color = Color.Lerp(_sun0Color, spaceSunColor, dark);
                sun.intensity = Mathf.Lerp(_sun0Intensity, spaceSunIntensity, dark);
            }
            if (_padProbe != null) _padProbe.intensity = Mathf.Lerp(_padProbe0, 0.15f, dark);
        }

        bool UpperOn => _rocket != null && _rocket.upperPlumes.Count > 0 && _rocket.upperPlumes[0].enabled;

        static readonly int ThrottleId = Shader.PropertyToID("_Throttle");
        // MaterialPropertyBlock 不能在欄位初始化時建立（Unity 不允許在建構子裡建立引擎物件），在 Awake 建
        MaterialPropertyBlock _mpb;

        // 遙測曲線（顯示用的真實量級；動畫時間有壓縮）。MECO 約 65 km、2.3 km/s（Falcon 9 的量級），
        // 滑行時速度略降，第二節點火後再加速；最後一點依結局改成目標軌道高度或次軌道頂點
        static readonly float[] TelT = { 0f, 4f, 8f, 12f, 16f, MecoT, IgnT, SwapT, FairingT, FairingT + 4.5f };
        static readonly float[] TelAlt = { 0f, 0.4f, 2.5f, 9f, 38f, 62f, 72f, 95f, 125f, 0f };
        static readonly float[] TelVel = { 0f, 0.06f, 0.24f, 0.55f, 1.5f, 2.3f, 2.2f, 2.9f, 4.2f, 7.6f };

        void UpdateTelemetry()
        {
            float t = _T;
            if (t <= 0) { LaunchFeed.AltitudeKm = 0; LaunchFeed.VelocityKms = 0; return; }
            if (_exploded) { LaunchFeed.VelocityKms = Mathf.Max(0, LaunchFeed.VelocityKms - 0.02f); return; }
            bool reaches = _outcome == LaunchOutcome.Nominal || _outcome == LaunchOutcome.LowerOrbit;
            // 失敗的飛行只到次軌道頂點（約 150 km），不能先爬到目標軌道高度再掉下來
            TelAlt[TelAlt.Length - 1] = !reaches ? 150f
                : _outcome == LaunchOutcome.LowerOrbit ? _orbitAltKm * 0.8f : Mathf.Max(160f, _orbitAltKm);
            float alt = Interp(TelT, TelAlt, t), vel = Interp(TelT, TelVel, t);
            if (_falling)
            {
                // 跟影格率無關：從開始下墜那一刻的數值往下算；水平速度還在，往下掉時速度略增
                float f = t - _fallT;
                alt = Mathf.Max(0f, _fallAlt - 1.6f * f * f);
                vel = _fallVel + 0.04f * f;
            }
            else if (!reaches && t > FairingT)
            {
                vel = Mathf.Min(vel, 6.4f);
            }
            LaunchFeed.AltitudeKm = alt;
            LaunchFeed.VelocityKms = vel;
        }

        static float Interp(float[] xs, float[] ys, float x)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i])
                {
                    float f = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
                    return Mathf.Lerp(ys[i - 1], ys[i], Mathf.SmoothStep(0, 1, f));
                }
            return ys[ys.Length - 1];
        }
    }
}
