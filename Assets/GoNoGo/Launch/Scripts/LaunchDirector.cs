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
    /// Delta II 的 OCO-2 約 56 分鐘），整段約 70 秒播完：倒數到離塔用原速，之後自動加速，
    /// 分離、點火、拋整流罩、衛星分離放慢，滑行最快（<see cref="LaunchPacing"/>）。玩家不能跳過、不能調速；
    /// 開發用的快轉和跳過在 Editor 選單 GoNoGo/Dev。
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
        const float TransferFov = 10f;   // 換場那一格的視角：越接近長焦（約 1°）透視越像，換場越看不出來；太窄的話鏡頭得放到幾公里外，會鑽進地球（10° 約 90 m）
        const float CallLead = 0.08f;    // 倒數數字從開口到聽得出來的延遲
        const float MaxSubStep = 0.25f;  // 快轉時一幀拆成幾小步，事件和分離動作才不會跳過
        const float SceneScaleMin = 0.1f; // 爬升後段畫面速度 = 真實速度 × 0.1（長焦鏡頭追得上、浮點精度夠）
        const float CamCycle = 14f;      // 太空段鏡頭輪替（真實秒）
        const float SegmentPush = 1.2f;  // 太空中分離出去的東西往後飄的速度（m/s）
        const float StackAccel = 1.5f;   // 上節燃燒時，分離物相對上節往後加速（m/s²，畫面用）
        float _budget = 70f;
        const float PlayBudget = 70f;    // 從 T−12 播到結束的真實秒數（不是硬性上限；長一點，分離和衛星展開才放得慢）
        const float RealTimeUntil = 8f;  // 倒數、點火、升空、離塔、開始轉向都用原速
        const float RoarGain = 0.4f;     // 引擎聲和隆隆聲照場景設定的音量再壓低（原本的音量蓋過倒數）
        const float LongFrameK = 2.3f;   // 長焦鏡頭的取景高度 = 火箭高度 × 這個倍數（上方留給倒數字幕、下方留給尾焰）
        const float WideFrameK = 2f;     // 升空後遠景跟著火箭推近：取景高度至少是火箭高度 × 這個倍數
        const float TiltFrameK = 0.42f;  // 箭體水平時，長焦取景再縮成直立時的幾倍（水平的火箭約占畫面寬度六成）
        const float LeadRoom = 0.14f;    // 構圖的前方留白：箭體越水平，瞄準點越往畫面左邊放（火箭往右飛，前方留空間）
        const float StarsLevel = 0.35f, StarsBurnK = 0.45f; // 星空亮度（相對材質設定）；引擎在燒時再暗一點，像相機跟著尾焰調曝光
        const float EndHold = 1.8f;      // 播完之後先停在衛星上幾秒，再出結果卡片
        const float ShakeRefFov = 45f;   // 地面鏡頭在這個視角時晃動照原本的幅度，視角越窄晃得越小

        /// <summary>開發用：在自動倍率上再乘幾倍（Editor 選單 GoNoGo/Dev 設定，正式遊戲一直是 1）。</summary>
        public static float DevSpeed = 1f;
        /// <summary>
        /// 衛星分離（彈簧分離環，例如 Lightband）：相對速度約 0.3–0.5 m/s，動量守恆，衛星比上節輕很多，
        /// 幾乎所有速度都在衛星上、上節只微微後退（Rocket Lab MkII Motorized Lightband 手冊 §7.22）。
        /// 上節後退速度（m/s）、衛星前進速度（m/s）、上節翻轉角速度（°/s，分離時的小擾動）。
        /// </summary>
        const float SepBackSpeed = 0.03f, SepFwdSpeed = 0.45f, SepTumble = 0.5f;
        /// <summary>衛星分離後推近衛星花的真實秒數。</summary>
        const float DeployZoomSecs = 5f;
        /// <summary>太陽能板在鏡頭推近到多少時開始展開、展開花幾個真實秒。</summary>
        const float SolarStartAt = 0.7f, SolarSecs = 3f;
        float _solarK;
        /// <summary>太空場景地表邊緣藍光的強度（材質預設 1.4）。</summary>
        const float EarthRimInSpace = 0.8f;

        RocketParts _rocket;
        LaunchTimeline _tl;
        TrailRenderer _trail;
        GameObject _payload;
        SatPart[] _solar = Array.Empty<SatPart>();
        Transform _firstStageFree;
        float _T;
        float _played;      // 倒數開始後經過的真實秒數（開發快轉時照快轉後的時間算）
        LaunchPacing _pace;
        // 任務時間的正本用 double：月球任務會走到 T+5 天（約 43 萬秒），float 在那裡的精度只剩 0.03 秒，每幀 1/60 秒會被捨入成 0 或 2 倍
        double _Td;
        float _sceneVel;    // 發射台段畫面上的速度（m/s）
        float _accel;       // 目前畫面上的加速度：分離出去的物體依它算相對運動
        float _smokeRate = -1f; // 上一次設給發射台煙霧的噴發量（-1 = 還沒設過；值沒變就不重設）
        bool _inSpace, _exploded, _fairingOpen, _deployed, _falling, _skipping, _done;
        float _deployT, _fairingT, _fallT;
        Vector3 _fallPivotLocal;      // 上節中心（root 的區域座標）：翻滾繞這一點，不繞火箭底部
        CinemachineFollow _sideFollow;
        bool _fairingFree;            // 整流罩打開後脫離箭體，各自往外飄
        Vector3 _fairingVelL, _fairingVelR;
        Transform _fallFocus;         // 不跟著翻滾的追蹤點
        Vector3 _sideOffset0;
        float _shake;
        // 地面四台鏡頭和它們的晃動元件：Start 取一次（Animate 每一小步都要設幅度）
        CinemachineCamera[] _shakeCams = Array.Empty<CinemachineCamera>();
        CinemachineBasicMultiChannelPerlin[] _shakeNoise = Array.Empty<CinemachineBasicMultiChannelPerlin>();
        float _camK;                  // 太空鏡頭距離倍率（小火箭鏡頭靠近一點）
        float _deployK;               // 展開鏡頭距離倍率：看衛星展開後多寬，不是看火箭多大
        float _payloadSpan;           // 衛星展開後的寬度（公尺，已含縮放）
        // 拋整流罩後鏡頭推近衛星：火箭是真實尺寸，Falcon 9 的整流罩 5.2 × 13 m，1 m 的衛星照火箭大小取景只剩一個點
        float _payloadSize;           // 衛星收起來時最長的一邊（公尺，已含縮放）
        float _payloadZoomK = 1f;     // 推近後的鏡頭距離 / 原本的距離（≤ 1）
        float _payloadPan = 1f;       // 拋整流罩後瞄準點往衛星移的比例（大火箭要留整節上節，移得少）
        const float UpperFrameMax = 0.5f;
        float _payloadZoom;           // 0 = 看整節上節，1 = 推近到衛星
        float _deployCam;             // 衛星分離後：0 = 分離那一刻的構圖，1 = 推近到衛星（照真實時間 DeployZoomSecs 秒）
        float _deployZoomFrom = 1f, _deployZoomTo = 1f;
        // 太空鏡頭的距離倍率（推近衛星、分離後推近）與很慢的漂移：鏡頭完全不動會像截圖
        float _sideM = 1f, _spaceM = 1f, _driftT;
        Light _earthshine;
        float _starsExp;
        // 上節點火：尾焰照真實時間從小長到全尺寸（一下子冒出全尺寸的尾焰很突兀）
        const float IgniteSecs = 0.8f;
        float _upperIgnite = 99f;
        readonly Dictionary<Renderer, Vector3> _plumeScale0 = new Dictionary<Renderer, Vector3>();
        // 換到太空場景後地球照真實時間淡入（地面場景沒有地球，換場那一格突然出現很突兀）
        const float EarthFadeSecs = 3f;
        float _earthFade, _earthExp0 = 1f, _atmoStr0 = 1.2f, _earthshine0 = 0.45f;
        Renderer _atmo;
        MaterialPropertyBlock _atmoMpb;
        // 換場時補正構圖：量換場前後上節在畫面上的位置，差多少就把太空段的構圖偏移補回去
        float _spaceScreenY;
        const float DollySecs = 3f;
        Vector3 _transferDir;
        float _transferFar, _transferNear, _transferK, _transferEase;
        Vector2 _sideScreen;
        /// <summary>換場後長焦鏡頭還在推軌：這時不換鏡頭（取景還沒對齊，混合會先拉遠再拉近）。</summary>
        bool Dollying => _cam == camLong && _inSpace && _transferK < 1f;
        const float TrailFadeSecs = 1.5f;
        float _trailFade = -1f;       // 主引擎關機後經過的真實秒數（-1 = 還沒關機）
        // 尾跡原本的顏色與透明度；淡出時每幀重複用同一個 Gradient 和陣列（不每幀配置新的）
        GradientColorKey[] _trailColor0;
        GradientAlphaKey[] _trailAlpha0, _trailAlpha;
        Gradient _trailFadeGrad;
        Vector3 _sepFocusOffset;      // 節間分離那一刻長焦鏡頭的瞄準點，相對上節底部
        // 節間分離後長焦鏡頭的瞄準點和取景從整枝火箭移到上節：分離那一刻就開始，照真實時間推（加速時照任務時間推，一秒內就縮放好幾倍）
        const float ReframeSecs = 2.5f;
        float _reframe;
        // 長焦鏡頭的取景寬度（自然對數）照真實時間平滑：每秒最多縮放約 1.5 倍，加速時也不會突然拉近拉遠
        const float FrameRate = 0.4f;
        float _frameLog = float.NaN;
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
        Vector3 _sepDir = Vector3.up;
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
        float _longFov0, _frameBase, _frameUpper, _wideFov0 = 40f, _wideZoom;
        CinemachineRotationComposer _wideAim;
        Vector2 _wideScreen0;
        Vector2 _longScreen = new Vector2(0f, 0.2f);
        // 最大動壓之後切到「望遠追蹤」：鏡頭在火箭側面約 1.7 km、略低，跟著火箭走。
        // 地面追蹤鏡頭固定不動的話，火箭往下游飛遠、姿態接近水平時，透視會讓它看起來像在往下栽
        const float ChaseFov = 2.5f, ChaseLookUp = 10f;
        float _chaseT = float.MaxValue;
        bool _chase;
        // 開始望遠追蹤時鏡頭不瞬移：照真實時間花 ChaseMoveSecs 秒從地面的位置平移到火箭側面（取景大小照算，所以只是視角慢慢轉）
        const float ChaseMoveSecs = 4f;
        Vector3 _chaseFrom;
        float _chaseK;
        Vector3 _chaseOffset;

        // 太空段的地球：貼圖照軌道角速度轉（v / (R + h)），看得出衛星在繞地球飛
        const double EarthRadiusKm = 6371.0;
        Renderer _earth;
        MaterialPropertyBlock _earthMpb;
        float _earthDeg;
        static readonly int TexRotX = Shader.PropertyToID("_TexRotX");
        static readonly int TexRotY = Shader.PropertyToID("_TexRotY");
        static readonly int TexRotZ = Shader.PropertyToID("_TexRotZ");
        static readonly int SkyRotX = Shader.PropertyToID("_SkyRotX");
        static readonly int SkyRotY = Shader.PropertyToID("_SkyRotY");
        static readonly int SkyRotZ = Shader.PropertyToID("_SkyRotZ");

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
        const float DarkRate = 0.2f;  // 天色每秒最多變多少（0 = 地面、1 = 全黑）
        float _darkWant, _darkShown;  // 照高度該有的天色、畫面上的天色
        float _darkApplied = -1f;     // 上一次套用到天空、霧、環境光、太陽的天色（-1 = 還沒套用過）
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
            if (m.S.LastLaunch == null) GameSession.Send(CommandType.Launch); // 直接開這個場景測試時
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
            _levelT = (float)(_tl.First("coast")?.T ?? double.MaxValue);
            // T+20 開始慢慢移到望遠追蹤（照真實時間花 ChaseMoveSecs 秒）：要在火箭飛遠、轉平之前移完，
            // 移到一半的位置透視最差，火箭看起來會像在往下栽（一節關機前至少 15 秒，分離整段都在同一個鏡頭裡）
            _chaseT = Mathf.Clamp(20f, 10f, Mathf.Max(10f, Mathf.Min(_mecoT, _swapT) - 15f));
            // 畫面速度在 35 秒內縮到 0.1：火箭在畫面上的高度長得慢，長焦鏡頭才不會一直往頭頂仰
            _scaleEnd = Mathf.Clamp(_mecoT - 8f, 25f, 35f);
            // 失敗的飛行：倍率照成功的飛行算，出事前看不出來
            var plan = _outcome == LaunchOutcome.Nominal ? _tl : LaunchTimeline.Build(m.Data, _design, LaunchOutcome.Nominal, "launch.nominal");
            // 月球任務多了地月轉移的縮時區間（約 30 秒），預算跟著加，近地那段才不會被壓縮
            float warpSecs = 0f;
            foreach (var w in _tl.Warps) warpSecs += (float)w.Seconds;
            _pace = new LaunchPacing(_tl, RealTimeUntil, PlayBudget + warpSecs - (RealTimeUntil - StartT), plan);
            // 實際要播的長度（失敗的飛行比預算短）：執行時的校正照這個算，不然會為了湊滿預算整段放慢
            _budget = (RealTimeUntil - StartT) + (float)_pace.PlannedTotal;

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
            if (camWide != null) _wideFov0 = camWide.Lens.FieldOfView;
            _wideAim = camWide != null ? camWide.GetComponent<CinemachineRotationComposer>() : null;
            if (_wideAim != null) _wideScreen0 = _wideAim.Composition.ScreenPosition;
            var towerAim = camTower != null ? camTower.GetComponent<CinemachineRotationComposer>() : null;
            if (towerAim != null) towerAim.TargetOffset = new Vector3(0, _rocket.height * 0.35f, 0);
            ScaleCamOffset(camTower, k);
            ScaleCamOffset(camEngine, k);
            _shakeCams = new[] { camWide, camEngine, camTower, camLong };
            _shakeNoise = new CinemachineBasicMultiChannelPerlin[_shakeCams.Length];
            for (int i = 0; i < _shakeCams.Length; i++)
                if (_shakeCams[i] != null) _shakeNoise[i] = _shakeCams[i].GetComponent<CinemachineBasicMultiChannelPerlin>();
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
            _atmoMpb = new MaterialPropertyBlock();
            var atmoT = spaceSet != null ? spaceSet.transform.Find("Earth_Atmosphere") : null;
            _atmo = atmoT != null ? atmoT.GetComponent<Renderer>() : null;
            if (_earth != null && _earth.sharedMaterial.HasProperty("_Exposure")) _earthExp0 = _earth.sharedMaterial.GetFloat("_Exposure");
            if (_atmo != null && _atmo.sharedMaterial.HasProperty("_Strength")) _atmoStr0 = _atmo.sharedMaterial.GetFloat("_Strength");

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
            if (roar != null) _roar0 = roar.volume *= RoarGain;
            if (rumble != null) _rumble0 = rumble.volume *= RoarGain;
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
            // 火箭底部放在畫面中間偏下一點（原本 0.2，尾焰會被畫面下緣切掉）
            _longScreen.y = 0.1f;
            // 取景寬度照火箭高度：火箭約占畫面高度的一半（照場景的視角取景的話，Electron 只占四分之一）
            float d0 = Vector3.Distance(camLong.transform.position, rocketAnchor.position);
            _frameBase = _rocket.height * LongFrameK;
            var lens = camLong.Lens;
            _longFov0 = lens.FieldOfView; // 場景設定的視角：鏡頭晃動照它算（視角越窄晃越小，火箭才不會被晃出畫面）
            lens.FieldOfView = 2f * Mathf.Atan(_frameBase * 0.5f / d0) * Mathf.Rad2Deg;
            camLong.Lens = lens;
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
            _payload = SatelliteBuilder.Build(_design, data, Evaluator.EvaluateDesign(_design, data, null, false), _rocket.payloadMount);
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
            // 太空鏡頭抬高往下看：700 km 的地平線在水平以下 25.7°，平視的話地球整個在畫面外。
            // 往下看約 SpaceLookDown 度，地球邊緣才會在畫面下方（後方鏡頭再多抬一點，看得到前方的地球）
            if (_spaceFollow != null) _spaceFollow.FollowOffset += Vector3.up * (_spaceFollow.FollowOffset.magnitude * Mathf.Tan((SpaceLookDown + 4f) * Mathf.Deg2Rad));
            if (_sideFollowBase != null) _sideFollowBase.FollowOffset += Vector3.up * (_sideFollowBase.FollowOffset.magnitude * Mathf.Tan(SpaceLookDown * Mathf.Deg2Rad));
            if (_spaceFollow != null) _spaceOff0 = _spaceFollow.FollowOffset;
            if (_sideFollowBase != null) _sideOff0 = _sideFollowBase.FollowOffset;
            // 側面鏡頭：火箭在畫面裡往右飛，瞄準點放在中間偏左，前方留空間
            var sideAim = camSide != null ? camSide.GetComponent<CinemachineRotationComposer>() : null;
            if (sideAim != null) { var c = sideAim.Composition; c.ScreenPosition = new Vector2(-0.1f, c.ScreenPosition.y); sideAim.Composition = c; }
            float current = _camK * 1.7f;                              // 太空鏡頭目前的距離倍率
            float target = Mathf.Max(0.35f, _payloadSize * 0.32f);     // 衛星大約佔畫面高度的 1/6，畫面下方還看得到上節
            float want = Mathf.Clamp(target / Mathf.Max(0.01f, current), 0.15f, 1f);
            // 衛星分離前整節上節都要留在畫面裡（最多佔畫面寬度的 UpperFrameMax）：大火箭的上節比衛星長很多，
            // 照衛星推近的話上節會整個出畫面，看起來像太早就只剩衛星
            float fit = 1f;
            if (camSide != null && _sideOff0.sqrMagnitude > 0f)
            {
                float hfov = Camera.VerticalToHorizontalFieldOfView(camSide.Lens.FieldOfView, 16f / 9f);
                float width = 2f * _sideOff0.magnitude * Mathf.Tan(hfov * 0.5f * Mathf.Deg2Rad);
                fit = (_rocket.height - _firstLen) / (UpperFrameMax * Mathf.Max(0.1f, width));
            }
            _payloadZoomK = Mathf.Clamp(Mathf.Max(want, fit), want, 1f);
            // 推近少了多少，瞄準點往衛星移的幅度就少多少（上節才會留在畫面中間）
            _payloadPan = want < 1f ? Mathf.Clamp01((1f - _payloadZoomK) / (1f - want)) : 1f;
        }

        /// <summary>拋整流罩後慢慢推近衛星（鏡頭混合完才開始，真實時間 4 秒，快轉時也看得到推近）。</summary>
        void UpdatePayloadZoom(float real)
        {
            if (_payload == null || _falling) return;
            if (!_fairingOpen) return;
            // 等鏡頭混合完才開始推近（兩個動作疊在一起，火箭在畫面上會先往一邊、再往另一邊）；推近照真實時間 4 秒
            if (!_deployed && _payloadZoom <= 0f && _camSince < _blendDur) return;
            if (!_deployed) _payloadZoom = Mathf.MoveTowards(_payloadZoom, 1f, real / 4f);
            float s = Mathf.SmoothStep(0f, 1f, _payloadZoom);
            float m = Mathf.Lerp(1f, _payloadZoomK, s);
            if (!_deployed) _spaceM = m;
            // 瞄準點移到衛星中心稍微下面一點，畫面下方留一點火箭上節（共乘時是下面那一疊）
            var sat = _payload.transform.position - _rocket.root.up * (_payloadSize * 0.25f);
            var focus = Vector3.Lerp(_upperFocus.position, sat, s * _payloadPan);
            if (_deployed)
            {
                // 分離後：瞄準點慢慢移到衛星中心、推近衛星（真實時間 DeployZoomSecs 秒，起步很慢，看得到兩邊分開）；
                // 上節慢慢退出畫面，鏡頭不跟著它走
                _deployCam = Mathf.MoveTowards(_deployCam, 1f, real / DeployZoomSecs);
                // 太陽能板等鏡頭推得夠近才展開（照真實時間），衛星太小的話看不到展開
                if (_deployCam >= SolarStartAt) _solarK = Mathf.MoveTowards(_solarK, 1f, real / SolarSecs);
                float d = Mathf.SmoothStep(0f, 1f, _deployCam);
                // 距離照比例變（大火箭從整節上節推到衛星要縮小十幾倍，線性的話最後一段會衝很快）
                m = Mathf.Exp(Mathf.Lerp(Mathf.Log(_deployZoomFrom), Mathf.Log(_deployZoomTo), d));
                // 瞄準點移到衛星的進度要配合推近：畫面上衛星離瞄準點的距離約和「剩下的偏移 / 鏡頭距離」成正比，
                // 照 d 線性移的話推近比較快，衛星會先被放大甩出去（往上衝再回來）。這樣算的話衛星在畫面上一路線性移到中央
                float fk = 1f - (1f - d) * (m / Mathf.Max(1e-4f, _deployZoomFrom));
                focus = Vector3.Lerp(focus, _payload.transform.position, Mathf.Clamp01(fk));
            }
            _sideM = m;
            _upperFocus.position = focus;
        }

        /// <summary>
        /// 進入停泊軌道（第一次滑行）後，上節慢慢轉成和地平線平行（真實時間 LevelSecs 秒）。
        /// 上升段機頭比水平高 10° 是對的（還在爬高）；入軌後真實的上節是順著飛行方向，之後的點火（圓軌化）也是水平的。
        /// </summary>
        void UpdateAttitude(float real)
        {
            if (!_inSpace || _falling || _rocket == null || _T < _levelT || _levelK >= 1f) return;
            float before = Mathf.SmoothStep(0f, 1f, _levelK);
            _levelK = Mathf.MoveTowards(_levelK, 1f, real / LevelSecs);
            float delta = (Mathf.SmoothStep(0f, 1f, _levelK) - before) * LevelDeg;
            // 繞上節中段轉，衛星和上節在畫面上都不會被甩開
            var pivot = _upperFocus != null ? _upperFocus.position : _rocket.upperStage.position;
            _rocket.root.RotateAround(pivot, _yawRight, delta);
        }

        const float SpaceLookDown = 16f;
        const float LevelSecs = 4f, LevelDeg = 10f; // 太空段的俯仰是 80°（FlightRotation），轉 10° 到水平
        float _levelT = float.MaxValue, _levelK;

        /// <summary>
        /// 太空鏡頭：距離倍率乘上很慢的漂移（左右約 6°、上下約 3°，週期一兩分鐘），畫面才有生命感。
        /// 失效翻滾時側面鏡頭由翻滾的程式控制，不加漂移。
        /// </summary>
        void UpdateSpaceCams(float real)
        {
            if (!_inSpace || _falling) return;
            _driftT += real;
            var up = spaceSet != null ? spaceSet.transform.up : Vector3.up;
            var right = spaceSet != null ? spaceSet.transform.right : Vector3.right;
            var drift = Quaternion.AngleAxis(6f * Mathf.Sin(_driftT * 0.09f), up) * Quaternion.AngleAxis(3f * Mathf.Sin(_driftT * 0.06f + 1f), right);
            if (_spaceFollow != null) _spaceFollow.FollowOffset = drift * (_spaceOff0 * _spaceM);
            if (_sideFollowBase != null) _sideFollowBase.FollowOffset = drift * (_sideOff0 * _sideM);
        }

        /// <summary>失效翻滾的鏡頭要看整節上節：把推近還原。</summary>
        void ResetPayloadZoom()
        {
            _sideM = _spaceM = 1f;
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
                new Cue(-5.5f, () => Blend(camEngine, 2f, true)), // 慢慢推到引擎，點火（T−3）前到位
                new Cue(-3f, Ignition),
                new Cue(0f, Liftoff),
                // 從引擎特寫慢慢拉回遠景（不硬切）。不接塔邊鏡頭：它和引擎特寫在火箭底部兩側，混合時鏡頭會鑽到火箭底下
                new Cue(0.3f, () => Blend(camWide, 2f, true)),
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
            _trailColor0 = g.colorKeys;
            _trailAlpha0 = g.alphaKeys;
            _trailAlpha = new GradientAlphaKey[_trailAlpha0.Length];
            _trailFadeGrad = new Gradient();
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
            if (!_skipping) Sfx.Explosion();
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
                    if (pr != null && pr.transform.IsChildOf(b)) CutPlume(pr);
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
        }

        void Meco()
        {
            foreach (var pr in _rocket.firstStagePlumes) CutPlume(pr);
            if (_trail != null) { _trail.emitting = false; _trailFade = 0f; }
            if (engineLight != null) engineLight.intensity = 0;
        }

        /// <summary>
        /// 主引擎關機後，留在空中的尾跡照真實時間變淡、變細（TrailFadeSecs 秒）。
        /// 不處理的話它會在火箭後方留一片暖色的煙，換到太空場景時被清掉，畫面像跳了一下。
        /// </summary>
        void FadeTrail(float real)
        {
            if (_trail == null || _trailFade < 0f || _inSpace || !_trail.enabled) return;
            _trailFade += real;
            float k = 1f - Mathf.SmoothStep(0f, 1f, _trailFade / TrailFadeSecs);
            _trail.widthMultiplier = _rocket.diameter * Mathf.Lerp(0.3f, 1f, k);
            for (int i = 0; i < _trailAlpha0.Length; i++)
            {
                _trailAlpha[i] = _trailAlpha0[i];
                _trailAlpha[i].alpha *= k;
            }
            _trailFadeGrad.SetKeys(_trailColor0, _trailAlpha);
            _trail.colorGradient = _trailFadeGrad;
            if (k <= 0f) { _trail.Clear(); _trail.enabled = false; }
        }

        /// <summary>節間分離：第一節被推開一點，先一起滑行；第二節點火後才真正拉開距離。</summary>
        void StageSeparation()
        {
            var up = _rocket.root.up;
            _sepFocusOffset = _rocket.root.position + up * (_rocket.height * 0.5f * Tilt()) - _rocket.upperStage.position;
            _firstStageFree = _rocket.firstStage;
            _firstStageFree.SetParent(rocketAnchor, true);
            _free.Add(new FreeBody { t = _firstStageFree, rel = _firstStageFree.position - _rocket.root.position, relV = -up * 2.5f, keep = 0f, axis = _rocket.root.right, spin = 3f });
        }

        /// <summary>多節火箭丟掉燒完的那一節（Taurus 的第 1、2 節、Electron 的第二節）。</summary>
        void DropSegment()
        {
            if (_activeSeg >= _rocket.upperSegments.Count) return;
            var seg = _rocket.upperSegments[_activeSeg];
            CutPlume(_rocket.segmentPlumes[_activeSeg]);
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
        }

        /// <summary>
        /// 換到太空場景。天空這時已經全黑（兩邊的天空一樣），姿態也已經轉到 80°，
        /// 長焦鏡頭換成「同方向、同大小」的近距離位置，所以畫面看起來是同一個鏡頭。
        /// </summary>
        void GoToSpace()
        {
            // 換場前上節中段在畫面上的位置（先把長焦鏡頭對齊這一步的火箭位置）
            UpdateLongCam();
            var upperMid = new Vector3(0, (_rocket.height - _firstLen) * 0.5f, 0);
            var vp0 = camLong != null ? LongViewport(_rocket.upperStage.TransformPoint(upperMid)) : Vector2.zero;
            _inSpace = true;
            var dir = camLong != null ? (camLong.transform.position - _rocket.upperStage.position).normalized : -_rocket.root.forward;
            float dist = _frameUpper * 0.5f / Mathf.Tan(TransferFov * 0.5f * Mathf.Deg2Rad);
            // 之後長焦鏡頭往前推到側面鏡頭的距離與取景（DollySecs 秒），再混合過去：兩台鏡頭差太多的話，混合中途會先拉遠再拉近
            _transferDir = dir;
            _transferFar = dist;
            _transferK = _transferEase = 0f;
            // 推軌的終點是側面鏡頭的位置：側面鏡頭沒在用時也要每一格更新，終點才不會落後
            if (camSide != null) camSide.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
            _transferNear = Mathf.Max(1f, _sideOff0.magnitude * _sideM);

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
            // 地球反照光：從下方打上來的偏藍補光，衛星和上節背光的那一面才不會全黑
            if (_earthshine == null && spaceSet != null)
            {
                var go = new GameObject("Earthshine");
                go.transform.SetParent(spaceSet.transform, false);
                go.transform.rotation = Quaternion.LookRotation(spaceSet.transform.up, spaceSet.transform.forward);
                _earthshine = go.AddComponent<Light>();
                _earthshine.type = LightType.Directional;
                _earthshine.color = new Color(0.55f, 0.7f, 1f);
                _earthshine.intensity = 0f; // 跟著地球一起淡入（ApplyEarth）
                _earthshine.shadows = LightShadows.None;
            }
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
            // 近距離的廣角和遠距離的長焦透視不同，同樣的構圖偏移，上節在畫面上的位置會差一截：量出差多少，補回去
            _spaceScreenY = 0f;
            for (int i = 0; camLong != null && i < 3; i++)
            {
                var vp1 = LongViewport(_rocket.upperStage.TransformPoint(upperMid));
                _leadX += vp0.x - vp1.x;
                _spaceScreenY += vp1.y - vp0.y;
                UpdateLongCam();
            }
            if (countdown != null) countdown.Stop();
            if (roar != null) roar.Stop();
            if (rumble != null) rumble.Stop();
            // 太空段的長焦鏡頭方向是自己算的（構圖偏移、前方留白，不是正對瞄準點）：混合到側面鏡頭時不要依瞄準點重算方向，
            // 否則混合開始那一格鏡頭會突然轉一下。升空時的遠景 → 長焦不能這樣設：兩台鏡頭差了幾公里，混合途中要靠瞄準點對準火箭
            if (camLong != null) camLong.BlendHint = CinemachineCore.BlendHints.IgnoreTarget;
            // 長焦鏡頭在換場那一格接手，先推軌到側面鏡頭的距離（DollySecs 秒），再混合到側面鏡頭
            _cam = camLong;
            _camSince = 0f;
            _blendDur = 0f;
            _camHold = DollySecs + 0.2f;
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
            _chaseFrom = camLong.transform.position;
            _chaseK = 0f;
            UpdateLongCam();
        }

        void SetUpperThrust(bool on)
        {
            if (on && !UpperOn) _upperIgnite = 0f;
            bool final = _activeSeg >= _rocket.upperSegments.Count;
            for (int i = 0; i < _rocket.segmentPlumes.Count; i++) SetPlume(_rocket.segmentPlumes[i], on && i == _activeSeg);
            foreach (var pr in _rocket.upperPlumes) SetPlume(pr, on && final);
        }

        void SetPlume(Renderer pr, bool on)
        {
            if (pr == null) return;
            if (!on && pr.enabled) CutPlume(pr);
            else pr.enabled = on;
        }

        /// <summary>
        /// 關火：尾焰不直接消失。原本的尾焰照常關掉（點火、分離的判斷都看它），
        /// 另外複製一份在原地照真實時間縮短、變暗（CutSecs 秒）再刪掉。
        /// </summary>
        void CutPlume(Renderer pr)
        {
            if (pr == null || !pr.enabled) return;
            pr.enabled = false;
            if (_skipping) return;
            var go = Instantiate(pr.gameObject, pr.transform.parent);
            go.name = pr.name + " (cutoff)";
            go.transform.localPosition = pr.transform.localPosition;
            go.transform.localRotation = pr.transform.localRotation;
            go.transform.localScale = pr.transform.localScale;
            var r = go.GetComponent<Renderer>();
            r.enabled = true;
            _cutoffs.Add(new Cutoff { r = r, scale0 = go.transform.localScale });
        }

        sealed class Cutoff { public Renderer r; public Vector3 scale0; public float t; }
        readonly List<Cutoff> _cutoffs = new List<Cutoff>();
        const float CutSecs = 0.7f;

        void UpdateCutoffs(float real)
        {
            for (int i = _cutoffs.Count - 1; i >= 0; i--)
            {
                var c = _cutoffs[i];
                c.t += real;
                if (c.r == null || c.t >= CutSecs)
                {
                    if (c.r != null) Destroy(c.r.gameObject);
                    _cutoffs.RemoveAt(i);
                    continue;
                }
                float k = 1f - Mathf.SmoothStep(0f, 1f, c.t / CutSecs);
                float w = Mathf.Lerp(0.5f, 1f, k);
                c.r.transform.localScale = new Vector3(c.scale0.x * w, c.scale0.y * Mathf.Max(0.02f, k), c.scale0.z * w);
                c.r.GetPropertyBlock(_mpb);
                _mpb.SetFloat(ThrottleId, k);
                c.r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>上節尾焰照點火後的真實時間長大：寬度從四成、長度從零長到全尺寸。</summary>
        void UpdateUpperPlumes(float real)
        {
            if (_rocket == null) return;
            _upperIgnite += real;
            float k = Mathf.SmoothStep(0f, 1f, _upperIgnite / IgniteSecs);
            // 尺寸只由 k 決定，也只有這裡會改上節尾焰的大小：k 沒變（還沒點火、已經長到全尺寸）就不必重設
            if (k == _plumeGrow) return;
            _plumeGrow = k;
            foreach (var pr in _rocket.segmentPlumes) GrowPlume(pr, k);
            foreach (var pr in _rocket.upperPlumes) GrowPlume(pr, k);
        }

        float _plumeGrow = -1f;       // 上一次套用到上節尾焰的 k（-1 = 還沒套用過）

        void GrowPlume(Renderer pr, float k)
        {
            if (pr == null) return;
            if (!_plumeScale0.TryGetValue(pr, out var s0)) _plumeScale0[pr] = s0 = pr.transform.localScale;
            float w = Mathf.Lerp(0.4f, 1f, k);
            pr.transform.localScale = new Vector3(s0.x * w, s0.y * Mathf.Max(0.02f, k), s0.z * w);
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
            if (_inSpace && !_deployed && !_falling && _cam != camSide && !Dollying && Blend(camSide, 2.5f)) _camHold = 8f;
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
            _sepDir = _rocket.root.up; // 分離方向固定（上節之後會翻轉）
            if (_payload != null) _payload.transform.SetParent(spaceAnchor, true);
            // 已經飄走的整流罩和丟掉的節收掉：它們的路徑會穿過展開鏡頭
            if (_fairingFree)
            {
                _rocket.fairingLeft.gameObject.SetActive(false);
                _rocket.fairingRight.gameObject.SetActive(false);
            }
            foreach (var d in _drift) if (d.t != null) d.t.gameObject.SetActive(false);
            _drift.Clear();
            // 留在側面鏡頭（不切到展開鏡頭）：焦點慢慢移到衛星並推近（UpdatePayloadZoom），上節自己往後退出畫面（Animate）
            _deployZoomFrom = _sideM;
            // 推近到衛星約占畫面高度的 40%；展開式太陽能板展開後也要放得下：衛星會慢慢自轉，翼板轉成直的也要在畫面裡（約占畫面高度的 75%）
            if (camSide != null && _sideOff0.sqrMagnitude > 0)
            {
                float tanV = Mathf.Tan(camSide.Lens.FieldOfView * 0.5f * Mathf.Deg2Rad);
                float dist = Mathf.Max(_payloadSize / (0.4f * 2f * tanV), _payloadSpan / (0.75f * 2f * tanV));
                _deployZoomTo = Mathf.Clamp(dist / _sideOff0.magnitude, 0.05f, 1.5f);
            }
            if (_cam != camSide) Blend(camSide, 3f, true);
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

        /// <summary>
        /// 抵達月球：先淡出到黑再換到近景（俯瞰圖和近景是兩個不同的場景，鏡頭混合接不起來，直接切會很突然），
        /// 衛星轉成引擎朝前，開始減速點火。
        /// </summary>
        void ArriveAtMoon()
        {
            if (_lunar == null) return;
            if (_skipping || SceneFlow.FadeOut == null) { ShowMoonArrival(); return; }
            SceneFlow.FadeOut(MoonFadeSecs, () =>
            {
                ShowMoonArrival();
                SceneFlow.FadeIn?.Invoke(MoonFadeSecs * 1.5f);
            });
        }

        const float MoonFadeSecs = 0.4f;

        void ShowMoonArrival()
        {
            if (_lunar == null || _arrived) return;
            _coasting = false;
            _arrived = true;
            _lunar.ShowArrival(_payload != null ? _payload.transform : null, spaceSet != null ? spaceSet.transform : null, LaunchKit.Load(), _payloadSize);
            _lunar.SetBurn(true);
            if (camDeploy != null) camDeploy.PreviousStateIsValid = false;
            Cut(camDeploy); // 淡入淡出本身就是轉場，不加音效
        }

        void Finish()
        {
            if (_done) return;
            _done = true;
            LaunchFeed.Speed = 1f;
            // 先停在衛星上一下（動畫照真實時間繼續），再出結果卡片；開發用的跳過不等
            if (_skipping) LaunchFeed.Finished = true;
            else StartCoroutine(FinishAfterHold());
        }

        System.Collections.IEnumerator FinishAfterHold()
        {
            yield return new WaitForSeconds(EndHold);
            LaunchFeed.Finished = true;
        }

        /// <summary>開發用的跳過：用小步長快轉到結尾，讓所有分離、展開都正確發生（不播聲音、不播報）。</summary>
        public void Skip()
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
            if (_deployed) _solarK = _deployCam = 1f; // 跳到結尾：推近和展開都直接做完
            _skipping = false;
        }

        // ---------------- 每幀 ----------------

        void Update()
        {
            float real = Time.deltaTime;
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
            // 一幀拆成小步，事件照順序發生；縮時區間（地月轉移）的倍率也在 LaunchPacing 裡
            float dev = Mathf.Max(1f, DevSpeed);
            _played += real * dev;
            float speed = dev * (float)_pace.Speed(_Td, _budget - _played);
            LaunchFeed.Speed = speed;
            // 縮時區間裡一秒播放 = 好幾小時任務時間：步長跟著倍率放大，而且一步不跨過區間邊界
            double todo = real * speed;
            while (todo > 1e-5 && !_done)
            {
                double rate = _tl.WarpRate(_Td);
                double st = Math.Min(todo, MaxSubStep * rate);
                double edge = _tl.NextWarpEdge(_Td);
                if (_Td + st > edge) st = Math.Max(1e-4, edge - _Td);
                Step((float)st);
                todo -= st;
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
        /// 整段飛行的姿態：離塔後先滾轉到飛行方位，再逐漸俯仰（重力轉向），換場那一刻剛好 80°，
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

            if (!_inSpace)
            {
                // 發射台的煙：升空後幾秒漸弱
                float smokeRate = throttle * Mathf.Clamp01(1f - (t - 6f) / 8f) * 70f;
                if (smokeRate != _smokeRate)
                {
                    _smokeRate = smokeRate;
                    foreach (var ps in padSmoke)
                    {
                        if (ps == null) continue;
                        var em = ps.emission;
                        em.rateOverTimeMultiplier = smokeRate;
                    }
                }
                UpdateSky();
            }

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
                if (_deployed && !_coasting && !_arrived)
                {
                    // 分離彈簧推開後兩邊等速飄開（不會越退越快），上節順便慢慢翻轉；
                    // 推開的速度在 1 秒內加上去，不是一分離就突然動
                    float push = Mathf.SmoothStep(0f, 1f, (t - _deployT) / 1f);
                    var back = _sepDir * (SepBackSpeed * push * dt);
                    _upperPos -= back;
                    _rocket.root.position -= back;
                    _rocket.root.RotateAround(_rocket.upperStage.position, _rocket.root.right, SepTumble * push * dt);
                }
                if (_arrived && _lunar != null) _lunar.TickArrival(dt, _tl.Velocity(_Td));
                if (_deployed && _payload != null && !_coasting && !_arrived)
                {
                    _payload.transform.position += _sepDir * (SepFwdSpeed * Mathf.SmoothStep(0f, 1f, (t - _deployT) / 1f) * dt);
                    _payload.transform.Rotate(Vector3.up, 4f * dt, Space.Self);
                    float e = Mathf.SmoothStep(0.12f, 1f, _solarK);
                    foreach (var p in _solar) if (p != null) p.transform.localScale = new Vector3(e, 1f, 1f);
                }
            }

            // ---- 鏡頭震動：只有點火到升空後 20 秒（長焦變焦之後的晃動會被放大成「火箭在抖」） ----
            float want = 0f;
            if (!_inSpace && t > -3f && t < 20f) want = throttle * Mathf.Lerp(1.6f, 0.5f, Mathf.Clamp01(t / 10f)) * Mathf.Clamp01((20f - t) / 6f);
            _shake = Mathf.Max(want, _shake - dt * 1.2f);
            for (int i = 0; i < _shakeCams.Length; i++)
            {
                var cam = _shakeCams[i];
                var n = _shakeNoise[i];
                if (cam == null || n == null) continue;
                // 晃動的幅度照視角比例縮小：視角越窄（推近、長焦），同樣的角度在畫面上越大，火箭會被晃出畫面
                float k = cam == camLong ? 0.35f * Mathf.Clamp01(cam.Lens.FieldOfView / Mathf.Max(0.01f, _longFov0))
                        : Mathf.Clamp01(cam.Lens.FieldOfView / ShakeRefFov);
                n.AmplitudeGain = _inSpace ? 0f : _shake * k;
            }
        }

        /// <summary>鏡頭：瞄準點、長焦變焦、太空段的鏡頭輪替（用真實時間，快轉時也不會一直切）。</summary>
        void UpdateCams(float real)
        {
            _camSince += real;
            _lunar?.Tick(_Td);
            if (_inSpace) FadeInStars(real);
            else UpdateDarkness(real);
            UpdateSpaceCams(real);
            UpdateAttitude(real);
            UpdateUpperPlumes(real);
            UpdateCutoffs(real);
            FadeTrail(real);
            if (_firstStageFree != null) _reframe = Mathf.MoveTowards(_reframe, 1f, real / ReframeSecs);
            ApplyEarth();
            if (_upperFocus != null && _rocket != null)
            {
                _focusBottom = Mathf.Lerp(_focusBottom, _stageBottom, 1f - Mathf.Exp(-real * 1.5f));
                float top = _rocket.height - _firstLen;
                _upperFocus.position = _rocket.upperStage.TransformPoint(new Vector3(0, _focusBottom + (top - _focusBottom) * 0.45f, 0));
                UpdatePayloadZoom(real);
            }
            UpdateLongCam(real);
            UpdateWideZoom(real);
            if (!_inSpace || _deployed || _falling || _skipping) return;
            // 拋整流罩後到衛星分離：只用側面鏡頭。後方鏡頭推近後是從上節屁股往前看，箭體會擋住衛星
            if (_fairingOpen && _payload != null && !Dollying)
            {
                if (_cam != camSide && camSide != null && Blend(camSide, 2.5f)) _camHold = CamCycle;
                return;
            }
            if (_camHold > 0f) { _camHold -= real; return; }
            // 換場後直接混合到側面鏡頭（拋整流罩、衛星分離都用它）：先到後方鏡頭再轉去側面，兩段混合接在一起，火箭在畫面上會來回晃
            if (_cam == camLong) { Blend(camSide, 1f, true); _camHold = CamCycle; return; }
            // 長時間燃燒、滑行：每隔一段時間換角度（側面看得到分離、後方看得到地球）
            if (Blend(_cam == camSpace ? camSide : camSpace, 3f)) _camHold = CamCycle;
        }

        /// <summary>
        /// 遠景鏡頭不動，升空後火箭越飛越遠：視角照真實時間慢慢收窄到「火箭高度 × WideFrameK」，
        /// 火箭在畫面裡不會越來越小。倒數時維持場景設定的視角（整座發射台都在畫面裡）。
        /// </summary>
        void UpdateWideZoom(float real)
        {
            if (camWide == null || _rocket == null || _inSpace) return;
            if (_T > 0f) _wideZoom = Mathf.MoveTowards(_wideZoom, 1f, real / 2.5f);
            var center = _rocket.root.position + _rocket.root.up * (_rocket.height * 0.5f);
            float dist = Mathf.Max(1f, Vector3.Distance(camWide.transform.position, center));
            float fit = Mathf.Clamp(2f * Mathf.Atan(_rocket.height * WideFrameK * 0.5f / dist) * Mathf.Rad2Deg, 2f, _wideFov0);
            float z = Mathf.SmoothStep(0f, 1f, _wideZoom);
            var lens = camWide.Lens;
            lens.FieldOfView = Mathf.Lerp(_wideFov0, fit, z);
            camWide.Lens = lens;
            // 推近時火箭底部往下放一點：火箭頭不會頂到倒數下方的字幕
            if (_wideAim != null)
            {
                var c = _wideAim.Composition;
                c.ScreenPosition = new Vector2(_wideScreen0.x, Mathf.Lerp(_wideScreen0.y, 0.36f, z));
                _wideAim.Composition = c;
            }
        }

        /// <summary>
        /// 長焦追蹤鏡頭的瞄準點與變焦。分離前瞄準火箭底部；分離後先把兩節都框進來，
        /// 第二節點火後轉去追上節並推近。太空段鏡頭跟著上節，維持換場那一格的方向與距離。
        /// 近剪裁面跟著距離往外推：火箭飛到幾十公里外時，深度緩衝的精度才夠（WebGL 沒有反向深度）。
        /// </summary>
        void UpdateLongCam(float real = 0f)
        {
            if (camLong == null || _longFocus == null || _rocket == null) return;
            if (_chase)
            {
                _chaseK = Mathf.MoveTowards(_chaseK, 1f, real / ChaseMoveSecs);
                // 最晚在一節關機前 8 秒移完：爬升段倍率高的時候（例如月球任務）4 個真實秒還沒走完就關機了，
                // 停在火箭正後下方的半路上，接近水平的火箭看起來像機頭朝下往下栽
                _chaseK = Mathf.Max(_chaseK, Mathf.InverseLerp(_chaseT, Mathf.Max(_chaseT + 1f, Mathf.Min(_mecoT, _swapT) - 8f), _T));
            }
            Vector3 focus;
            float frame;
            if (_inSpace)
            {
                focus = _rocket.upperStage.position;
                // 推軌：從長焦位置移到側面鏡頭目前的位置，取景跟著變成側面鏡頭的取景；
                // 推完時兩台鏡頭位置、方向、視角都一樣，之後換到側面鏡頭畫面不會動（不會先拉遠再拉近）
                _transferK = Mathf.MoveTowards(_transferK, 1f, real / DollySecs);
                _transferEase = Mathf.SmoothStep(0f, 1f, _transferK);
                var far = focus + _transferDir * _transferFar;
                var near = camSide != null ? camSide.State.GetFinalPosition() : focus + _transferDir * _transferNear;
                float sideFov = camSide != null ? camSide.State.Lens.FieldOfView : 36f;
                float frameSide = 2f * Vector3.Distance(near, focus) * Mathf.Tan(sideFov * 0.5f * Mathf.Deg2Rad);
                frame = Mathf.Exp(Mathf.Lerp(Mathf.Log(_frameUpper), Mathf.Log(Mathf.Max(0.01f, frameSide)), _transferEase));
                camLong.transform.position = Vector3.Lerp(far, near, _transferEase);
                if (camSide != null) _sideScreen = SideScreen(focus, near);
            }
            else if (_firstStageFree == null)
            {
                // 火箭轉向後接近水平：取景照傾斜程度推近（直立時看高度、水平時看寬度），瞄準點移到箭體中段，機頭才不會出畫面
                float tilt = Tilt();
                focus = _rocket.root.position + _rocket.root.up * (_rocket.height * 0.5f * tilt);
                frame = _frameBase * Mathf.Lerp(1f, TiltFrameK, tilt);
                if (_chase) camLong.transform.position = Vector3.Lerp(_chaseFrom, focus + _chaseOffset, Mathf.SmoothStep(0f, 1f, _chaseK));
            }
            else
            {
                // 最晚在換到太空場景前推完（換場那一格的構圖是照上節算的）
                float w = Mathf.Max(Mathf.SmoothStep(0f, 1f, _reframe), Ease(_sesT + 0.8f, Mathf.Max(_sesT + 3.2f, _swapT - 1f), _T));
                // 分離那一刻的瞄準點綁在上節上（相對上節的位置），之後沿著上節慢慢移到上節底部：
                // 跟著往後掉的第一節算的話，上節在畫面上會先往右飄、再往左滑，整個畫面像被晃了一下
                focus = _rocket.upperStage.position + _sepFocusOffset * (1f - w);
                // 不拉遠去框住兩節：從分離前的取景直接慢慢推到上節，第一節自己掉出畫面（一遠一近變化太快）
                frame = Mathf.Lerp(_frameBase * Mathf.Lerp(1f, TiltFrameK, Tilt()), _frameUpper, w);
                if (_chase) camLong.transform.position = Vector3.Lerp(_chaseFrom, focus + _chaseOffset, Mathf.SmoothStep(0f, 1f, _chaseK));
            }
            _longFocus.position = focus;
            // real = 0（換場、開始追蹤的那一格）只套用目前的值，不往目標走
            float want = Mathf.Log(Mathf.Max(0.01f, frame));
            if (float.IsNaN(_frameLog)) _frameLog = want;
            float diff = want - _frameLog;
            float step = Mathf.Clamp(diff * (1f - Mathf.Exp(-real * 1.5f)), -FrameRate * real, FrameRate * real);
            // 換到太空場景前一定要追上：換場那一格的鏡頭距離是照上節的取景算的，沒追上的話會變成很廣角的透視
            if (!_inSpace && _swapT < 1e5f && _T < _swapT && _pace != null)
            {
                float need = diff * Mathf.Min(1f, real / Mathf.Max(0.05f, (float)(_pace.PlannedLeft(_T) - _pace.PlannedLeft(_swapT)) - 0.1f));
                if (Mathf.Abs(need) > Mathf.Abs(step)) step = need;
            }
            if (_inSpace) step = diff; // 太空段的取景本身就是平滑的推軌，不再延遲（延遲的話推軌時火箭會忽大忽小）
            _frameLog += step;
            frame = Mathf.Exp(_frameLog);
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
        /// 上節在側面鏡頭畫面上的位置（和 AimLongCam 的 ScreenPosition 同一套座標）：
        /// 推軌時長焦鏡頭的構圖照這個值慢慢移過去，直接轉鏡頭方向的話火箭會在畫面上繞一大圈。
        /// </summary>
        Vector2 SideScreen(Vector3 focus, Vector3 sidePos)
        {
            var st = camSide.State;
            var local = Quaternion.Inverse(st.GetFinalOrientation()) * (focus - sidePos);
            if (local.z < 0.01f) return Vector2.zero;
            var outCam = brain != null ? brain.OutputCamera : null;
            float aspect = outCam != null ? outCam.aspect : 16f / 9f;
            float vfov = st.Lens.FieldOfView, hfov = Camera.VerticalToHorizontalFieldOfView(vfov, aspect);
            float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(yaw / hfov, -pitch / vfov);
        }

        float _leadX;                 // 長焦構圖的前方留白（畫面寬度的比例）：換場後維持換場前的值再補正

        /// <summary>某一點在長焦鏡頭畫面上的位置（0–1，左下是 0）：直接用鏡頭的位置、方向、視角算，不必等這一幀算圖。</summary>
        Vector2 LongViewport(Vector3 p)
        {
            var t = camLong.transform;
            var local = Quaternion.Inverse(t.rotation) * (p - t.position);
            var outCam = brain != null ? brain.OutputCamera : null;
            float aspect = outCam != null ? outCam.aspect : 16f / 9f;
            float th = Mathf.Tan(camLong.Lens.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float z = Mathf.Max(1e-3f, local.z);
            return new Vector2(0.5f + local.x / (z * 2f * th * aspect), 0.5f + local.y / (z * 2f * th));
        }

        /// <summary>箭體偏離垂直的程度：0 = 直立、1 = 水平。</summary>
        float Tilt() => Mathf.Clamp01(Vector3.Angle(_rocket.root.up, Vector3.up) / 90f);

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
            // 前方留白：換到太空場景後維持換場前的值（構圖要接得起來）
            if (!_inSpace) _leadX = -LeadRoom * Tilt();
            var screen = new Vector2(_longScreen.x + _leadX, _longScreen.y + (_inSpace ? _spaceScreenY : 0f));
            if (_inSpace) screen = Vector2.Lerp(screen, _sideScreen, _transferEase);
            camLong.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up)
                * Quaternion.Euler(-screen.y * vfov, -screen.x * hfov, 0f);
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
            _earthFade = Mathf.MoveTowards(_earthFade, 1f, Time.deltaTime / EarthFadeSecs);
            float fade = Mathf.SmoothStep(0f, 1f, _earthFade);
            _earthMpb.SetFloat(ExposureId, _earthExp0 * fade);
            // 地表邊緣的藍光調淡（材質的值是給設施窗外那顆小地球用的），光暈才不會一大片
            _earthMpb.SetFloat(AtmoStrengthId, EarthRimInSpace);
            _earth.SetPropertyBlock(_earthMpb);
            if (_atmo != null)
            {
                _atmo.GetPropertyBlock(_atmoMpb);
                _atmoMpb.SetFloat(StrengthId, _atmoStr0 * fade);
                _atmo.SetPropertyBlock(_atmoMpb);
            }
            if (_earthshine != null) _earthshine.intensity = _earthshine0 * fade;
            // 星空跟地面一樣是慣性方向固定的：鏡頭跟著軌道轉（機頭保持水平），看起來就和地球一起往後流
            if (_starsInst != null)
            {
                var sky = Matrix4x4.Rotate(Quaternion.Inverse(spin));
                _starsInst.SetVector(SkyRotX, sky.GetRow(0));
                _starsInst.SetVector(SkyRotY, sky.GetRow(1));
                _starsInst.SetVector(SkyRotZ, sky.GetRow(2));
            }
        }

        /// <summary>
        /// 升空的聲音：越飛越遠越小聲。照真實時間淡出（開始加速後引擎聲 RoarFadeSecs 秒、隆隆聲 RumbleFadeSecs 秒），
        /// 照任務時間的話原速和剛加速那段會有十秒左右的全音量，聽起來拖太久。
        /// </summary>
        void UpdateAudio()
        {
            if (_skipping || _inSpace) return;
            if (_T > RealTimeUntil) _audioFade += Time.unscaledDeltaTime;
            if (roar != null) roar.volume = _roar0 * (1f - Mathf.SmoothStep(0f, 1f, _audioFade / RoarFadeSecs)) * (_exploded ? 0f : 1f);
            if (rumble != null) rumble.volume = _rumble0 * (1f - Mathf.SmoothStep(0f, 1f, _audioFade / RumbleFadeSecs));
        }

        const float RoarFadeSecs = 4f, RumbleFadeSecs = 6f;
        float _audioFade;

        /// <summary>天空隨（遙測的）高度變黑：大氣變薄、霧散掉、陽光從黃昏暖色變成太空的白光。</summary>
        void UpdateSky()
        {
            _darkWant = Ease(3f, 75f, LaunchFeed.AltitudeKm);
        }

        /// <summary>
        /// 天色照真實時間跟上高度（每秒最多變 DarkRate）：加速爬升時高度一兩秒就衝過去，直接照高度的話天空會一下子變黑。
        /// 快換到太空場景時加快，換場那一刻剛好全黑（兩邊的天空才接得起來）。
        /// </summary>
        void UpdateDarkness(float real)
        {
            if (_inSpace || _pace == null) return;
            float step = DarkRate * real;
            if (_swapT < 1e5f && _T < _swapT)
            {
                float left = Mathf.Max(0.2f, (float)(_pace.PlannedLeft(_T) - _pace.PlannedLeft(_swapT)));
                step = Mathf.Max(step, (1f - _darkShown) * real / left);
            }
            _darkShown = Mathf.MoveTowards(_darkShown, Mathf.Max(_darkWant, _darkShown), step);
            // 天色沒變就不重設（倒數那十幾秒一直是 0）：ApplyDarkness 設的值只由天色決定，發射台段也沒有別人會改它們
            if (_darkShown != _darkApplied)
            {
                ApplyDarkness(_darkShown);
                _darkApplied = _darkShown;
            }
            // 天空幾乎全黑時換成星空，但亮度先保持 0：長焦下星點會被放大成光斑，換到太空、鏡頭拉開之後才亮
            if (_darkShown > 0.97f && _starsShared != null && _starsInst == null)
            {
                _starsInst = new Material(_starsShared) { name = _starsShared.name + " (runtime)" };
                _starsBright = _starsShared.GetFloat("_Brightness");
                _starsInst.SetFloat("_Brightness", 0f);
                RenderSettings.skybox = _starsInst;
            }
        }

        /// <summary>星空照真實時間淡入（換場後 1 秒開始、6 秒內亮完）：照任務時間的話，加速時一瞬間就亮了。</summary>
        /// <remarks>
        /// 真的相機曝光是給被照亮的火箭和地球的，星星幾乎拍不到：整體壓暗（StarsLevel），
        /// 引擎在燒、尾焰在畫面裡時再暗一點，關機滑行時慢慢亮回來（照真實時間，約 2 秒）。
        /// </remarks>
        void FadeInStars(float real)
        {
            if (_starsInst == null) return;
            _starsT += real;
            float want = UpperOn ? StarsBurnK : 1f;
            _starsExp = Mathf.MoveTowards(_starsExp == 0f ? want : _starsExp, want, real * 0.3f);
            _starsInst.SetFloat(BrightnessId, _starsBright * StarsLevel * _starsExp * Mathf.SmoothStep(0f, 1f, (_starsT - 1f) / 6f));
        }

        void ApplyDarkness(float dark)
        {
            float lit = (1f - dark) * (1f - dark);
            if (_skyInst != null)
            {
                _skyInst.SetFloat(ExposureId, _sky0Exposure * lit);
                _skyInst.SetFloat(AtmosphereThicknessId, Mathf.Lerp(_sky0Thickness, 0.3f, dark));
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
        // 每幀都要設的材質屬性：名稱先換成 id，不必每幀查字串
        static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
        static readonly int AtmoStrengthId = Shader.PropertyToID("_AtmoStrength");
        static readonly int StrengthId = Shader.PropertyToID("_Strength");
        static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
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
