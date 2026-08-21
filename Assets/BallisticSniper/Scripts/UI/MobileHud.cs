using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BallisticSniper
{
    public struct HudSnapshot
    {
        public int Stage;
        public int Range;
        public float Wind;
        public BallisticSolution Solution;
        public float ElevationDial;
        public float WindageDial;
        public int Zoom;
        public float Breath;
        public bool HoldingBreath;
        public int TargetsCleared;
        public int ShotsRemaining;
        public int Score;
        public bool BonusMode;
        public bool CanFire;
        public Difficulty Difficulty;
    }

    public struct ResultSnapshot
    {
        public string Headline;
        public int Points;
        public float ErrorCentimetres;
        public float ErrorMilX;
        public float ErrorMilY;
        public string TargetNote;
        public float ReviewZoom;
        public string ActionLabel;
    }

    public struct SummarySnapshot
    {
        public int Score;
        public int HighScore;
        public int HitCount;
        public int DestroyedCount;
        public int TotalShots;
        public int SuccessfulShots;
    }

    public sealed class MobileHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(5, 10, 10, 255);
        private static readonly Color Paper = new Color32(239, 237, 220, 255);
        private static readonly Color Gold = new Color32(232, 180, 75, 255);
        private static readonly Color GoldLight = new Color32(250, 220, 142, 255);
        private static readonly Color Mint = new Color32(123, 205, 178, 255);
        private static readonly Color Red = new Color32(218, 76, 63, 255);
        private static readonly Color Panel = new Color32(13, 28, 25, 226);
        private static readonly string[] DifficultyLabels =
        {
            "КАДЕТ\nПодсказка",
            "СТРЕЛОК\nСнос + качка",
            "ЭКСПЕРТ\nПорывы ветра"
        };

        private BallisticGame game;
        private Font font;
        private Texture2D uiTexture;
        private Canvas canvas;
        private RectTransform safeRoot;
        private GameObject aimSurface;
        private GameObject scopeLayer;
        private ScopeOverlayGraphic scopeOverlay;
        private ReticleGraphic reticle;
        private GameObject gameplayRoot;
        private GameObject menuRoot;
        private GameObject helpRoot;
        private GameObject briefingRoot;
        private GameObject resultRoot;
        private GameObject summaryRoot;
        private GameObject pauseRoot;
        private GameObject cinematicRoot;

        private readonly Button[] difficultyButtons = new Button[3];
        private Text highScoreText;
        private Text stageText;
        private Text rangeText;
        private Text windText;
        private Text targetText;
        private Text ammoText;
        private Text scoreText;
        private Text elevationText;
        private Text elevationCalcText;
        private Text windageText;
        private Text windageCalcText;
        private Text zoomText;
        private Text breathText;
        private Text reticleInfoText;
        private Text modeText;
        private RectTransform breathFillRect;
        private Button startButton;
        private ReliableButtonBinding startBinding;
        private Button fireButton;
        private Text fireButtonText;
        private Text briefingKicker;
        private Text briefingTitle;
        private Text briefingNote;
        private Text briefingStats;
        private Text briefingSolution;
        private Button briefingEnterButton;
        private Text resultHeadline;
        private Text resultPoints;
        private Text resultError;
        private Text resultCorrection;
        private Text resultTarget;
        private Text resultZoom;
        private Button resultAction;
        private Text summaryRank;
        private Text summaryScore;
        private Text summaryStats;
        private Text cinematicLabel;

        // Runtime buttons are dispatched directly on pointer-down. Waiting
        // for uGUI's pointer-up/click sequence proved unreliable on several
        // Android devices after a landscape/safe-area change.
        private readonly List<ReliableButtonBinding> reliableButtons = new List<ReliableButtonBinding>();

        public float ScopeRadius
        {
            get
            {
                if (safeRoot != null && safeRoot.rect.width > 1f && safeRoot.rect.height > 1f)
                    return Mathf.Min(safeRoot.rect.height * 0.455f, safeRoot.rect.width * 0.32f);
                return Mathf.Min(1080f * 0.455f, 1920f * 0.32f);
            }
        }
        public float CanvasHeight => safeRoot != null && safeRoot.rect.height > 1f ? safeRoot.rect.height : 1080f;
        public Button StartButtonForTests => startButton;
        public Button BriefingEnterButtonForTests => briefingEnterButton;
        public bool IsGameplayVisible => gameplayRoot != null && gameplayRoot.activeInHierarchy;
        public bool IsScopeVisible => scopeLayer != null && scopeLayer.activeInHierarchy;
        public bool IsBriefingVisible => briefingRoot != null && briefingRoot.activeInHierarchy;
        public bool IsMenuVisible => menuRoot != null && menuRoot.activeInHierarchy;

        public void Initialize(BallisticGame owner)
        {
            game = owner;
            LoadFont();
            CreateUiTexture();
            CreateCanvas();
            CreateScopeLayer();
            CreateGameplay();
            CreateMenu();
            CreateHelp();
            CreateBriefing();
            CreateResult();
            CreateSummary();
            CreatePause();
            CreateCinematic();
            ShowMenu(0, Difficulty.Shooter);
        }

        private void Update()
        {
            DispatchReliableTouches();
        }

        public void SetReticleScale(float pixelsPerMil, int zoom)
        {
            if (reticle != null) reticle.SetScale(pixelsPerMil, ScopeRadius, zoom);
        }

        public void ShowMenu(int highScore, Difficulty difficulty)
        {
            SetRoots(menu: true);
            highScoreText.text = "РЕКОРД  " + highScore;
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                bool selected = i == (int)difficulty;
                Graphic graphic = difficultyButtons[i].targetGraphic;
                Color normal = selected ? new Color32(232, 180, 75, 255) : new Color32(23, 37, 34, 245);
                graphic.color = Color.white;
                ColorBlock colors = difficultyButtons[i].colors;
                colors.normalColor = normal;
                colors.highlightedColor = selected ? new Color32(250, 220, 142, 255) : new Color32(44, 73, 64, 255);
                colors.pressedColor = selected ? new Color32(201, 143, 45, 255) : new Color32(13, 30, 26, 255);
                difficultyButtons[i].colors = colors;
                graphic.CrossFadeColor(normal, 0f, true, true);

                Text label = difficultyButtons[i].GetComponentInChildren<Text>();
                label.text = (selected ? "✓ " : string.Empty) + DifficultyLabels[i];
                label.color = selected ? Ink : Paper;
            }
        }

        public void ShowHelp()
        {
            SetRoots(help: true);
        }

        public void ShowBriefing(int stage, StageDefinition definition, float wind, BallisticSolution solution)
        {
            SetRoots(briefing: true);
            briefingEnterButton.interactable = true;
            SetButtonLabel(briefingEnterButton, "НА РУБЕЖ");
            briefingKicker.text = "РУБЕЖ " + (stage + 1) + " / " + GameRules.Stages;
            briefingTitle.text = definition.Name;
            briefingNote.text = definition.Note;
            briefingStats.text = string.Format(CultureInfo.InvariantCulture,
                "ДИСТАНЦИЯ\n{0} м\n\nВЕТЕР\n{1:0.0} м/с\n\nЦЕЛЕЙ\n{2}\n\nПАТРОНОВ\n{3}",
                definition.RangeMetres, Mathf.Abs(wind), GameRules.TargetsPerStage, GameRules.ShotsPerStage);
            briefingSolution.text = string.Format(CultureInfo.InvariantCulture,
                "TOF  {0:0.00} с    •    ELEV  +{1:0.0} MIL    •    WINDAGE  {2}\nОба барабана: шаг 0,5 MIL    •    FFP ×4–×16",
                solution.TimeSeconds,
                solution.ElevationMil,
                FormatWindage((float)-solution.WindMil));
        }

        public void ShowGameplay(HudSnapshot snapshot, bool flight)
        {
            SetRoots(gameplay: true, scope: true, aim: true);
            UpdateGameplay(snapshot);
            fireButton.interactable = snapshot.CanFire && !flight;
            fireButtonText.text = flight ? "ПУЛЯ В ПУТИ" : "ОГОНЬ";
        }

        public void ShowImpactWait(HudSnapshot snapshot)
        {
            SetRoots(gameplay: true, scope: true);
            UpdateGameplay(snapshot);
            fireButton.interactable = false;
            fireButtonText.text = "РАЗБОР";
        }

        public void UpdateGameplay(HudSnapshot snapshot)
        {
            stageText.text = GameRules.StageDefinitions[snapshot.Stage].Name;
            rangeText.text = snapshot.Range + " м";
            string arrow = snapshot.Wind >= 0f ? "→" : "←";
            windText.text = string.Format(CultureInfo.InvariantCulture, "ВЕТЕР  {0}  {1:0.0} м/с", arrow, Mathf.Abs(snapshot.Wind));
            targetText.text = snapshot.BonusMode ? "БОНУСНАЯ СТАЛЬ" : snapshot.TargetsCleared + "/" + GameRules.TargetsPerStage + " ЦЕЛЕЙ";
            ammoText.text = snapshot.ShotsRemaining + " ПАТР.";
            scoreText.text = snapshot.Score.ToString(CultureInfo.InvariantCulture);
            elevationText.text = string.Format(CultureInfo.InvariantCulture, "{0:+0.0;-0.0;0.0}", snapshot.ElevationDial);
            elevationCalcText.text = string.Format(CultureInfo.InvariantCulture, "РАСЧЁТ  +{0:0.0}", snapshot.Solution.ElevationMil);
            windageText.text = FormatWindage(snapshot.WindageDial);
            windageCalcText.text = "РАСЧЁТ  " + FormatWindage((float)-snapshot.Solution.WindMil);
            zoomText.text = "×" + snapshot.Zoom;
            breathText.text = snapshot.HoldingBreath ? "ДЕРЖУ • ВЕДИ" : "ДЫХАНИЕ";
            Vector2 fillMax = breathFillRect.anchorMax;
            fillMax.x = Mathf.Clamp01(snapshot.Breath);
            breathFillRect.anchorMax = fillMax;
            reticleInfoText.text = "FFP  ×" + snapshot.Zoom + "   •   1 MIL";
            modeText.text = snapshot.Difficulty == Difficulty.Cadet ? "КАДЕТ • БЕЗ КАЧКИ" :
                snapshot.Difficulty == Difficulty.Shooter ? "СТРЕЛОК • СНОС + КАЧКА" : "ЭКСПЕРТ • ПОРЫВЫ";
            fireButton.interactable = snapshot.CanFire;
        }

        public void ShowResult(ResultSnapshot snapshot)
        {
            SetRoots(gameplay: true, result: true, scope: true);
            resultHeadline.text = snapshot.Headline;
            resultHeadline.color = snapshot.Points > 0 ? Gold : Red;
            resultPoints.text = snapshot.Points > 0 ? "+" + snapshot.Points + " ОЧКОВ" : "0 ОЧКОВ";
            resultError.text = string.Format(CultureInfo.InvariantCulture, "ОТКЛОНЕНИЕ\n{0:0} см", snapshot.ErrorCentimetres);
            string horizontal = Mathf.Abs(snapshot.ErrorMilX) < 0.03f
                ? "WIND  0.0 MIL"
                : string.Format(CultureInfo.InvariantCulture, "WIND  {0} {1:0.00} MIL", snapshot.ErrorMilX > 0f ? "L" : "R", Mathf.Abs(snapshot.ErrorMilX));
            string vertical = Mathf.Abs(snapshot.ErrorMilY) < 0.03f
                ? "ELEV  0.0 MIL"
                : string.Format(CultureInfo.InvariantCulture, "ELEV  {0} {1:0.00} MIL", snapshot.ErrorMilY > 0f ? "−" : "+", Mathf.Abs(snapshot.ErrorMilY));
            resultCorrection.text = "ПОПРАВКА ПРИЦЕЛА\n" + horizontal + "\n" + vertical;
            resultTarget.text = snapshot.TargetNote;
            resultZoom.text = string.Format(CultureInfo.InvariantCulture, "РАЗБОР  ×{0:0.0}", snapshot.ReviewZoom);
            SetButtonLabel(resultAction, snapshot.ActionLabel);
        }

        public void ShowSummary(SummarySnapshot snapshot)
        {
            SetRoots(summary: true);
            summaryRank.text = snapshot.Score >= 650 ? "СНАЙПЕР" : snapshot.Score >= 400 ? "СТРЕЛОК" : "НОВОБРАНЕЦ";
            summaryScore.text = snapshot.Score + "  /  " + GameRules.CampaignMaxScore;
            int accuracy = snapshot.TotalShots == 0 ? 0 : Mathf.RoundToInt(snapshot.SuccessfulShots * 100f / snapshot.TotalShots);
            summaryStats.text =
                "ЦЕЛЕЙ ПОРАЖЕНО\n" + snapshot.HitCount + " / " + GameRules.CampaignTargets +
                "\n\nРАЗРУШЕНО\n" + snapshot.DestroyedCount + " / " + GameRules.CampaignDestructibles +
                "\n\nТОЧНОСТЬ\n" + accuracy + "%\n\nЛУЧШИЙ СЧЁТ\n" + snapshot.HighScore;
        }

        public void ShowPause()
        {
            SetRoots(gameplay: true, pause: true, scope: true);
        }

        public void ShowCinematic(int cameraVariant)
        {
            SetRoots(cinematic: true);
            cinematicLabel.text = string.Format(CultureInfo.InvariantCulture, "BULLET CAM  {0:00}/{1:00}   •   {2}",
                cameraVariant + 1, GameRules.CinematicNames.Length, GameRules.CinematicNames[cameraVariant]);
        }

        private void CreateCanvas()
        {
            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(transform, false);

            GameObject canvasObject = new GameObject("Mobile HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject safe = new GameObject("Safe Area", typeof(RectTransform));
            safe.transform.SetParent(canvasObject.transform, false);
            safeRoot = safe.GetComponent<RectTransform>();
            Stretch(safeRoot);
            safe.AddComponent<SafeAreaFitter>();
        }

        private void CreateScopeLayer()
        {
            aimSurface = new GameObject("Aim Surface", typeof(RectTransform), typeof(Image), typeof(AimDragSurface));
            aimSurface.transform.SetParent(safeRoot, false);
            Stretch(aimSurface.GetComponent<RectTransform>());
            Image aimImage = aimSurface.GetComponent<Image>();
            aimImage.color = new Color(0f, 0f, 0f, 0.001f);
            AimDragSurface drag = aimSurface.GetComponent<AimDragSurface>();
            drag.Dragged = game.DragAim;

            scopeLayer = new GameObject("Scope Optics", typeof(RectTransform));
            scopeLayer.transform.SetParent(safeRoot, false);
            Stretch(scopeLayer.GetComponent<RectTransform>());

            GameObject shadeObject = new GameObject("Circular Scope Mask", typeof(RectTransform), typeof(ScopeOverlayGraphic));
            shadeObject.transform.SetParent(scopeLayer.transform, false);
            Stretch(shadeObject.GetComponent<RectTransform>());
            scopeOverlay = shadeObject.GetComponent<ScopeOverlayGraphic>();
            scopeOverlay.color = new Color(0.006f, 0.010f, 0.009f, 0.96f);
            scopeOverlay.raycastTarget = false;

            GameObject reticleObject = new GameObject("FFP MIL Reticle", typeof(RectTransform), typeof(ReticleGraphic));
            reticleObject.transform.SetParent(scopeLayer.transform, false);
            Stretch(reticleObject.GetComponent<RectTransform>());
            reticle = reticleObject.GetComponent<ReticleGraphic>();
            reticle.raycastTarget = false;
        }

        private void CreateGameplay()
        {
            gameplayRoot = CreateRoot("Gameplay HUD");
            CreatePanel(gameplayRoot.transform, "Top Left", new Vector2(0.025f, 0.84f), new Vector2(0.31f, 0.965f), new Color32(8, 17, 15, 218));
            stageText = CreateText(gameplayRoot.transform, "Stage", new Vector2(0.038f, 0.903f), new Vector2(0.29f, 0.953f), 23, TextAnchor.MiddleLeft, Gold);
            rangeText = CreateText(gameplayRoot.transform, "Range", new Vector2(0.038f, 0.852f), new Vector2(0.14f, 0.902f), 31, TextAnchor.MiddleLeft, Paper, FontStyle.Bold);
            windText = CreateText(gameplayRoot.transform, "Wind", new Vector2(0.145f, 0.852f), new Vector2(0.29f, 0.902f), 23, TextAnchor.MiddleRight, Mint);

            CreatePanel(gameplayRoot.transform, "Top Right", new Vector2(0.69f, 0.84f), new Vector2(0.975f, 0.965f), new Color32(8, 17, 15, 218));
            targetText = CreateText(gameplayRoot.transform, "Targets", new Vector2(0.71f, 0.905f), new Vector2(0.86f, 0.952f), 22, TextAnchor.MiddleLeft, GoldLight);
            ammoText = CreateText(gameplayRoot.transform, "Ammo", new Vector2(0.855f, 0.905f), new Vector2(0.955f, 0.952f), 22, TextAnchor.MiddleRight, Paper);
            scoreText = CreateText(gameplayRoot.transform, "Score", new Vector2(0.71f, 0.848f), new Vector2(0.955f, 0.907f), 34, TextAnchor.MiddleRight, Gold, FontStyle.Bold);

            CreatePanel(gameplayRoot.transform, "Elevation Panel", new Vector2(0.025f, 0.34f), new Vector2(0.14f, 0.70f), Panel);
            CreateText(gameplayRoot.transform, "Elevation Heading", new Vector2(0.035f, 0.645f), new Vector2(0.13f, 0.688f), 18, TextAnchor.MiddleCenter, Gold, FontStyle.Bold).text = "ELEV • MIL";
            elevationText = CreateText(gameplayRoot.transform, "Elevation Value", new Vector2(0.035f, 0.57f), new Vector2(0.13f, 0.646f), 31, TextAnchor.MiddleCenter, Paper, FontStyle.Bold);
            elevationCalcText = CreateText(gameplayRoot.transform, "Elevation Calculation", new Vector2(0.035f, 0.525f), new Vector2(0.13f, 0.575f), 15, TextAnchor.MiddleCenter, Mint);
            CreateButton(gameplayRoot.transform, "Elevation Minus", "− 0.5", new Vector2(0.039f, 0.425f), new Vector2(0.126f, 0.515f), () => game.AdjustElevation(-0.5f), false);
            CreateButton(gameplayRoot.transform, "Elevation Plus", "+ 0.5", new Vector2(0.039f, 0.335f), new Vector2(0.126f, 0.415f), () => game.AdjustElevation(0.5f), true);

            CreatePanel(gameplayRoot.transform, "Zoom Panel", new Vector2(0.145f, 0.34f), new Vector2(0.235f, 0.70f), Panel);
            CreateText(gameplayRoot.transform, "Zoom Heading", new Vector2(0.155f, 0.645f), new Vector2(0.225f, 0.688f), 18, TextAnchor.MiddleCenter, Gold, FontStyle.Bold).text = "ZOOM";
            zoomText = CreateText(gameplayRoot.transform, "Zoom Value", new Vector2(0.155f, 0.56f), new Vector2(0.225f, 0.645f), 32, TextAnchor.MiddleCenter, Paper, FontStyle.Bold);
            CreateButton(gameplayRoot.transform, "Zoom Minus", "−", new Vector2(0.158f, 0.445f), new Vector2(0.222f, 0.535f), () => game.AdjustZoom(-1), false);
            CreateButton(gameplayRoot.transform, "Zoom Plus", "+", new Vector2(0.158f, 0.345f), new Vector2(0.222f, 0.435f), () => game.AdjustZoom(1), true);

            CreatePanel(gameplayRoot.transform, "Windage Panel", new Vector2(0.86f, 0.34f), new Vector2(0.975f, 0.70f), Panel);
            CreateText(gameplayRoot.transform, "Windage Heading", new Vector2(0.87f, 0.645f), new Vector2(0.965f, 0.688f), 18, TextAnchor.MiddleCenter, Gold, FontStyle.Bold).text = "WIND • MIL";
            windageText = CreateText(gameplayRoot.transform, "Windage Value", new Vector2(0.87f, 0.57f), new Vector2(0.965f, 0.646f), 27, TextAnchor.MiddleCenter, Paper, FontStyle.Bold);
            windageCalcText = CreateText(gameplayRoot.transform, "Windage Calculation", new Vector2(0.87f, 0.525f), new Vector2(0.965f, 0.575f), 14, TextAnchor.MiddleCenter, Mint);
            CreateButton(gameplayRoot.transform, "Windage Left", "L 0.5", new Vector2(0.874f, 0.425f), new Vector2(0.961f, 0.515f), () => game.AdjustWindage(-0.5f), false);
            CreateButton(gameplayRoot.transform, "Windage Right", "R 0.5", new Vector2(0.874f, 0.335f), new Vector2(0.961f, 0.415f), () => game.AdjustWindage(0.5f), true);

            Button menu = CreateButton(gameplayRoot.transform, "Pause", "Ⅱ", new Vector2(0.025f, 0.735f), new Vector2(0.075f, 0.82f), game.TogglePause, false);
            menu.GetComponentInChildren<Text>().fontSize = 28;

            GameObject breath = CreatePanel(gameplayRoot.transform, "Breath Hold + Aim", new Vector2(0.035f, 0.055f), new Vector2(0.19f, 0.19f), new Color32(19, 45, 39, 238));
            breath.GetComponent<RawImage>().raycastTarget = true;
            HoldDragButton hold = breath.AddComponent<HoldDragButton>();
            hold.HoldChanged = game.SetBreath;
            hold.Dragged = game.DragAim;
            breathText = CreateText(breath.transform, "Breath Label", new Vector2(0.08f, 0.43f), new Vector2(0.92f, 0.90f), 22, TextAnchor.MiddleCenter, Paper, FontStyle.Bold);
            GameObject breathBack = CreatePanel(breath.transform, "Stamina Back", new Vector2(0.11f, 0.15f), new Vector2(0.89f, 0.31f), new Color32(3, 10, 8, 220));
            GameObject breathFillObject = CreatePanel(breathBack.transform, "Stamina Fill", Vector2.zero, Vector2.one, Mint);
            breathFillRect = breathFillObject.GetComponent<RectTransform>();

            fireButton = CreateButton(gameplayRoot.transform, "Fire", "ОГОНЬ", new Vector2(0.805f, 0.045f), new Vector2(0.965f, 0.21f), game.Fire, true);
            fireButtonText = fireButton.GetComponentInChildren<Text>();
            fireButtonText.fontSize = 31;

            reticleInfoText = CreateText(gameplayRoot.transform, "Reticle Info", new Vector2(0.37f, 0.07f), new Vector2(0.63f, 0.115f), 18, TextAnchor.MiddleCenter, new Color32(226, 232, 218, 220));
            modeText = CreateText(gameplayRoot.transform, "Mode", new Vector2(0.37f, 0.025f), new Vector2(0.63f, 0.066f), 15, TextAnchor.MiddleCenter, GoldLight);
        }

        private void CreateMenu()
        {
            menuRoot = CreateRoot("Main Menu", new Color32(3, 10, 9, 205));
            CreateText(menuRoot.transform, "Kicker", new Vector2(0.075f, 0.82f), new Vector2(0.50f, 0.90f), 22, TextAnchor.MiddleLeft, Mint, FontStyle.Bold).text = "OFFLINE BALLISTICS SIMULATOR";
            CreateText(menuRoot.transform, "Title", new Vector2(0.075f, 0.59f), new Vector2(0.60f, 0.82f), 74, TextAnchor.MiddleLeft, Paper, FontStyle.Bold).text = "BALLISTIC";
            CreateText(menuRoot.transform, "Subtitle", new Vector2(0.078f, 0.53f), new Vector2(0.58f, 0.61f), 31, TextAnchor.MiddleLeft, Gold, FontStyle.Bold).text = "СНАЙПЕРСКИЙ РУБЕЖ • UNITY 3D";
            CreateText(menuRoot.transform, "Claim", new Vector2(0.078f, 0.46f), new Vector2(0.62f, 0.535f), 22, TextAnchor.MiddleLeft, Paper).text = "Пять целей одновременно. Оптика ×4–×16. Реальная поправка.";
            CreateText(menuRoot.transform, "Specs", new Vector2(0.078f, 0.36f), new Vector2(0.62f, 0.46f), 20, TextAnchor.MiddleLeft, GoldLight).text = "КАЛИБР  .308 WIN       НАЧ. СКОРОСТЬ  820 м/с       ОПТИКА  FFP ×4–×16";

            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                float left = 0.078f + i * 0.158f;
                difficultyButtons[i] = CreateButton(menuRoot.transform, "Difficulty " + i, DifficultyLabels[i],
                    new Vector2(left, 0.16f), new Vector2(left + 0.145f, 0.33f), () => game.SetDifficulty((Difficulty)captured), i == 1);
            }
            CreateText(menuRoot.transform, "Mode Label", new Vector2(0.078f, 0.32f), new Vector2(0.30f, 0.365f), 17, TextAnchor.MiddleLeft, Paper).text = "РЕЖИМ";

            highScoreText = CreateText(menuRoot.transform, "High Score", new Vector2(0.70f, 0.67f), new Vector2(0.94f, 0.73f), 22, TextAnchor.MiddleCenter, GoldLight, FontStyle.Bold);
            startButton = CreateButton(menuRoot.transform, "Start", "НАЧАТЬ", new Vector2(0.72f, 0.51f), new Vector2(0.93f, 0.66f), game.StartCampaign, true);
            startBinding = reliableButtons[reliableButtons.Count - 1];
            startButton.GetComponentInChildren<Text>().fontSize = 31;
            CreateButton(menuRoot.transform, "Help", "КАК ИГРАТЬ", new Vector2(0.72f, 0.39f), new Vector2(0.93f, 0.49f), game.OpenHelp, false);
            CreateText(menuRoot.transform, "Offline", new Vector2(0.56f, 0.035f), new Vector2(0.95f, 0.08f), 17, TextAnchor.MiddleRight, new Color32(255, 255, 255, 150)).text = "v3.2.0  •  Без рекламы  •  Без интернета  •  Без регистрации";
        }

        private void CreateHelp()
        {
            helpRoot = CreateRoot("Help", new Color32(3, 10, 9, 235));
            CreateText(helpRoot.transform, "Kicker", new Vector2(0.09f, 0.86f), new Vector2(0.50f, 0.92f), 21, TextAnchor.MiddleLeft, Mint, FontStyle.Bold).text = "ПОЛЕВАЯ ИНСТРУКЦИЯ";
            CreateText(helpRoot.transform, "Title", new Vector2(0.09f, 0.73f), new Vector2(0.75f, 0.87f), 47, TextAnchor.MiddleLeft, Paper, FontStyle.Bold).text = "КАК ПОПАСТЬ С ПЕРВОГО";
            string[] heads = { "01  ВЫБЕРИ ЦЕЛЬ", "02  КРАТНОСТЬ И СЕТКА", "03  ВНЕСИ ПОПРАВКУ", "04  СОБЕРИ КОМБО" };
            string[] bodies =
            {
                "Все пять целей активны одновременно. На поздних рубежах они скользят, качаются и меняют высоту.",
                "Меняй ×4–×16. FFP-сетка масштабируется вместе с изображением; деления всегда сохраняют значение MIL.",
                "Слева ELEV, справа WINDAGE. Зажми ДЫХАНИЕ и веди прицел тем же пальцем; вторым пальцем нажми ОГОНЬ.",
                "У каждого материала свой звук. Бочка запускает цепную реакцию; яблочко включает один из 14 трёхмерных киноповторов."
            };
            for (int i = 0; i < 4; i++)
            {
                float left = 0.09f + i * 0.207f;
                GameObject card = CreatePanel(helpRoot.transform, "Help Card " + i, new Vector2(left, 0.28f), new Vector2(left + 0.187f, 0.70f), new Color32(16, 35, 31, 235));
                CreateText(card.transform, "Head", new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.93f), 22, TextAnchor.MiddleLeft, Gold, FontStyle.Bold).text = heads[i];
                CreateText(card.transform, "Body", new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.69f), 17, TextAnchor.UpperLeft, Paper).text = bodies[i];
            }
            CreateText(helpRoot.transform, "MIL note", new Vector2(0.09f, 0.13f), new Vector2(0.78f, 0.23f), 19, TextAnchor.MiddleLeft, GoldLight).text = "1 крупное деление = 1 MIL при любой кратности. Бочка рядом с объектом экономит патрон и открывает бонусные выстрелы.";
            CreateButton(helpRoot.transform, "Back", "НАЗАД", new Vector2(0.80f, 0.10f), new Vector2(0.92f, 0.22f), game.CloseHelp, false);
        }

        private void CreateBriefing()
        {
            briefingRoot = CreateRoot("Briefing", new Color32(3, 10, 9, 220));
            GameObject card = CreatePanel(briefingRoot.transform, "Briefing Card", new Vector2(0.20f, 0.12f), new Vector2(0.80f, 0.88f), new Color32(12, 29, 25, 242));
            briefingKicker = CreateText(card.transform, "Kicker", new Vector2(0.08f, 0.83f), new Vector2(0.92f, 0.93f), 21, TextAnchor.MiddleCenter, Mint, FontStyle.Bold);
            briefingTitle = CreateText(card.transform, "Title", new Vector2(0.08f, 0.66f), new Vector2(0.92f, 0.84f), 43, TextAnchor.MiddleCenter, Paper, FontStyle.Bold);
            briefingNote = CreateText(card.transform, "Note", new Vector2(0.08f, 0.59f), new Vector2(0.92f, 0.68f), 20, TextAnchor.MiddleCenter, GoldLight);
            briefingStats = CreateText(card.transform, "Stats", new Vector2(0.10f, 0.19f), new Vector2(0.31f, 0.58f), 19, TextAnchor.UpperLeft, Paper, FontStyle.Bold);
            briefingSolution = CreateText(card.transform, "Solution", new Vector2(0.34f, 0.26f), new Vector2(0.90f, 0.55f), 20, TextAnchor.MiddleLeft, GoldLight, FontStyle.Bold);
            briefingEnterButton = CreateButton(card.transform, "Enter Range", "НА РУБЕЖ", new Vector2(0.36f, 0.06f), new Vector2(0.64f, 0.20f), game.EnterRange, true);
            briefingEnterButton.GetComponentInChildren<Text>().fontSize = 28;
        }

        private void CreateResult()
        {
            resultRoot = CreateRoot("Shot Result");
            GameObject card = CreatePanel(resultRoot.transform, "Result Card", new Vector2(0.745f, 0.20f), new Vector2(0.975f, 0.80f), new Color32(10, 25, 22, 247));
            resultHeadline = CreateText(card.transform, "Headline", new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.96f), 31, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
            resultPoints = CreateText(card.transform, "Points", new Vector2(0.08f, 0.76f), new Vector2(0.92f, 0.85f), 17, TextAnchor.MiddleCenter, Paper, FontStyle.Bold);
            resultError = CreateText(card.transform, "Error", new Vector2(0.09f, 0.58f), new Vector2(0.91f, 0.75f), 22, TextAnchor.MiddleLeft, Paper, FontStyle.Bold);
            resultCorrection = CreateText(card.transform, "Correction", new Vector2(0.09f, 0.32f), new Vector2(0.91f, 0.58f), 17, TextAnchor.MiddleLeft, GoldLight, FontStyle.Bold);
            resultTarget = CreateText(card.transform, "Target", new Vector2(0.09f, 0.20f), new Vector2(0.91f, 0.32f), 15, TextAnchor.MiddleCenter, Paper);
            resultZoom = CreateText(card.transform, "Review Zoom", new Vector2(0.09f, 0.12f), new Vector2(0.91f, 0.20f), 15, TextAnchor.MiddleCenter, Gold);
            resultAction = CreateButton(resultRoot.transform, "Continue", "К ЦЕЛЯМ", new Vector2(0.765f, 0.065f), new Vector2(0.955f, 0.175f), game.ContinueAfterResult, true);
        }

        private void CreateSummary()
        {
            summaryRoot = CreateRoot("Summary", new Color32(3, 10, 9, 225));
            CreateText(summaryRoot.transform, "Kicker", new Vector2(0.11f, 0.82f), new Vector2(0.48f, 0.90f), 21, TextAnchor.MiddleLeft, Mint, FontStyle.Bold).text = "СЕССИЯ ЗАВЕРШЕНА";
            summaryRank = CreateText(summaryRoot.transform, "Rank", new Vector2(0.11f, 0.62f), new Vector2(0.48f, 0.82f), 58, TextAnchor.MiddleLeft, Paper, FontStyle.Bold);
            CreateText(summaryRoot.transform, "Result label", new Vector2(0.11f, 0.55f), new Vector2(0.45f, 0.63f), 21, TextAnchor.MiddleLeft, Gold).text = "ИТОГОВЫЙ РЕЗУЛЬТАТ";
            summaryScore = CreateText(summaryRoot.transform, "Score", new Vector2(0.11f, 0.34f), new Vector2(0.50f, 0.55f), 62, TextAnchor.MiddleLeft, Gold, FontStyle.Bold);
            GameObject stats = CreatePanel(summaryRoot.transform, "Stats", new Vector2(0.54f, 0.27f), new Vector2(0.88f, 0.82f), new Color32(15, 37, 32, 238));
            summaryStats = CreateText(stats.transform, "Stats Text", new Vector2(0.10f, 0.08f), new Vector2(0.90f, 0.92f), 20, TextAnchor.MiddleLeft, Paper, FontStyle.Bold);
            CreateButton(summaryRoot.transform, "Again", "ЕЩЁ РАЗ", new Vector2(0.54f, 0.10f), new Vector2(0.70f, 0.22f), game.RestartCampaign, true);
            CreateButton(summaryRoot.transform, "Menu", "В МЕНЮ", new Vector2(0.72f, 0.10f), new Vector2(0.88f, 0.22f), game.OpenMenu, false);
        }

        private void CreatePause()
        {
            pauseRoot = CreateRoot("Pause", new Color32(0, 0, 0, 215));
            CreateText(pauseRoot.transform, "Pause Title", new Vector2(0.35f, 0.57f), new Vector2(0.65f, 0.72f), 51, TextAnchor.MiddleCenter, Paper, FontStyle.Bold).text = "ПАУЗА";
            CreateButton(pauseRoot.transform, "Resume", "ПРОДОЛЖИТЬ", new Vector2(0.40f, 0.42f), new Vector2(0.60f, 0.54f), game.ResumeGame, true);
            CreateButton(pauseRoot.transform, "Menu", "В МЕНЮ", new Vector2(0.40f, 0.27f), new Vector2(0.60f, 0.39f), game.OpenMenu, false);
        }

        private void CreateCinematic()
        {
            cinematicRoot = CreateRoot("Cinematic Labels");
            cinematicLabel = CreateText(cinematicRoot.transform, "Camera Name", new Vector2(0.24f, 0.87f), new Vector2(0.76f, 0.95f), 22, TextAnchor.MiddleCenter, GoldLight, FontStyle.Bold);
            CreateText(cinematicRoot.transform, "Slow Motion", new Vector2(0.37f, 0.05f), new Vector2(0.63f, 0.11f), 16, TextAnchor.MiddleCenter, Paper).text = "CINEMATIC IMPACT • .308 WIN";
        }

        private void SetRoots(
            bool menu = false,
            bool help = false,
            bool briefing = false,
            bool gameplay = false,
            bool result = false,
            bool summary = false,
            bool pause = false,
            bool cinematic = false,
            bool scope = false,
            bool aim = false)
        {
            menuRoot.SetActive(menu);
            helpRoot.SetActive(help);
            briefingRoot.SetActive(briefing);
            gameplayRoot.SetActive(gameplay);
            resultRoot.SetActive(result);
            summaryRoot.SetActive(summary);
            pauseRoot.SetActive(pause);
            cinematicRoot.SetActive(cinematic);
            scopeLayer.SetActive(scope);
            aimSurface.SetActive(aim);
        }

        private GameObject CreateRoot(string name, Color? background = null)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(safeRoot, false);
            Stretch(root.GetComponent<RectTransform>());
            if (background.HasValue)
            {
                RawImage image = root.AddComponent<RawImage>();
                image.texture = uiTexture;
                image.color = background.Value;
                image.raycastTarget = false;
            }
            return root;
        }

        private GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            panel.transform.SetParent(parent, false);
            SetAnchors(panel.GetComponent<RectTransform>(), anchorMin, anchorMax);
            RawImage image = panel.GetComponent<RawImage>();
            image.texture = uiTexture;
            image.color = color;
            image.raycastTarget = false;
            return panel;
        }

        private Text CreateText(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int size,
            TextAnchor alignment,
            Color color,
            FontStyle style = FontStyle.Normal)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            SetAnchors(textObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            Shadow shadow = textObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            return text;
        }

        private Button CreateButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Action action,
            bool primary)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(RawImage), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            SetAnchors(buttonObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
            RawImage image = buttonObject.GetComponent<RawImage>();
            image.texture = uiTexture;
            image.color = Color.white;
            image.raycastTarget = true;
            Color normal = primary ? new Color32(201, 143, 45, 248) : new Color32(26, 49, 43, 244);
            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = primary ? new Color32(239, 187, 87, 255) : new Color32(44, 73, 64, 255);
            colors.pressedColor = primary ? new Color32(170, 111, 28, 255) : new Color32(13, 30, 26, 255);
            colors.disabledColor = new Color32(40, 48, 45, 180);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ReliableButtonBinding binding = new ReliableButtonBinding(button, action);
            reliableButtons.Add(binding);
            ReliableTapReceiver pointerDown = buttonObject.AddComponent<ReliableTapReceiver>();
            pointerDown.Pressed = binding.Invoke;
            // Keep Unity's standard pointer-up route as a second path. The
            // pointer-down receiver and global Android fallback are additional
            // routes; the binding debounces all of them.
            if (action != null) button.onClick.AddListener(binding.Invoke);
            Text text = CreateText(buttonObject.transform, "Label", new Vector2(0.06f, 0.08f), new Vector2(0.94f, 0.92f),
                21, TextAnchor.MiddleCenter, primary ? Ink : Paper, FontStyle.Bold);
            text.text = label;
            return button;
        }

        private void DispatchReliableTouches()
        {
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began)
                    {
                        InvokeButtonAt(touch.position);
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(0)) InvokeButtonAt(Input.mousePosition);
        }

        private void InvokeButtonAt(Vector2 screenPosition)
        {
            // START has a normalized safe-area hit zone as an Android fallback.
            // It remains valid even when a device reports stale RectTransform
            // geometry during a landscape orientation/safe-area transition.
            if (game.CurrentScreen == GameScreen.Menu && startBinding != null && startBinding.CanInvoke &&
                (RectTransformUtility.RectangleContainsScreenPoint(startBinding.Rect, screenPosition, null) ||
                 IsStartZone(screenPosition)))
            {
                startBinding.Invoke();
                return;
            }

            for (int i = reliableButtons.Count - 1; i >= 0; i--)
            {
                ReliableButtonBinding binding = reliableButtons[i];
                if (!binding.CanInvoke) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(binding.Rect, screenPosition, null)) continue;
                binding.Invoke();
                return;
            }
        }

        private static bool IsStartZone(Vector2 screenPosition)
        {
            Rect safe = Screen.safeArea;
            if (safe.width <= 1f || safe.height <= 1f)
            {
                safe = new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
            }
            float normalizedX = (screenPosition.x - safe.xMin) / safe.width;
            float normalizedY = (screenPosition.y - safe.yMin) / safe.height;
            return normalizedX >= 0.69f && normalizedX <= 0.96f &&
                   normalizedY >= 0.48f && normalizedY <= 0.69f;
        }

        public void TapStartThroughAndroidFallbackForTests()
        {
            Canvas.ForceUpdateCanvases();
            InvokeButtonAt(StartButtonScreenCentre());
        }

        public void TapStartThroughPointerDownForTests()
        {
            Canvas.ForceUpdateCanvases();
            PointerEventData pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = StartButtonScreenCentre()
            };
            ExecuteEvents.Execute<IPointerDownHandler>(startButton.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        }

        public void TapStartThroughStandardClickForTests()
        {
            startButton.onClick.Invoke();
        }

        private Vector2 StartButtonScreenCentre()
        {
            RectTransform rect = (RectTransform)startButton.transform;
            Vector3 worldCentre = rect.TransformPoint(rect.rect.center);
            return RectTransformUtility.WorldToScreenPoint(null, worldCentre);
        }

        private sealed class ReliableButtonBinding
        {
            private readonly Button button;
            private readonly Action action;
            private int lastInvokedFrame = -100;
            private float lastInvokedAt = -100f;

            public ReliableButtonBinding(Button button, Action action)
            {
                this.button = button;
                this.action = action;
            }

            public RectTransform Rect => (RectTransform)button.transform;
            public bool CanInvoke => action != null && button != null && button.gameObject.activeInHierarchy && button.isActiveAndEnabled && button.interactable;

            public void Invoke()
            {
                if (!CanInvoke || lastInvokedFrame == Time.frameCount || Time.unscaledTime - lastInvokedAt < 0.30f) return;
                lastInvokedFrame = Time.frameCount;
                lastInvokedAt = Time.unscaledTime;
                action();
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            Text text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        private static string FormatWindage(float mil)
        {
            if (Mathf.Abs(mil) < 0.001f) return "0.0";
            return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0}", mil < 0f ? "L" : "R", Mathf.Abs(mil));
        }

        private void LoadFont()
        {
            try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch (ArgumentException) { font = null; }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                catch (ArgumentException) { font = null; }
            }
        }

        private void CreateUiTexture()
        {
            uiTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = "Runtime UI White Pixel",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            uiTexture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            uiTexture.Apply(false, true);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
