using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using GoNoGo.Presentation;
using Unity.Cinemachine;
using UnityEngine;

namespace GoNoGo.Launch
{
    /// <summary>
    /// 發射導演：照真實的飛行時間表（<see cref="LaunchTimeline"/>，來自 launchers.json 的 flight）演出整段發射。
    /// 結果在按下發射時就由核心決定好了；時間表依火箭、軌道、結果組出事件（一節關機、節間分離、拋整流罩、
    /// 多節火箭的每一次點火與分離、滑行、再點火、衛星分離）。任務時間是真的（Falcon 9 約 12 分鐘、
    /// Delta II 的 OCO-2 約 56 分鐘），所以升空後可以 1、5、10、50 倍播放（大家共用，存在 SessionState）。
    /// <para>
    /// 爬升段是一個連續的長焦追蹤鏡頭：天空隨（遙測的）高度變黑，助推器分離、主引擎關機、節間分離、
    /// 第二節點火都在同一個鏡頭裡發生。畫面上的移動速度是真實速度乘一個比例（升空時 1，之後縮小），
    /// 長焦鏡頭才追得上。天空全黑之後換到太空場景，換的那一格構圖一模一樣；太空段火箭在畫面裡不動
    /// （姿態在換場前就轉好了），分離出去的東西往後飄走，鏡頭不會跟著晃。
    /// </para>
    /// <para>
    /// 倒數跟著錄音走：錄音真的開始播之後才開始倒數，之後任務時間直接由錄音位置換算（不設門檻），
    /// 畫面上的數字一定和念的數字一致。
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
        const float TransferFov = 50f;   // 換場那一格的視角：太窄的話鏡頭得放到幾公里外，會鑽進地球
        const float CallLead = 0.08f;    // 倒數數字從開口到聽得出來的延遲
        const float MaxSubStep = 0.25f;  // 快轉時一幀拆成幾小步，事件和分離動作才不會跳過
        const float SceneScaleMin = 0.1f; // 爬升後段畫面速度 = 真實速度 × 0.1（長焦鏡頭追得上、浮點精度夠）
        const float CamCycle = 14f;      // 太空段鏡頭輪替（真實秒）
        const float SegmentPush = 1.2f;  // 太空中分離出去的東西往後飄的速度（m/s）
        const float StackAccel = 1.5f;   // 上節燃燒時，分離物相對上節往後加速（m/s²，畫面用）

        RocketParts _rocket;
        LaunchTimeline _tl;
        TrailRenderer _trail;
        GameObject _payload;
        SatPart[] _solar = Array.Empty<SatPart>();
        Transform _firstStageFree;
        float _T;
        // 任務時間的正本用 double：月球任務會走到 T+5 天（約 43 萬秒），float 在那裡的精度只剩 0.03 秒，每幀 1/60 秒會被捨入成 0 或 2 倍
        double _Td;
        float _pathAlt;     // 發射台段飛過的距離（公尺）
        float _sceneVel;    // 發射台段畫面上的速度（m/s）
        float _accel;       // 目前畫面上的加速度：分離出去的物體依它算相對運動
        bool _inSpace, _exploded, _fairingOpen, _deployed, _falling, _skipping, _done, _skipHandled;
        float _deployT, _fairingT, _fallT;
        Vector3 _fallPivotLocal;      // 上節中心（root 的區域座標）：翻滾繞這一點，不繞火箭底部
        CinemachineFollow _sideFollow;
        bool _fairingFree;            // 整流罩打開後脫離箭體，各自往外飄
        Vector3 _fairingVelL, _fairingVelR;
        Transform _fallFocus;         // 不跟著翻滾的追蹤點
        Vector3 _sideOffset0;
        float _shake;
        float _camK;                  // 太空鏡頭距離倍率（小火箭鏡頭靠近一點）
        float _deployK;               // 展開鏡頭距離倍率：看衛星展開後多寬，不是看火箭多大
        float _payloadSpan;           // 衛星展開後的寬度（公尺，已含縮放）
        // 拋整流罩後鏡頭推近衛星：火箭是真實尺寸，Falcon 9 的整流罩 5.2 × 13 m，1 m 的衛星照火箭大小取景只剩一個點
        float _payloadSize;           // 衛星收起來時最長的一邊（公尺，已含縮放）
        float _payloadZoomK = 1f;     // 推近後的鏡頭距離 / 原本的距離（≤ 1）
        float _payloadZoom;           // 0 = 看整節上節，1 = 推近到衛星
        CinemachineFollow _spaceFollow, _sideFollowBase;
        Vector3 _spaceOff0, _sideOff0;
        readonly List<Cue> _cues = new List<Cue>();
        int _nextCue;
        LaunchOutcome _outcome;
        Design _design;
        string _reason;

        // 時間表上的幾個關鍵時間
        float _swapT, _pitchEnd, _scaleEnd, _mecoT, _sepT, _sesT;

        // 飛行方位：垂直於長焦鏡頭的視線，火箭在畫面裡由左往右轉向
        Quaternion _yaw;
        Vector3 _yawRight;
        float _rollDeg;
        float _firstLen;              // 上節底部在 root 上的高度
        Vector3 _upperPos;            // 太空段：上節底部的世界座標

        // 多節上節：目前在燒（或下一個要燒）的是第幾節；最下面還接著的節的底部高度（鏡頭瞄準用）
        int _activeSeg;
        float _stageBottom, _focusBottom;

        // 長焦鏡頭：自己算瞄準點、視角和方向。
        // 不用 Cinemachine 的 RotationComposer：它用 float 的 acos 算角度，視角縮到 0.1° 以下時，
        // 每一幀該轉的角度（約 0.001°）會被算成 0，鏡頭停住、誤差累積到約 0.02° 才一次補上，
        // 畫面上就是火箭沿著飛行方向前後抖（視角越窄抖越大；快轉時每幀都補，反而看不出來）。
        Transform _longFocus;
        Transform _upperFocus;
        float _longFov0, _frameBase, _frameUpper;
        Vector2 _longScreen = new Vector2(0f, 0.2f);
        Vector3 _transferOffset;
        // 最大動壓之後切到「望遠追蹤」：鏡頭在火箭側面約 1.7 km、略低，跟著火箭走。
        // 地面追蹤鏡頭固定不動的話，火箭往下游飛遠、姿態接近水平時，透視會讓它看起來像在往下栽
        const float ChaseFov = 2.5f, ChaseLookUp = 10f;
        float _chaseT = float.MaxValue;
        bool _chase;
        Vector3 _chaseOffset;

        // 太空段的地球：貼圖照軌道角速度轉（v / (R + h)），看得出衛星在繞地球飛
        const double EarthRadiusKm = 6371.0;
        Renderer _earth;
        MaterialPropertyBlock _earthMpb;
        float _earthDeg;
        static readonly int TexRotX = Shader.PropertyToID("_TexRotX");
        static readonly int TexRotY = Shader.PropertyToID("_TexRotY");
        static readonly int TexRotZ = Shader.PropertyToID("_TexRotZ");

