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
        const float SpaceCutT = 16f;

        RocketParts _rocket;
        TrailRenderer _trail;
        GameObject _payload;
        Transform _firstStageFree;
        Vector3 _firstStageVel;
        float _T;
        float _pathAlt;     // 發射台段的高度（公尺）
        float _pathVel;
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
            BuildPayload(s);

            foreach (var cam in new[] { camWide, camEngine, camTower, camLong })
                if (cam != null) cam.Target.TrackingTarget = _rocket.root;
            foreach (var cam in new[] { camSpace, camSide })
                if (cam != null) cam.Target.TrackingTarget = _rocket.upperStage;
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
            ScaleCamFollow(camSpace, Mathf.Max(0.35f, k));
            ScaleCamFollow(camSide, Mathf.Max(0.35f, k));
            _camK = Mathf.Max(0.35f, k);
            ScaleCamFollow(camDeploy, _camK);

            sun.transform.rotation = Quaternion.Euler(padSunEuler);
            if (spaceSet != null) spaceSet.SetActive(false);
            if (engineLight != null) engineLight.intensity = 0;
            if (flashLight != null) flashLight.intensity = 0;
            foreach (var ps in padSmoke) if (ps != null) { var em = ps.emission; em.enabled = false; }

            BuildCues();
            _T = StartT;
            Cut(camWide);
            LaunchFeed.SkipRequested += Skip;
            if (countdown != null && countdown.clip != null)
            {
                var bank = AudioBank.Load();
                countdown.time = Mathf.Clamp((bank != null ? bank.countdownTerminalStart : 43f) + 3f, 0, countdown.clip.length - 1);
                countdown.Play();
            }
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
            _cues.Add(new Cue(SpaceCutT - 0.6f, () => { Say("ui.launch.call.meco"); }));
            _cues.Add(new Cue(SpaceCutT, CutToSpace));
            _cues.Add(new Cue(SpaceCutT + 1.2f, StageSeparation));
            _cues.Add(new Cue(SpaceCutT + 4f, () => Blend(camSide, 2.5f)));

            switch (_outcome)
            {
                case LaunchOutcome.FairingFailure:
                    _cues.Add(new Cue(24f, () => { Stage("ui.launch.stage.fairing"); Say("ui.launch.call.fairing_fail"); Sfx.Alert(); }));
                    _cues.Add(new Cue(28f, () => { Stage("ui.launch.stage.anomaly"); Say("ui.launch.call.underspeed"); StartFall(); }));
                    _endT = 34f;
                    break;
                case LaunchOutcome.Failure: // 太重或到不了：上節燒完還是速度不足
                    _cues.Add(new Cue(24f, OpenFairing));
                    _cues.Add(new Cue(27f, () => { Stage("ui.launch.stage.anomaly"); Say("ui.launch.call.underspeed"); StartFall(); }));
                    _endT = 33f;
                    break;
                default:
                    _cues.Add(new Cue(24f, OpenFairing));
                    _cues.Add(new Cue(28.5f, () =>
                    {
                        Stage("ui.launch.stage.orbit");
                        Say(_outcome == LaunchOutcome.LowerOrbit ? "ui.launch.call.lower" : "ui.launch.call.orbit");
                        SetUpperThrust(false);
                    }));
                    _cues.Add(new Cue(30.5f, Deploy));
                    _endT = 37f;
                    break;
            }
            _cues.Add(new Cue(_endT, Finish));
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
            var at = _rocket.root.position + Vector3.up * _rocket.height * 0.5f;
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

        void CutToSpace()
        {
            _inSpace = true;
            Stage("ui.launch.stage.staging");
            // 火箭等一下會瞬間移動 2 萬公尺：尾跡要先清掉，否則會畫出一條橫跨天空的直線
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); _trail.enabled = false; }
            if (padSet != null) padSet.SetActive(false);
            if (spaceSet != null) spaceSet.SetActive(true);
            sun.transform.rotation = Quaternion.Euler(spaceSunEuler);
            sun.color = spaceSunColor;
            sun.intensity = spaceSunIntensity;
            foreach (var ps in padSmoke) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            // 整支火箭移到太空場景，機頭朝前（+Z），稍微抬頭
            _rocket.root.SetParent(spaceAnchor, false);
            _rocket.root.localPosition = Vector3.zero;
            _rocket.root.localRotation = Quaternion.Euler(80f, 0, 0);
            foreach (var pr in _rocket.firstStagePlumes) pr.enabled = false;
            if (engineLight != null) engineLight.intensity = 0;
            // 火箭瞬間移動了約 2 萬公尺：太空鏡頭的阻尼狀態要作廢，否則鏡頭會從發射台一路「飛」上來，前一秒只看得到星空
            foreach (var cam in new[] { camSpace, camSide, camDeploy })
                if (cam != null) cam.PreviousStateIsValid = false;
            Cut(camSpace);
            if (!_skipping)
            {
                if (roar != null) roar.volume *= 0.25f;
                if (countdown != null) countdown.Stop();
            }
        }

        void StageSeparation()
        {
            Say("ui.launch.call.stagesep");
            _firstStageFree = _rocket.firstStage;
            _firstStageFree.SetParent(spaceAnchor, true);
            _firstStageVel = -_rocket.root.up * 6f;
            SetUpperThrust(true);
            Sfx.Whoosh();
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
            _rocket.root.position -= _rocket.root.up * (6f + 8f * _camK);
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
            _T += dt;
            LaunchFeed.T = _T;
            while (_nextCue < _cues.Count && _cues[_nextCue].t <= _T)
            {
                var c = _cues[_nextCue++];
                c.act();
            }
            Animate(dt);
            UpdateTelemetry();
        }

        void Animate(float dt)
        {
            float t = _T;
            // ---- 發射台段：火箭往上加速 ----
            if (!_inSpace && !_exploded && t > 0)
            {
                float a = Mathf.Lerp(4f, 14f, Mathf.Clamp01(t / 14f));
                _pathVel += a * dt;
                _pathAlt += _pathVel * dt;
                var pitch = Quaternion.Euler(0, 0, -Mathf.Clamp01((t - 5f) / 10f) * 12f); // 開始轉向
                _rocket.root.localPosition = pitch * Vector3.up * _pathAlt;
                _rocket.root.localRotation = pitch;
            }

            // ---- 推力、尾焰、火光 ----
            float throttle = t < -3f ? 0f : t < 0f ? Mathf.SmoothStep(0, 1, (t + 3f) / 1.5f) : 1f;
            if (_exploded || _inSpace) throttle = 0f;
            LaunchFeed.Throttle = _inSpace ? (UpperOn ? 1f : 0f) : throttle;
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
            if (engineLight != null && !_inSpace && !_exploded)
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

            // ---- 太空段 ----
            if (_inSpace)
            {
                if (UpperOn) _rocket.root.position += _rocket.root.up * 30f * dt;
                if (_firstStageFree != null)
                {
                    _firstStageFree.position += _firstStageVel * dt;
                    _firstStageFree.Rotate(Vector3.right, 6f * dt, Space.Self);
                }
                if (_fairingFree)
                {
                    _rocket.fairingLeft.position += _fairingVelL * dt;
                    _rocket.fairingRight.position += _fairingVelR * dt;
                    _rocket.fairingLeft.Rotate(Vector3.forward, 14f * dt, Space.Self);
                    _rocket.fairingRight.Rotate(Vector3.forward, -14f * dt, Space.Self);
                }
                else if (_fairingOpen && t - _fairingT >= 2.5f)
                {
                    // 打開完成：兩半脫離上節（不然上節翻滾時會把它們一起甩到鏡頭前），保持往外的速度
                    _fairingFree = true;
                    float sp = _rocket.fairingRadius * 2.4f;
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
                    float drift = f * f * 6f * fr;
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
                        _sideFollow.FollowOffset = _sideOffset0 * Mathf.Lerp(1f, 1.8f, Mathf.SmoothStep(0f, 1f, f / 3f));
                }
                if (_deployed && _payload != null)
                {
                    float f = Mathf.Clamp01((t - _deployT) / 4f);
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

            // ---- 鏡頭震動 ----
            float want = 0f;
            if (!_inSpace && t > -3f) want = throttle * Mathf.Lerp(2.2f, 0.6f, Mathf.Clamp01(t / 10f));
            _shake = Mathf.Max(want, _shake - dt * 1.2f);
            foreach (var cam in new[] { camWide, camEngine, camTower, camLong })
            {
                if (cam == null) continue;
                var n = cam.GetComponent<CinemachineBasicMultiChannelPerlin>();
                if (n != null) n.AmplitudeGain = _shake * (cam == camLong ? 0.35f : 1f);
            }
        }

        bool UpperOn => _rocket != null && _rocket.upperPlumes.Count > 0 && _rocket.upperPlumes[0].enabled;

        static readonly int ThrottleId = Shader.PropertyToID("_Throttle");
        // MaterialPropertyBlock 不能在欄位初始化時建立（Unity 不允許在建構子裡建立引擎物件），在 Awake 建
        MaterialPropertyBlock _mpb;

        // 遙測曲線（顯示用的真實量級；動畫時間有壓縮）
        static readonly float[] TelT = { 0f, 4f, 8f, 12f, 16f, 20f, 24f, 28.5f };
        static readonly float[] TelAlt = { 0f, 0.4f, 2.5f, 9f, 68f, 95f, 120f, 0f };
        static readonly float[] TelVel = { 0f, 0.06f, 0.24f, 0.55f, 2.3f, 2.6f, 3.6f, 7.6f };

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
            else if (_outcome != LaunchOutcome.Nominal && _outcome != LaunchOutcome.LowerOrbit && t > 24f)
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