        // 鏡頭切換：同一時間只有一個混合，太空段每隔一段時間換角度
        CinemachineCamera _cam;
        float _camSince, _blendDur, _camHold;

        /// <summary>分離出去的助推器、第一節（發射台段，用相對火箭的位置與速度算）、太空中丟掉的上節。</summary>
        sealed class FreeBody
        {
            public Transform t;
            public Vector3 rel, relV;
            public float keep;        // 保留火箭加速度的比例
            public Vector3 axis;
            public float spin;
            public float age;
        }
        readonly List<FreeBody> _free = new List<FreeBody>();
        readonly List<FreeBody> _drift = new List<FreeBody>();

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
        enum Sync { Off, Waiting, Locked }
        float[] _calls;
        Sync _sync;
        float _syncWait, _audio0;
        float _roar0 = 1f, _rumble0 = 0.8f;

        // 月球任務：衛星分離後的地月轉移俯瞰圖（縮時）與抵達月球的近景
        LunarTransferView _lunar;
        bool _coasting, _arrived;

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
            if (_skyInst != null) Destroy(_skyInst);
            if (_starsInst != null) Destroy(_starsInst);
        }

        void Start()
        {
            GameSession.EnsureMission();
            var m = GameSession.Mission;
            if (m.S.LastLaunch == null) { GameSession.Send(CommandType.RunTest); GameSession.Send(CommandType.Launch); } // 直接開這個場景測試時
            var launch = m.S.LastLaunch;
            _design = launch.Design;
            _outcome = launch.Outcome;
            _reason = launch.ReasonCode ?? "";

            _tl = LaunchTimeline.Build(m.Data, _design, _outcome, _reason);
            _swapT = _tl.SwapT < 1e6 ? (float)_tl.SwapT : float.MaxValue;
            _mecoT = (float)(_tl.First("meco")?.T ?? 150);
            _sepT = (float)(_tl.First("stage_sep")?.T ?? _mecoT + 3);
            _sesT = (float)(_tl.First("ses")?.T ?? _sepT + 4);
            _pitchEnd = _swapT < 1e5f ? _swapT : _sesT + 8f;
            // 最大動壓過後 10 秒切到望遠追蹤（一節關機前至少 15 秒，分離整段都在同一個鏡頭裡）
            _chaseT = Mathf.Clamp((float)(_tl.First("maxq")?.T ?? 70) + 10f, 45f, Mathf.Max(45f, Mathf.Min(_mecoT, _swapT) - 15f));
            // 畫面速度在 35 秒內縮到 0.1：火箭在畫面上的高度長得慢，長焦鏡頭才不會一直往頭頂仰
            _scaleEnd = Mathf.Clamp(_mecoT - 8f, 25f, 35f);
            _skipHandled = false; // 進場景之前就按了跳過的話，第一幀的 Update 會處理

            var kit = LaunchKit.Load();
            _rocket = RocketBuilder.Build(_design.LauncherId, kit, rocketAnchor);
            _firstLen = _rocket.upperStage.localPosition.y;
            // 整流罩沿上節的 X 軸分成兩半，而太空側面鏡頭正好在 +X 那一側：上節繞自己的軸轉 90°，
            // 兩半改成在畫面上下方向分開；衛星反向轉回來，展開鏡頭看到的方向不變
            _rocket.upperStage.localRotation = Quaternion.Euler(0f, 90f, 0f);
            _rocket.payloadMount.localRotation = Quaternion.Euler(0f, -90f, 0f);
            BuildPayload(m.Data);

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
            if (camLong != null) camLong.Lens.FieldOfView *= Mathf.Clamp(k * 1.3f, 0.4f, 1f);
            var towerAim = camTower != null ? camTower.GetComponent<CinemachineRotationComposer>() : null;
            if (towerAim != null) towerAim.TargetOffset = new Vector3(0, _rocket.height * 0.35f, 0);
            ScaleCamOffset(camTower, k);
            ScaleCamOffset(camEngine, k);
            ScaleCamFollow(camSpace, Mathf.Max(0.35f, k) * 1.7f);
            ScaleCamFollow(camSide, Mathf.Max(0.35f, k) * 1.7f);
            _camK = Mathf.Max(0.35f, k);
            // 鏡頭原本的距離約 6.9 m。衛星分離後會慢慢自轉，太陽能翼轉成直的也要整個留在畫面裡（垂直視角 38°），距離取展開寬度的 1.7 倍
            var deployFollow = camDeploy != null ? camDeploy.GetComponent<CinemachineFollow>() : null;
            float deployDist = deployFollow != null ? deployFollow.FollowOffset.magnitude : 6.9f;
            _deployK = Mathf.Max(_camK, _payloadSpan * 1.7f / Mathf.Max(0.1f, deployDist));
            ScaleCamFollow(camDeploy, _deployK);

            SetupFlightAzimuth();
            SetupPayloadZoom();
            SetupLongCam();
            var earthT = spaceSet != null ? spaceSet.transform.Find("Earth") : null;
            _earth = earthT != null ? earthT.GetComponent<Renderer>() : null;
            _earthMpb = new MaterialPropertyBlock();

            sun.transform.rotation = Quaternion.Euler(padSunEuler);
            if (spaceSet != null) spaceSet.SetActive(false);
            if (engineLight != null) engineLight.intensity = 0;
            if (flashLight != null) flashLight.intensity = 0;
            foreach (var ps in padSmoke) if (ps != null) { var em = ps.emission; em.enabled = false; }
            SetupSky();
            SetupLunar();

            BuildCues();
            _T = StartT;
            _Td = StartT;
            LaunchFeed.T = _T;
            Cut(camWide);
            if (roar != null) _roar0 = roar.volume;
            if (rumble != null) _rumble0 = rumble.volume;
            SetupCountdownAudio();
        }

        /// <summary>
        /// 倒數錄音：等音檔真的載入、真的開始播，才開始倒數（瀏覽器解碼或載入慢的時候，數字不會先跑掉）。
        /// </summary>
        void SetupCountdownAudio()
        {
            _sync = Sync.Off;
            if (countdown == null || countdown.clip == null) return;
            var bank = AudioBank.Load();
            _calls = bank != null && bank.countdownCalls != null && bank.countdownCalls.Length >= 2 ? bank.countdownCalls : null;
            // 從 T−12 對應的錄音位置開始播：念「十」的那一刻剛好是 T−10
            _audio0 = Mathf.Clamp(_calls != null ? (float)LaunchClock.AudioFromT(StartT, _calls, CallLead) : 42.5f, 0, countdown.clip.length - 1);
            if (countdown.clip.loadState != AudioDataLoadState.Loaded) countdown.clip.LoadAudioData();
            _sync = Sync.Waiting;
            _syncWait = 0;
        }

        /// <summary>還在等錄音開始的話回傳 true（時鐘停在 T−12）。</summary>
        bool WaitForCountdownAudio(float realDt)
        {
            if (_sync != Sync.Waiting) return false;
            _syncWait += realDt;
            var clip = countdown.clip;
            if (!countdown.isPlaying && clip.loadState == AudioDataLoadState.Loaded)
            {
                countdown.time = _audio0;
                countdown.Play();
            }
            if (countdown.isPlaying && countdown.time > _audio0 + 0.01f)
            {
                _sync = _calls != null ? Sync.Locked : Sync.Off;
                return false;
            }
            // 三秒都沒聲音（沒有音效裝置、載入失敗）：不等了，照實際時間倒數
            if (_syncWait > 3f || clip.loadState == AudioDataLoadState.Failed)
            {
                _sync = Sync.Off;
                return false;
            }
            return true;
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
            // 構圖沿用場景裡設定的位置（火箭底部在畫面中央偏下），方向改由 UpdateLongCam 自己算
            var aim = camLong.GetComponent<CinemachineRotationComposer>();
            if (aim != null) { _longScreen = aim.Composition.ScreenPosition; Destroy(aim); }
            _longFov0 = camLong.Lens.FieldOfView;
            float d0 = Vector3.Distance(camLong.transform.position, rocketAnchor.position);
            _frameBase = 2f * d0 * Mathf.Tan(_longFov0 * 0.5f * Mathf.Deg2Rad);
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

        void BuildPayload(GameData data)
        {
            _payload = SatelliteBuilder.Build(_design, data, Evaluator.EvaluateDesign(_design, data), _rocket.payloadMount);
            // 展開式太陽能翼發射時收起來，分離後才展開；機身貼片本來就貼在機身上，不動
            var solarDef = data.Part(_design.Get(PartCategory.Solar));
            bool wings = solarDef != null && solarDef.Mount == "wing";
            _solar = wings ? _payload.GetComponentsInChildren<SatPart>().Where(p => p.category == PartCategory.Solar).ToArray() : Array.Empty<SatPart>();
            float openW = Width(_payload);
            foreach (var p in _solar) p.transform.localScale = new Vector3(0.12f, 1f, 1f);
            // 縮到整流罩裝得下（Taurus 的整流罩比第一節細）。要用收起來的大小量：
            // 太陽能翼張開有 4.7–7.4 m，拿張開的寬度去塞整流罩，整顆衛星會被縮到只剩 1/3–1/8
            var rs = _payload.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            float inner = Mathf.Min(_rocket.diameter, _rocket.fairingRadius * 2f);
            float fit = Mathf.Min(1f, inner * 0.75f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)));
            // 共乘：玩家的衛星裝在整疊共乘衛星的最上面
            float baseY = _rocket.rideshare ? RideshareStack.Build(_rocket.payloadMount, _rocket.fairingRadius) : 0f;
            _payload.transform.localScale = Vector3.one * fit;
            _payload.transform.localPosition = new Vector3(0, baseY + b.size.y * 0.5f * fit, 0);
            _payloadSpan = openW * fit;
            _payloadSize = Mathf.Max(b.size.x, b.size.y, b.size.z) * fit;
        }

        /// <summary>
        /// 太空鏡頭原本照火箭高度取景（k = 火箭高度 / 70 m）。拋整流罩之後改看衛星：
        /// 推近到「衛星最長邊 × 固定倍數」的距離，大火箭裡的小衛星也看得清楚；火箭和衛星本身都維持真實比例。
        /// </summary>
        void SetupPayloadZoom()
        {
            _spaceFollow = camSpace != null ? camSpace.GetComponent<CinemachineFollow>() : null;
            _sideFollowBase = camSide != null ? camSide.GetComponent<CinemachineFollow>() : null;
            if (_spaceFollow != null) _spaceOff0 = _spaceFollow.FollowOffset;
            if (_sideFollowBase != null) _sideOff0 = _sideFollowBase.FollowOffset;
            float current = _camK * 1.7f;                              // 太空鏡頭目前的距離倍率
            float target = Mathf.Max(0.35f, _payloadSize * 0.32f);     // 衛星大約佔畫面高度的 1/6，畫面下方還看得到上節
            _payloadZoomK = Mathf.Clamp(target / Mathf.Max(0.01f, current), 0.15f, 1f);
        }

        /// <summary>拋整流罩後慢慢推近衛星（真實時間 3 秒，快轉時也看得到推近）。</summary>
        void UpdatePayloadZoom(float real)
        {
            if (_payload == null || _falling || _deployed) return;
            if (!_fairingOpen) return;
            _payloadZoom = Mathf.MoveTowards(_payloadZoom, 1f, real / 3f);
            float s = Mathf.SmoothStep(0f, 1f, _payloadZoom);
            float m = Mathf.Lerp(1f, _payloadZoomK, s);
            if (_spaceFollow != null) _spaceFollow.FollowOffset = _spaceOff0 * m;
            if (_sideFollowBase != null) _sideFollowBase.FollowOffset = _sideOff0 * m;
            // 瞄準點移到衛星中心稍微下面一點，畫面下方留一點火箭上節（共乘時是下面那一疊）
            var sat = _payload.transform.position - _rocket.root.up * (_payloadSize * 0.25f);
            _upperFocus.position = Vector3.Lerp(_upperFocus.position, sat, s);
        }

        /// <summary>失效翻滾的鏡頭要看整節上節：把推近還原。</summary>
        void ResetPayloadZoom()
        {
            if (_spaceFollow != null) _spaceFollow.FollowOffset = _spaceOff0;
            if (_sideFollowBase != null) _sideFollowBase.FollowOffset = _sideOff0;
        }

        static float Width(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Max(b.size.x, b.size.z);
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
            var list = new List<Cue>
            {
                new Cue(-10f, () => Say("ui.launch.call.go")),
                new Cue(-4.5f, () => Cut(camEngine)),
                new Cue(-3f, Ignition),
                new Cue(0f, Liftoff),
                new Cue(0.6f, () => Cut(camTower)),
                new Cue(3.5f, () => Say("ui.launch.call.tower")),
                new Cue(5.5f, () => { Blend(camLong, 1.2f); Say("ui.launch.call.pitch"); }),
            };
            foreach (var e in _tl.Events)
            {
                var ev = e;
                list.Add(new Cue((float)e.T, () => OnEvent(ev)));
            }
            if (_chaseT < Mathf.Min(_mecoT, _swapT)) list.Add(new Cue(_chaseT, StartChase));
            if (_swapT < 1e5f) list.Add(new Cue(_swapT, GoToSpace));
            // 地月轉移的最後一段：衛星接近月球時，鏡頭從俯瞰圖移到月球旁邊
            if (_lunar != null) list.Add(new Cue((float)_lunar.ApproachT(), () => { if (_coasting) Blend(_lunar.Approach, 4f, true); }));
            _cues.Clear();
            _cues.AddRange(list.OrderBy(c => c.t)); // OrderBy 是穩定排序：同時間的照加入順序
        }

        void OnEvent(TimelineEvent e)
        {
            switch (e.Id)
            {
                case "booster_cutoff": BoosterCutoff(); break;
                case "booster_sep": BoosterSeparation(); break;
                case "meco": Meco(); break;
                case "stage_sep": StageSeparation(); break;
                case "ses": _activeSeg = 0; SetUpperThrust(true); break;
                case "fairing": OpenFairing(); break;
                case "fairing_fail": if (!_skipping) Sfx.Alert(); break;
                case "upper_cutoff": case "seco": case "seco2": SetUpperThrust(false); break;
                case "upper_sep": DropSegment(); break;
                case "upper_ignite": case "restart": SetUpperThrust(true); break;
                case "deploy": Deploy(); break;
                case "lunar_coast": StartLunarCoast(); break;
                case "loi": ArriveAtMoon(); break;
                case "capture": _lunar?.SetBurn(false); if (!_skipping) Sfx.Confirm(); break;
                case "eclipse": StartCoroutine(EnterEclipse()); break;
                case "signal_lost": if (!_skipping) Sfx.Alert(); break;
                case "underspeed": StartFall(); break;
                case "anomaly": Explode(); break;
                case "end": Finish(); break;
            }
            if (!string.IsNullOrEmpty(e.Call)) Say(e.N > 0 ? e.Call + "#" + e.N : e.Call);
        }

        void Say(string key) { if (!_skipping) LaunchFeed.Say(key); }

        void Cut(CinemachineCamera cam) => Activate(cam, 0f);

        /// <summary>混合到另一個鏡頭。上一個混合還沒結束就不換（兩個混合疊在一起，畫面會被拉扯）。</summary>
        bool Blend(CinemachineCamera cam, float seconds, bool force = false)
        {
            if (!force && !_skipping && _camSince < _blendDur) return false;
            Activate(cam, seconds);
            return true;
        }

        void Activate(CinemachineCamera cam, float blend)
        {
            if (cam == null) return;
            if (_skipping) blend = 0f;
            if (brain != null)
                brain.DefaultBlend = blend <= 0f
                    ? new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f)
                    : new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blend);
            cam.Prioritize();
            _cam = cam;
            _camSince = 0f;
            _blendDur = blend;
        }

        // ---------------- 事件 ----------------

        void Ignition()
        {
            foreach (var pr in _rocket.firstStagePlumes) pr.enabled = true;
            foreach (var ps in padSmoke) if (ps != null) { var em = ps.emission; em.enabled = true; ps.Play(); }
            Say("ui.launch.call.ignition");
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
            var at = _rocket.root.position + _rocket.root.up * _rocket.height * 0.5f;
            if (fireball != null) { fireball.transform.position = at; fireball.Play(); }
            if (debris != null) { debris.transform.position = at; debris.Play(); }
            if (flashLight != null) { flashLight.transform.position = at; flashLight.intensity = 400f; flashLight.range = _rocket.height * 6f; }
            foreach (var r in _rocket.root.GetComponentsInChildren<Renderer>()) r.enabled = false;
            if (_trail != null) { _trail.emitting = false; _trail.enabled = true; }
            if (engineLight != null) engineLight.intensity = 0;
            if (roar != null) roar.Stop();
            _shake = 3f;
            if (!_skipping) Sfx.Stamp();
        }

        List<Transform> Boosters()
        {
            var list = new List<Transform>();
            if (_rocket.firstStage == null) return list;
            foreach (Transform c in _rocket.firstStage) if (c.name.StartsWith("Booster")) list.Add(c);
            return list;
        }

        /// <summary>助推器燃燒完畢（Delta II 的 GEM-40、Falcon Heavy 的側核心）：只有它們的尾焰熄掉。</summary>
        void BoosterCutoff()
        {
            foreach (var b in Boosters())
                foreach (var pr in _rocket.firstStagePlumes)
                    if (pr != null && pr.transform.IsChildOf(b)) pr.enabled = false;
        }

        /// <summary>助推器分離：往外推開、頂端往外倒，慢慢落到後面（長焦鏡頭裡看得到整個過程）。</summary>
        void BoosterSeparation()
        {
            BoosterCutoff();
            var up = _rocket.root.up;
            foreach (var b in Boosters())
            {
                var outward = Vector3.ProjectOnPlane(b.position - _rocket.root.position, up).normalized;
                b.SetParent(rocketAnchor, true);
                _free.Add(new FreeBody
                {
                    t = b,
                    rel = b.position - _rocket.root.position,
                    relV = outward * _rocket.diameter * 1.2f,
                    keep = 0.4f,
                    axis = Vector3.Cross(up, outward),
                    spin = 9f,
                });
            }
            if (!_skipping) Sfx.Whoosh();
        }

        void Meco()
        {
            foreach (var pr in _rocket.firstStagePlumes) if (pr != null) pr.enabled = false;
            if (_trail != null) _trail.emitting = false;
            if (engineLight != null) engineLight.intensity = 0;
        }

        /// <summary>節間分離：第一節被推開一點，先一起滑行；第二節點火後才真正拉開距離。</summary>
        void StageSeparation()
        {
            var up = _rocket.root.up;
            _firstStageFree = _rocket.firstStage;
            _firstStageFree.SetParent(rocketAnchor, true);
            _free.Add(new FreeBody { t = _firstStageFree, rel = _firstStageFree.position - _rocket.root.position, relV = -up * 2.5f, keep = 0f, axis = _rocket.root.right, spin = 3f });
            if (!_skipping) Sfx.Whoosh();
        }

        /// <summary>多節火箭丟掉燒完的那一節（Taurus 的第 1、2 節、Electron 的第二節）。</summary>
        void DropSegment()
        {
            if (_activeSeg >= _rocket.upperSegments.Count) return;
            var seg = _rocket.upperSegments[_activeSeg];
            _rocket.segmentPlumes[_activeSeg].enabled = false;
            var up = _rocket.root.up;
            if (_inSpace)
            {
                seg.SetParent(spaceAnchor, true);
                _drift.Add(new FreeBody { t = seg, relV = -up * SegmentPush + _rocket.root.right * 0.15f, axis = _rocket.root.right, spin = 4f });
                // 分離是看點：換到側面鏡頭（上一個混合還沒結束就不換）
                if (!_deployed && !_falling && _cam != camSide && Blend(camSide, 2.5f)) _camHold = 8f;
            }
            else
            {
                seg.SetParent(rocketAnchor, true);
                _free.Add(new FreeBody { t = seg, rel = seg.position - _rocket.root.position, relV = -up * 2f, keep = 0f, axis = _rocket.root.right, spin = 3f });
            }
            _activeSeg++;
            _stageBottom = _activeSeg < _rocket.segmentBase.Count ? _rocket.segmentBase[_activeSeg] : 0f;
            if (!_skipping) Sfx.Whoosh();
        }

        /// <summary>
        /// 換到太空場景。天空這時已經全黑（兩邊的天空一樣），姿態也已經轉到 80°，
        /// 長焦鏡頭換成「同方向、同大小」的近距離位置，所以畫面看起來是同一個鏡頭。
        /// </summary>
        void GoToSpace()
        {
            _inSpace = true;
            var dir = camLong != null ? (camLong.transform.position - _rocket.upperStage.position).normalized : -_rocket.root.forward;
            float dist = _frameUpper * 0.5f / Mathf.Tan(TransferFov * 0.5f * Mathf.Deg2Rad);
            _transferOffset = dir * dist;

            // 火箭等一下會瞬間移動上萬公尺：尾跡要先清掉，否則會畫出一條橫跨天空的直線
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); _trail.enabled = false; }
            foreach (var fb in _free) if (fb.t != null) fb.t.gameObject.SetActive(false);
            _free.Clear();
            _firstStageFree = null;
            if (padSet != null) padSet.SetActive(false);
            foreach (var ps in padSmoke) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ApplyDarkness(1f);
            if (spaceSet != null) spaceSet.SetActive(true);
            if (_starsInst != null) RenderSettings.skybox = _starsInst;
            _starsT = 0f;

            // 世界方向不變（換場前已經轉到 80°）；上節放在原本太空鏡頭調好的位置
            var rot = FlightRotation(_pitchEnd);
            _rocket.root.SetParent(spaceAnchor, false);
            _rocket.root.rotation = rot;
            _upperPos = spaceAnchor.TransformPoint(Quaternion.Euler(80f, 0, 0) * new Vector3(0, _firstLen, 0));
            _rocket.root.position = _upperPos - rot * new Vector3(0, _firstLen, 0);
            if (engineLight != null) engineLight.intensity = 0;
            foreach (var cam in new[] { camLong, camSpace, camSide, camDeploy })
                if (cam != null) cam.PreviousStateIsValid = false;
            UpdateLongCam();
            if (countdown != null) countdown.Stop();
            if (roar != null) roar.Stop();
            if (rumble != null) rumble.Stop();
            // 長焦鏡頭在換場那一格接手，停 0.6 秒再慢慢移到上節後方
            _cam = camLong;
            _camSince = 0f;
            _blendDur = 0f;
            _camHold = 0.6f;
        }

        /// <summary>
        /// 切到望遠追蹤：鏡頭放到火箭飛行方向的側面（原本地面鏡頭那一側）、略低，之後跟著瞄準點平移。
        /// 視線和飛行方向垂直，畫面上看到的就是火箭真正的姿態：往右上爬升、慢慢轉平。
        /// </summary>
        void StartChase()
        {
            if (camLong == null || _exploded || _inSpace) return;
            var heading = _yaw * Vector3.forward;
            var side = Vector3.ProjectOnPlane(camLong.transform.position - _longFocus.position, Vector3.up);
            side = Vector3.ProjectOnPlane(side, heading);
            side = side.sqrMagnitude > 1f ? side.normalized : -_yawRight;
            float dist = Mathf.Max(300f, _frameBase * 0.5f / Mathf.Tan(ChaseFov * 0.5f * Mathf.Deg2Rad));
            float up = ChaseLookUp * Mathf.Deg2Rad;
            _chaseOffset = side * dist * Mathf.Cos(up) - Vector3.up * dist * Mathf.Sin(up);
            _chase = true;
            camLong.PreviousStateIsValid = false;
            UpdateLongCam();
        }

        void SetUpperThrust(bool on)
        {
            bool final = _activeSeg >= _rocket.upperSegments.Count;
            for (int i = 0; i < _rocket.segmentPlumes.Count; i++) _rocket.segmentPlumes[i].enabled = on && i == _activeSeg;
            foreach (var pr in _rocket.upperPlumes) pr.enabled = on && final;
        }

        bool UpperOn
        {
            get
            {
                if (_rocket == null) return false;
                foreach (var pr in _rocket.segmentPlumes) if (pr.enabled) return true;
                foreach (var pr in _rocket.upperPlumes) if (pr.enabled) return true;
                return false;
            }
        }

        void OpenFairing()
        {
            _fairingOpen = true;
            _fairingT = _T;
            if (_inSpace && !_deployed && !_falling && _cam != camSide && Blend(camSide, 2.5f)) _camHold = 8f;
            if (!_skipping) Sfx.Whoosh();
        }

        void StartFall()
        {
            _falling = true;
            _fallT = _T;
            SetUpperThrust(false);

            Bounds b = default; bool any = false;
            foreach (var r in _rocket.upperStage.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            var center = any ? b.center : _rocket.upperStage.position;
            _fallPivotLocal = _rocket.root.InverseTransformPoint(center);

            // 鏡頭改追上節中心的固定點，並往後拉，讓整節翻滾都留在畫面裡、地球一直在下方
            ResetPayloadZoom();
            if (camSide != null)
            {
                _fallFocus = new GameObject("FallFocus").transform;
                _fallFocus.SetParent(spaceAnchor, false);
                _fallFocus.position = center;
                camSide.Target.TrackingTarget = _fallFocus;
                _sideFollow = camSide.GetComponent<CinemachineFollow>();
                if (_sideFollow != null) _sideOffset0 = _sideFollow.FollowOffset;
                if (_cam != camSide) Blend(camSide, 2f, true);
            }
        }

        /// <summary>入軌後失效：太陽慢慢被地球擋住（地影）。</summary>
        System.Collections.IEnumerator EnterEclipse()
        {
            float i0 = sun.intensity;
            for (float t = 0; t < 2.5f; t += Time.deltaTime)
            {
                sun.intensity = Mathf.Lerp(i0, 0.02f, t / 2.5f);
                yield return null;
            }
            sun.intensity = 0.02f;
        }

        void Deploy()
        {
            _deployed = true;
            _deployT = _T;
            if (_payload != null) _payload.transform.SetParent(spaceAnchor, true);
            // 已經飄走的整流罩和丟掉的節收掉：它們的路徑會穿過展開鏡頭
            if (_fairingFree)
            {
                _rocket.fairingLeft.gameObject.SetActive(false);
                _rocket.fairingRight.gameObject.SetActive(false);
            }
            foreach (var d in _drift) if (d.t != null) d.t.gameObject.SetActive(false);
            _drift.Clear();
            // 上節同時退到展開鏡頭後方：硬切看不出瞬移，畫面就像分離已經完成
            var back = _rocket.root.up * (6f + 8f * _deployK);
            _upperPos -= back;
            _rocket.root.position -= back;
            Cut(camDeploy);
            if (!_skipping) Sfx.Confirm();
        }

        /// <summary>月球任務才建：地月轉移俯瞰圖（先藏起來）。地球沿用近地太空場景的材質。</summary>
        void SetupLunar()
        {
            if (_tl.Lunar == null) return;
            var kit = LaunchKit.Load();
            var atmo = spaceSet != null ? spaceSet.transform.Find("Earth_Atmosphere") : null;
            _lunar = new LunarTransferView(_tl.Lunar, kit, _earth != null ? _earth.sharedMaterial : null,
                atmo != null ? atmo.GetComponent<Renderer>()?.sharedMaterial : null, sun, _yaw);
        }

        /// <summary>
        /// 地月轉移滑行開始：近地的場景（地球、上節、衛星）收起來，切到俯瞰圖。
        /// 接下來約 5 天用縮時播完（時間表的 Warps），衛星與月球的位置都照任務時間算。
        /// </summary>
        void StartLunarCoast()
        {
            if (_lunar == null) return;
            _coasting = true;
            if (spaceSet != null)
                foreach (var n in new[] { "Earth", "Earth_Atmosphere" })
                {
                    var e = spaceSet.transform.Find(n);
                    if (e != null) e.gameObject.SetActive(false);
                }
            if (_rocket != null) _rocket.root.gameObject.SetActive(false);
            foreach (var d in _drift) if (d.t != null) d.t.gameObject.SetActive(false);
            _drift.Clear();
            if (_payload != null) _payload.SetActive(false);
            _lunar.ShowTransfer();
            _lunar.Tick(_Td);
            Cut(_lunar.Wide);
        }

        /// <summary>抵達月球：換到近景，衛星轉成引擎朝前，開始減速點火。</summary>
        void ArriveAtMoon()
        {
            if (_lunar == null) return;
            _coasting = false;
            _arrived = true;
            _lunar.ShowArrival(_payload != null ? _payload.transform : null, spaceSet != null ? spaceSet.transform : null, LaunchKit.Load(), _payloadSize);
            _lunar.SetBurn(true);
            if (camDeploy != null) camDeploy.PreviousStateIsValid = false;
            Cut(camDeploy);
            if (!_skipping) Sfx.Whoosh();
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            LaunchFeed.Finished = true;
        }

        /// <summary>跳過：用小步長快轉到結尾，讓所有分離、展開都正確發生（不播聲音、不播報）。</summary>
        void Skip()
        {
            if (_done) return;
            _skipping = true;
            _sync = Sync.Off;
            if (countdown != null) countdown.Stop();
            if (roar != null) roar.Stop();
            if (rumble != null) rumble.Stop();
            int guard = 0;
            // 縮時區間（地月轉移）一步跨一大段，其他照原本的小步長
            while (!_done && guard++ < 40000)
            {
                double rate = _tl.WarpRate(_Td);
                Step(rate > 1 ? (float)(rate * 0.25) : _T < _swapT + 5f ? 0.25f : 1f);
            }
            _skipping = false;
        }

        // ---------------- 每幀 ----------------

        void Update()
        {
            float real = Time.deltaTime;
            var s = GameSession.HasMission ? GameSession.Mission.S : null;
            if (s != null && s.LaunchSkipped && !_skipHandled)
            {
                _skipHandled = true;
                Skip();
            }
            if (_done)
            {
                Animate(real);
                UpdateCams(real);
                return;
            }
            if (WaitForCountdownAudio(Time.unscaledDeltaTime))
            {
                LaunchFeed.T = _T;
                UpdateCams(real);
                return;
            }
            // 升空後才能快轉（倒數跟著錄音）；一幀拆成小步，事件照順序發生
            float speed = _T >= 0f && s != null ? Mathf.Max(1, s.LaunchSpeed) : 1f;
            // 縮時區間裡一秒播放 = 好幾小時任務時間；一步不跨過區間邊界，進出縮時的倍率才不會算錯
            float todo = real * speed;
            while (todo > 1e-5f && !_done)
            {
                float st = Mathf.Min(todo, MaxSubStep);
                double rate = _tl.WarpRate(_Td);
                double dtm = st * rate;
                double edge = _tl.NextWarpEdge(_Td);
                if (_Td + dtm > edge)
                {
                    dtm = Math.Max(1e-4, edge - _Td);
                    st = (float)Math.Min(st, dtm / rate);
                }
                Step((float)dtm);
                todo -= Mathf.Max(st, 1e-5f);
            }
            UpdateCams(real);
            UpdateAudio();
        }

        void Step(float dt)
        {
            double prev = _Td;
            _Td += dt;
            // 倒數期間時間直接由錄音位置換算（不設門檻、只往前走）：數字一定在念出來的那一刻跳
            if (!_skipping && _calls != null && _sync != Sync.Off && _Td < 1 && countdown != null && countdown.isPlaying)
            {
                double at = LaunchClock.TFromAudio(countdown.time, _calls, CallLead);
                _Td = Math.Max(prev, at);
            }
            _T = (float)_Td;
            LaunchFeed.T = _T;
            while (_nextCue < _cues.Count && _cues[_nextCue].t <= _T)
            {
                var c = _cues[_nextCue++];
                c.act();
            }
            Animate((float)(_Td - prev));
            UpdateTelemetry();
        }

        static float Ease(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>
        /// 整段飛行的姿態：塔邊鏡頭之後先滾轉到飛行方位，再逐漸俯仰（重力轉向），換場那一刻剛好 80°，
        /// 等於「太空場景的方位 × Euler(80, 0, 0)」。換場之後不再轉（轉動中的長焦和跟拍鏡頭會晃）。
        /// </summary>
        Quaternion FlightRotation(float t)
        {
            float roll = Ease(5.5f, 10f, t) * _rollDeg;
            // 重力轉向：俯仰起步快、後面慢慢收（指數）和 smoothstep 各半；Falcon 9 一分鐘約 30°、一節關機約 75°
            float span = Mathf.Max(1f, _pitchEnd - 6f), x = Mathf.Clamp(t - 6f, 0f, span), tau = 0.6f * span;
            float expo = (1f - Mathf.Exp(-x / tau)) / (1f - Mathf.Exp(-span / tau));
            float pitch = 80f * (0.5f * Ease(6f, _pitchEnd, t) + 0.5f * expo);
            return Quaternion.AngleAxis(pitch, _yawRight) * Quaternion.AngleAxis(roll, Vector3.up);
        }

        /// <summary>畫面速度 / 真實速度：升空時 1（看得出火箭有多大），之後縮小到 0.1，長焦鏡頭才追得上。</summary>
        float SceneScale(float t) => Mathf.Lerp(1f, SceneScaleMin, Ease(4f, _scaleEnd, t));

        float Throttle(float t)
        {
            if (_exploded) return 0f;
            if (t < -3f) return 0f;
            if (t < 0f) return Mathf.SmoothStep(0, 1, (t + 3f) / 1.5f);
            return _tl.Thrusting(t) ? 1f : 0f;
        }

        void Animate(float dt)
        {
            float t = _T;
            // ---- 發射台段：火箭照真實速度（乘比例）往上飛、轉向 ----
            if (!_inSpace && !_exploded && t > 0)
            {
                float v = (float)_tl.Velocity(t) * 1000f * SceneScale(t);
                _accel = dt > 1e-5f ? Mathf.Max(0f, (v - _sceneVel) / dt) : 0f;
                _sceneVel = v;
                _pathAlt += v * dt;
                _rocket.root.rotation = FlightRotation(t);
                _rocket.root.position += _rocket.root.up * v * dt;
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
            float throttle = _inSpace ? 0f : Throttle(t);
            bool firstOn = !_inSpace && !_exploded && _firstStageFree == null && t < _mecoT + 0.01f;
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 18f, 0.3f);
            // 空氣越稀薄尾焰越寬越長
            float thin = Mathf.Clamp01(LaunchFeed.AltitudeKm / 40f);
            foreach (var pr in _rocket.firstStagePlumes)
            {
                if (pr == null || !pr.enabled) continue;
                var sc = pr.transform.localScale;
                float len = Mathf.Lerp(0.2f, 1f, firstOn ? throttle : 0f) * (1f + thin * 1.8f);
                pr.transform.localScale = new Vector3(sc.x, sc.x * 9f * len * flicker, sc.z);
                pr.GetPropertyBlock(_mpb);
                _mpb.SetFloat(ThrottleId, throttle);
                pr.SetPropertyBlock(_mpb);
            }
            if (engineLight != null && firstOn)
            {
                engineLight.transform.position = _rocket.root.position - _rocket.root.up * 2f;
                engineLight.intensity = throttle * 900f * flicker * Mathf.Max(0.3f, _rocket.height / 70f) * (1f - thin);
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

            if (!_inSpace) UpdateSky();
            else FadeInStars(dt);

            // ---- 太空段：上節在畫面裡不動，分離出去的東西往後飄、地球在下面轉 ----
            if (_inSpace)
            {
                SpinEarth(dt);
                bool burning = UpperOn;
                var up = _rocket.root.up;
                foreach (var d in _drift)
                {
                    if (d.t == null) continue;
                    if (burning) d.relV -= up * StackAccel * dt;
                    d.t.position += d.relV * dt;
                    d.t.Rotate(d.axis, d.spin * dt, Space.World);
                    d.age += dt;
                    if (d.age > 120f) d.t.gameObject.SetActive(false);
                }
                if (_fairingFree)
                {
                    if (burning) { _fairingVelL -= up * StackAccel * dt; _fairingVelR -= up * StackAccel * dt; }
                    _rocket.fairingLeft.position += _fairingVelL * dt;
                    _rocket.fairingRight.position += _fairingVelR * dt;
                    _rocket.fairingLeft.Rotate(Vector3.forward, 14f * dt, Space.Self);
                    _rocket.fairingRight.Rotate(Vector3.forward, -14f * dt, Space.Self);
                    if (t - _fairingT > 90f) { _rocket.fairingLeft.gameObject.SetActive(false); _rocket.fairingRight.gameObject.SetActive(false); }
                }
                else if (_fairingOpen && t - _fairingT >= 2.5f)
                {
                    // 打開完成：兩半脫離上節（不然上節翻滾時會把它們一起甩到鏡頭前），保持往外的速度
                    _fairingFree = true;
                    float sp = _rocket.fairingRadius * 1.4f;
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
                    _rocket.root.position += Vector3.down * Mathf.Min(f, 12f) * 1.5f * dt;
                    if (_fallFocus != null) _fallFocus.position = _rocket.root.TransformPoint(_fallPivotLocal);
                    if (_sideFollow != null)
                        _sideFollow.FollowOffset = _sideOffset0 * Mathf.Lerp(1f, 1.2f, Mathf.SmoothStep(0f, 1f, f / 3f));
                }
                if (_arrived && _lunar != null) _lunar.TickArrival(dt, _tl.Velocity(_Td));
                if (_deployed && _payload != null && !_coasting && !_arrived)
                {
                    _payload.transform.position += _rocket.root.up * 1.2f * dt;
                    _payload.transform.Rotate(Vector3.up, 4f * dt, Space.Self);
                    float e = Mathf.SmoothStep(0.12f, 1f, Mathf.Clamp01((t - _deployT - 1f) / 3f));
                    foreach (var p in _solar) if (p != null) p.transform.localScale = new Vector3(e, 1f, 1f);
                }
            }

            // ---- 鏡頭震動：只有點火到升空後 20 秒（長焦變焦之後的晃動會被放大成「火箭在抖」） ----
            float want = 0f;
            if (!_inSpace && t > -3f && t < 20f) want = throttle * Mathf.Lerp(2.2f, 0.6f, Mathf.Clamp01(t / 10f)) * Mathf.Clamp01((20f - t) / 6f);
            _shake = Mathf.Max(want, _shake - dt * 1.2f);
            foreach (var cam in new[] { camWide, camEngine, camTower, camLong })
            {
                if (cam == null) continue;
                var n = cam.GetComponent<CinemachineBasicMultiChannelPerlin>();
                if (n == null) continue;
                // 長焦的視角很窄，同樣的晃動角度在畫面上會被放大：依視角比例縮小
                n.AmplitudeGain = _inSpace ? 0f : _shake * (cam == camLong ? 0.35f * Mathf.Clamp01(cam.Lens.FieldOfView / Mathf.Max(0.01f, _longFov0)) : 1f);
            }
        }

        /// <summary>鏡頭：瞄準點、長焦變焦、太空段的鏡頭輪替（用真實時間，快轉時也不會一直切）。</summary>
        void UpdateCams(float real)
        {
            _camSince += real;
            _lunar?.Tick(_Td);
            ApplyEarth();
            if (_upperFocus != null && _rocket != null)
            {
                _focusBottom = Mathf.Lerp(_focusBottom, _stageBottom, 1f - Mathf.Exp(-real * 1.5f));
                float top = _rocket.height - _firstLen;
                _upperFocus.position = _rocket.upperStage.TransformPoint(new Vector3(0, _focusBottom + (top - _focusBottom) * 0.45f, 0));
                UpdatePayloadZoom(real);
            }
            UpdateLongCam();
            if (!_inSpace || _deployed || _falling || _skipping) return;
            // 拋整流罩後到衛星分離：只用側面鏡頭。後方鏡頭推近後是從上節屁股往前看，箭體會擋住衛星
            if (_fairingOpen && _payload != null)
            {
                if (_cam != camSide && camSide != null && Blend(camSide, 2.5f)) _camHold = CamCycle;
                return;
            }
            if (_camHold > 0f) { _camHold -= real; return; }
            if (_cam == camLong) { Blend(camSpace, 3.5f, true); _camHold = CamCycle; return; }
            // 長時間燃燒、滑行：每隔一段時間換角度（側面看得到分離、後方看得到地球）
            if (Blend(_cam == camSpace ? camSide : camSpace, 3f)) _camHold = CamCycle;
        }

        /// <summary>
        /// 長焦追蹤鏡頭的瞄準點與變焦。分離前瞄準火箭底部；分離後先把兩節都框進來，
        /// 第二節點火後轉去追上節並推近。太空段鏡頭跟著上節，維持換場那一格的方向與距離。
        /// 近剪裁面跟著距離往外推：火箭飛到幾十公里外時，深度緩衝的精度才夠（WebGL 沒有反向深度）。
        /// </summary>
        void UpdateLongCam()
        {
            if (camLong == null || _longFocus == null || _rocket == null) return;
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
                if (_chase) camLong.transform.position = focus + _chaseOffset;
            }
            else
            {
                var tip = _rocket.root.position + _rocket.root.up * _rocket.height;
                var bottom = _firstStageFree.position;
                float w = Ease(_sesT + 0.8f, _sesT + 3.2f, _T);
                focus = Vector3.Lerp(bottom, _rocket.upperStage.position, w);
                frame = Mathf.Lerp(Mathf.Max(_frameBase, Vector3.Distance(tip, bottom) * 1.75f), _frameUpper, w);
                if (_chase) camLong.transform.position = focus + _chaseOffset;
            }
            _longFocus.position = focus;
            float dist = Mathf.Max(1f, Vector3.Distance(camLong.transform.position, focus));
            var lens = camLong.Lens;
            lens.FieldOfView = Mathf.Clamp(2f * Mathf.Atan(frame * 0.5f / dist) * Mathf.Rad2Deg, 0.03f, 90f);
            if (_inSpace) { lens.NearClipPlane = 0.3f; lens.FarClipPlane = 30000f; }
            else
            {
                lens.NearClipPlane = Mathf.Lerp(1f, Mathf.Max(1f, dist * 0.25f), Ease(25f, 45f, _T));
                lens.FarClipPlane = Mathf.Max(12000f, dist * 2f);
            }
            camLong.Lens = lens;
            AimLongCam(focus, lens.FieldOfView);
        }

        /// <summary>
        /// 長焦鏡頭的方向：直接由「鏡頭 → 瞄準點」的向量算（不經過角度），再依構圖把瞄準點放到畫面中央偏下。
        /// 不做阻尼：火箭的路徑本身就是平滑的，阻尼在幀時間不穩時反而會讓落後量忽大忽小。
        /// </summary>
        void AimLongCam(Vector3 focus, float vfov)
        {
            var dir = focus - camLong.transform.position;
            if (dir.sqrMagnitude < 1e-6f) return;
            var outCam = brain != null ? brain.OutputCamera : null;
            float aspect = outCam != null ? outCam.aspect : 16f / 9f;
            float hfov = Camera.VerticalToHorizontalFieldOfView(vfov, aspect);
            // ScreenPosition：0 是中央，±0.5 是邊緣，正 Y 往下。瞄準點要在下方，鏡頭就往上抬
            camLong.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up)
                * Quaternion.Euler(-_longScreen.y * vfov, -_longScreen.x * hfov, 0f);
        }

        /// <summary>太空段的地球照軌道角速度轉：地面往飛行的反方向流過去（雲、陸地跟著動，晨昏線不動）。</summary>
        void SpinEarth(float dt)
        {
            if (_earth == null || _tl == null) return;
            double t = Math.Max(0.0, _T);
            double v = _tl.Velocity(t), h = Math.Max(0.0, _tl.Altitude(t));
            _earthDeg += (float)(v / (EarthRadiusKm + h) * (180.0 / Math.PI)) * dt;
        }

        void ApplyEarth()
        {
            if (_earth == null || !_inSpace) return;
            // 整顆地球繞「軌道法線」（太空場景的右方）轉 -θ：上方的地面往後（-前方）走。
            // 球冠的網格不動，只轉取貼圖的方向：看得到的那一塊永遠是高細分的，不會轉出邊界
            var m = _earth.transform.rotation;
            var spin = Quaternion.AngleAxis(-_earthDeg, spaceSet.transform.right);
            var tex = Matrix4x4.Rotate(Quaternion.Inverse(m) * Quaternion.Inverse(spin) * m);
            _earth.GetPropertyBlock(_earthMpb);
            _earthMpb.SetVector(TexRotX, tex.GetRow(0));
            _earthMpb.SetVector(TexRotY, tex.GetRow(1));
            _earthMpb.SetVector(TexRotZ, tex.GetRow(2));
            _earth.SetPropertyBlock(_earthMpb);
        }

        /// <summary>升空的聲音：越飛越遠越小聲（真實的追蹤攝影，一兩分鐘後就聽不到了）。</summary>
        void UpdateAudio()
        {
            if (_skipping || _inSpace) return;
            float t = _T;
            if (roar != null) roar.volume = _roar0 * (1f - Ease(30f, 100f, t)) * (_exploded ? 0f : 1f);
            if (rumble != null) rumble.volume = _rumble0 * (1f - Ease(60f, 140f, t));
        }

        /// <summary>天空隨（遙測的）高度變黑：大氣變薄、霧散掉、陽光從黃昏暖色變成太空的白光。</summary>
        void UpdateSky()
        {
            float dark = Ease(3f, 75f, LaunchFeed.AltitudeKm);
            ApplyDarkness(dark);
            // 天空幾乎全黑時換成星空，但亮度先保持 0：長焦下星點會被放大成光斑，換到太空、鏡頭拉開之後才亮
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

        static readonly int ThrottleId = Shader.PropertyToID("_Throttle");
        // MaterialPropertyBlock 不能在欄位初始化時建立（Unity 不允許在建構子裡建立引擎物件），在 Awake 建
        MaterialPropertyBlock _mpb;

        /// <summary>遙測與階段：全部來自時間表（真實的高度、速度）。</summary>
        void UpdateTelemetry()
        {
            float t = _T;
            LaunchFeed.StageKey = _tl.StageKey(t);
            LaunchFeed.Throttle = _exploded ? 0f : t < 0f ? Throttle(t) : (_tl.Thrusting(t) ? 1f : 0f);
            if (t <= 0) { LaunchFeed.AltitudeKm = 0; LaunchFeed.VelocityKms = 0; return; }
            if (_exploded) { LaunchFeed.VelocityKms = Mathf.Max(0, LaunchFeed.VelocityKms - 0.02f); return; }
            LaunchFeed.AltitudeKm = (float)_tl.Altitude(_Td);
            LaunchFeed.VelocityKms = (float)_tl.Velocity(_Td);
        }
    }
}
