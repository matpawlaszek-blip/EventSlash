using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace EventSlash
{
    [Serializable]
    public class FruitDefinition
    {
        public string name;
        public GameObject wholePrefab;
        public GameObject halfAPrefab;
        public GameObject halfBPrefab;
        public Color juiceColor = Color.red;
        public float scale = 1.25f;
    }

    public enum GameState
    {
        MainMenu,
        Playing,
        GameOver
    }

    public enum SettingsTab
    {
        General,
        Tracking,
        Game
    }

    public class EventSlashGameManager : MonoBehaviour
    {
        public static EventSlashGameManager Instance { get; private set; }

        public const int DefaultLanguageIndex = 1; // 0 = English, 1 = Polski
        public const bool DefaultBombsEnabled = true;
        public const float DefaultBombSpawnChance = 0.18f;
        public const float DefaultSpawnRateMultiplier = 1.0f;
        public const float DefaultFruitSizeMultiplier = 1.0f;
        public const int DefaultMaxLives = 3;
        public const bool DefaultLivesEnabled = true;

        [Header("General Settings")]
        [Tooltip("0 = English, 1 = Polski")]
        public int languageIndex = DefaultLanguageIndex;

        [Header("Fruit Prefabs (Whole + Sliced Halves)")]
        public List<FruitDefinition> fruitTypes = new List<FruitDefinition>();

        [Header("Bomb Prefab (Whole + Exploded Halves)")]
        public FruitDefinition bombDefinition;
        public bool bombsEnabled = DefaultBombsEnabled;
        [Range(0.05f, 0.45f)]
        public float bombSpawnChance = DefaultBombSpawnChance;

        [Header("Gameplay Spawning Settings")]
        public float spawnIntervalMin = 1.15f;
        public float spawnIntervalMax = 2.10f;
        [Range(0.5f, 2.0f)]
        public float spawnRateMultiplier = DefaultSpawnRateMultiplier;
        [Range(0.7f, 1.5f)]
        public float fruitSizeMultiplier = DefaultFruitSizeMultiplier;
        public float spawnY = -5.3f;
        public float spawnZ = 0.0f;
        public float minLaunchSpeedY = 13.6f;
        public float maxLaunchSpeedY = 16.0f;
        [Range(1, 5)]
        public int maxLives = DefaultMaxLives;
        public bool livesEnabled = DefaultLivesEnabled;

        [Header("Game State")]
        public GameState currentState = GameState.MainMenu;
        public int score;
        public int bestScore;
        public int lives;

        [Header("UI Panels & Elements")]
        public GameObject menuBackgroundCanvasObj;
        public GameObject menuPanel;
        public GameObject hudPanel;
        public GameObject gameOverPanel;
        public GameObject settingsPopupPanel;

        public Text scoreText;
        public Text bestScoreHudText;
        public Text livesText;

        [Header("Ninja Combo Banner Elements")]
        public RectTransform comboBannerRect;
        public CanvasGroup comboBannerGroup;
        public Text comboHeaderText;
        public Text comboPopupText;

        [Header("Game Over Summary Elements")]
        public Text gameOverTitleText;
        public Text pointsLabelText;
        public Text gameOverScoreText;
        public Text gameOverBestText;

        [Header("Menu & Settings Tabs")]
        public Text menuStatusText;
        public Text menuProfileBestText;
        public Text settingsTitleText;
        public GameObject generalTabPanel;
        public GameObject trackingTabPanel;
        public GameObject gameTabPanel;
        public Button btnTabGeneral;
        public Button btnTabTracking;
        public Button btnTabGame;
        public Button btnResetDefaults;

        [Header("General Tab UI Elements")]
        public Text languageLabelText;
        public Dropdown languageDropdown;

        [Header("Tracking Tab UI Elements")]
        public Slider minHandSpeedSlider;
        public Text minHandSpeedValueText;
        public Slider minHandThicknessSlider;
        public Text minHandThicknessValueText;
        public Slider sensitivitySlider;
        public Text sensitivityValueText;
        public Button btnToggleMaxHands;
        public Text maxHandsToggleText;
        public Button btnToggleSwayFilter;
        public Text swayFilterToggleText;
        public Button btnToggleDenoise;
        public Text denoiseToggleText;

        [Header("Game Tab UI Elements")]
        public Slider spawnRateSlider;
        public Text spawnRateValueText;
        public Slider fruitSizeSlider;
        public Text fruitSizeValueText;
        public Slider maxLivesSlider;
        public Text maxLivesValueText;
        public Button btnToggleLives;
        public Text livesToggleText;
        public Slider bombChanceSlider;
        public Text bombChanceValueText;
        public Button btnToggleBombs;
        public Text bombsToggleText;

        [Header("Interactive 3D Fruit Selectors (Main Menu Only)")]
        public MenuFruitSelector liveModeSelector;
        public MenuFruitSelector rawModeSelector;

        [Header("Navigation Buttons")]
        public Button btnHudBackToMenu;
        public Button btnGameOverMenu;
        public Button btnToggleSettings;
        public Button btnCloseSettings;

        private SettingsTab _activeSettingsTab = SettingsTab.General;
        private float _nextSpawnTimer;
        private float _lastSliceTime;
        private int _comboCount;
        private float _comboDisplayTimer;
        private float _comboTotalDuration = 1.15f;
        private Vector2 _comboBaseAnchoredPos = new Vector2(0f, 315f);
        private float _scorePopTimer;
        private int _wavesSinceLastBomb;
        private bool _lastGameOverWasBomb;

        public bool IsEnglish => languageIndex == 0;

        private void Awake()
        {
            Application.runInBackground = true;
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            LoadSavedSettings();
        }

        private void Start()
        {
            if (comboBannerRect != null)
            {
                _comboBaseAnchoredPos = comboBannerRect.anchoredPosition;
            }
            EnsureUIWired();
            SyncAllSettingsUI();
            SelectSettingsTab(SettingsTab.General);
            ShowMainMenu();
        }

        private void LoadSavedSettings()
        {
            bestScore = PlayerPrefs.GetInt("EventSlash_BestScore", 0);
            languageIndex = Mathf.Clamp(PlayerPrefs.GetInt("EventSlash_Language", DefaultLanguageIndex), 0, 1);

            // Game tab settings
            bombsEnabled = PlayerPrefs.GetInt("EventSlash_BombsEnabled", DefaultBombsEnabled ? 1 : 0) == 1;
            bombSpawnChance = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_BombChance", DefaultBombSpawnChance), 0.05f, 0.45f);
            spawnRateMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_SpawnRate", DefaultSpawnRateMultiplier), 0.5f, 2.0f);
            fruitSizeMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_FruitSize", DefaultFruitSizeMultiplier), 0.7f, 1.5f);
            maxLives = Mathf.Clamp(PlayerPrefs.GetInt("EventSlash_MaxLives", DefaultMaxLives), 1, 5);
            livesEnabled = PlayerPrefs.GetInt("EventSlash_LivesEnabled", DefaultLivesEnabled ? 1 : 0) == 1;

            // Tracking tab settings
            if (EventCameraManager.Instance != null)
            {
                EventCameraManager cam = EventCameraManager.Instance;
                cam.sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_Sensitivity", EventCameraManager.DefaultSensitivity), 0.4f, 2.0f);
                cam.minHandSpeed = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_MinHandSpeed", EventCameraManager.DefaultMinHandSpeed), 80f, 320f);
                cam.minHandThickness = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_MinHandThickness", EventCameraManager.DefaultMinHandThickness), 140f, 480f);
                cam.maxHands = Mathf.Clamp(PlayerPrefs.GetInt("EventSlash_MaxHands", EventCameraManager.DefaultMaxHands), 1, 2);
                cam.swayFilterEnabled = PlayerPrefs.GetInt("EventSlash_SwayFilter", EventCameraManager.DefaultSwayFilterEnabled ? 1 : 0) == 1;
                cam.denoiseEnabled = PlayerPrefs.GetInt("EventSlash_DenoiseEnabled", EventCameraManager.DefaultDenoiseEnabled ? 1 : 0) == 1;
            }
        }

        public void ResetSettingsToDefaults()
        {
            switch (_activeSettingsTab)
            {
                case SettingsTab.General:
                    languageIndex = DefaultLanguageIndex;
                    PlayerPrefs.SetInt("EventSlash_Language", languageIndex);
                    break;

                case SettingsTab.Tracking:
                    if (EventCameraManager.Instance != null)
                    {
                        EventCameraManager cam = EventCameraManager.Instance;
                        cam.sensitivity = EventCameraManager.DefaultSensitivity;
                        cam.minHandSpeed = EventCameraManager.DefaultMinHandSpeed;
                        cam.minHandThickness = EventCameraManager.DefaultMinHandThickness;
                        cam.maxHands = EventCameraManager.DefaultMaxHands;
                        cam.swayFilterEnabled = EventCameraManager.DefaultSwayFilterEnabled;
                        cam.denoiseEnabled = EventCameraManager.DefaultDenoiseEnabled;

                        PlayerPrefs.SetFloat("EventSlash_Sensitivity", cam.sensitivity);
                        PlayerPrefs.SetFloat("EventSlash_MinHandSpeed", cam.minHandSpeed);
                        PlayerPrefs.SetFloat("EventSlash_MinHandThickness", cam.minHandThickness);
                        PlayerPrefs.SetInt("EventSlash_MaxHands", cam.maxHands);
                        PlayerPrefs.SetInt("EventSlash_SwayFilter", cam.swayFilterEnabled ? 1 : 0);
                        PlayerPrefs.SetInt("EventSlash_DenoiseEnabled", cam.denoiseEnabled ? 1 : 0);
                    }
                    break;

                case SettingsTab.Game:
                    bombsEnabled = DefaultBombsEnabled;
                    bombSpawnChance = DefaultBombSpawnChance;
                    spawnRateMultiplier = DefaultSpawnRateMultiplier;
                    fruitSizeMultiplier = DefaultFruitSizeMultiplier;
                    maxLives = DefaultMaxLives;
                    livesEnabled = DefaultLivesEnabled;

                    PlayerPrefs.SetInt("EventSlash_BombsEnabled", bombsEnabled ? 1 : 0);
                    PlayerPrefs.SetFloat("EventSlash_BombChance", bombSpawnChance);
                    PlayerPrefs.SetFloat("EventSlash_SpawnRate", spawnRateMultiplier);
                    PlayerPrefs.SetFloat("EventSlash_FruitSize", fruitSizeMultiplier);
                    PlayerPrefs.SetInt("EventSlash_MaxLives", maxLives);
                    PlayerPrefs.SetInt("EventSlash_LivesEnabled", livesEnabled ? 1 : 0);
                    break;
            }

            PlayerPrefs.Save();
            SyncAllSettingsUI();
        }

        private string GetLocalizedFruitName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return IsEnglish ? "FRUIT" : "OWOC";
            }

            string upper = rawName.Trim().ToUpperInvariant();
            if (IsEnglish)
            {
                switch (upper)
                {
                    case "ARBUZ":
                    case "WATERMELON":
                        return "WATERMELON";
                    case "JABŁKO":
                    case "JABLKO":
                    case "APPLE":
                        return "APPLE";
                    case "POMARAŃCZA":
                    case "POMARANCZA":
                    case "ORANGE":
                        return "ORANGE";
                    case "BANAN":
                    case "BANANA":
                        return "BANANA";
                    case "BOMBA":
                    case "BOMB":
                        return "BOMB";
                    default:
                        return upper;
                }
            }
            else
            {
                switch (upper)
                {
                    case "WATERMELON":
                    case "ARBUZ":
                        return "ARBUZ";
                    case "APPLE":
                    case "JABŁKO":
                    case "JABLKO":
                        return "JABŁKO";
                    case "ORANGE":
                    case "POMARAŃCZA":
                    case "POMARANCZA":
                        return "POMARAŃCZA";
                    case "BANANA":
                    case "BANAN":
                        return "BANAN";
                    case "BOMB":
                    case "BOMBA":
                        return "BOMBA";
                    default:
                        return upper;
                }
            }
        }

        private void SyncAllSettingsUI()
        {
            if (languageDropdown != null)
            {
                languageDropdown.SetValueWithoutNotify(Mathf.Clamp(languageIndex, 0, 1));
                languageDropdown.RefreshShownValue();
            }

            if (settingsTitleText != null)
            {
                settingsTitleText.text = IsEnglish ? "SETTINGS" : "USTAWIENIA";
            }
            if (languageLabelText != null)
            {
                languageLabelText.text = IsEnglish ? "LANGUAGE:" : "JĘZYK:";
            }

            if (btnTabGeneral != null)
            {
                Text l = btnTabGeneral.GetComponentInChildren<Text>(true);
                if (l != null) l.text = IsEnglish ? "GENERAL" : "OGÓLNE";
            }
            if (btnTabTracking != null)
            {
                Text l = btnTabTracking.GetComponentInChildren<Text>(true);
                if (l != null) l.text = IsEnglish ? "TRACKING" : "ŚLEDZENIE";
            }
            if (btnTabGame != null)
            {
                Text l = btnTabGame.GetComponentInChildren<Text>(true);
                if (l != null) l.text = IsEnglish ? "GAME" : "ROZGRYWKA";
            }

            if (btnResetDefaults != null)
            {
                Text rTxt = btnResetDefaults.GetComponentInChildren<Text>(true);
                if (rTxt != null) rTxt.text = IsEnglish ? "DEFAULT" : "DOMYŚLNE";
            }
            if (btnCloseSettings != null)
            {
                Text cTxt = btnCloseSettings.GetComponentInChildren<Text>(true);
                if (cTxt != null) cTxt.text = IsEnglish ? "CLOSE" : "ZAMKNIJ";
            }
            if (btnGameOverMenu != null)
            {
                Text goTxt = btnGameOverMenu.GetComponentInChildren<Text>(true);
                if (goTxt != null) goTxt.text = IsEnglish ? "BACK TO MENU" : "POWRÓT DO MENU";
            }
            if (btnHudBackToMenu != null)
            {
                Text hudMenuTxt = btnHudBackToMenu.GetComponentInChildren<Text>(true);
                if (hudMenuTxt != null) hudMenuTxt.text = "MENU [ESC]";
            }

            if (pointsLabelText != null)
            {
                pointsLabelText.text = IsEnglish ? "POINTS SCORED" : "ZDOBYTE PUNKTY";
            }
            if (gameOverTitleText != null)
            {
                if (_lastGameOverWasBomb)
                {
                    gameOverTitleText.text = IsEnglish ? "BOMB EXPLOSION!" : "WYBUCH BOMBY!";
                }
                else
                {
                    gameOverTitleText.text = IsEnglish ? "GAME OVER" : "KONIEC GRY";
                }
            }
            if (gameOverBestText != null)
            {
                gameOverBestText.text = IsEnglish
                    ? $"BEST SCORE: {bestScore}"
                    : $"NAJLEPSZY WYNIK: {bestScore}";
            }
            if (bestScoreHudText != null)
            {
                bestScoreHudText.text = IsEnglish ? $"BEST: {bestScore}" : $"REKORD: {bestScore}";
            }
            if (menuProfileBestText != null)
            {
                menuProfileBestText.text = IsEnglish
                    ? $"NINJA PROFILE  •  BEST: {bestScore}"
                    : $"PROFIL NINJA  •  REKORD: {bestScore}";
            }
            if (menuStatusText != null)
            {
                string rawFile = EventCameraManager.Instance != null ? EventCameraManager.Instance.activeRawFileName : "hand_test.raw";
                menuStatusText.text = IsEnglish
                    ? $"Slash a fruit with your hand to start! (RAW file: {rawFile})"
                    : $"Przeciągnij ostrzem przez wybrany owoc, aby rozpocząć! (Plik RAW: {rawFile})";
            }

            if (liveModeSelector != null)
            {
                string liveText = IsEnglish ? "LIVE CAMERA" : "KAMERA LIVE";
                liveModeSelector.SetArcTexts(liveText, liveText);
            }
            if (rawModeSelector != null)
            {
                rawModeSelector.SetArcTexts(
                    IsEnglish ? "OFFLINE MODE" : "TRYB OFFLINE",
                    IsEnglish ? "RAW FILE" : "PLIK RAW"
                );
            }

            EventCameraManager cam = EventCameraManager.Instance;
            float sens = cam != null ? cam.sensitivity : EventCameraManager.DefaultSensitivity;
            float minSpd = cam != null ? cam.minHandSpeed : EventCameraManager.DefaultMinHandSpeed;
            float minThk = cam != null ? cam.minHandThickness : EventCameraManager.DefaultMinHandThickness;

            if (minHandSpeedSlider != null) minHandSpeedSlider.SetValueWithoutNotify(minSpd);
            OnMinHandSpeedChanged(minSpd);

            if (minHandThicknessSlider != null) minHandThicknessSlider.SetValueWithoutNotify(minThk);
            OnMinHandThicknessChanged(minThk);

            if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(sens);
            OnSensitivityChanged(sens);

            UpdateMaxHandsToggleUI();
            UpdateSwayFilterToggleUI();
            UpdateDenoiseToggleUI();

            if (spawnRateSlider != null) spawnRateSlider.SetValueWithoutNotify(spawnRateMultiplier);
            OnSpawnRateChanged(spawnRateMultiplier);

            if (fruitSizeSlider != null) fruitSizeSlider.SetValueWithoutNotify(fruitSizeMultiplier);
            OnFruitSizeChanged(fruitSizeMultiplier);

            if (maxLivesSlider != null) maxLivesSlider.SetValueWithoutNotify(maxLives);
            UpdateLivesUI();

            if (bombChanceSlider != null) bombChanceSlider.SetValueWithoutNotify(bombSpawnChance);
            OnBombChanceChanged(bombSpawnChance);

            UpdateBombsToggleUI();
        }

        public void SelectGeneralTab()
        {
            SelectSettingsTab(SettingsTab.General);
        }

        public void SelectTrackingTab()
        {
            SelectSettingsTab(SettingsTab.Tracking);
        }

        public void SelectGameTab()
        {
            SelectSettingsTab(SettingsTab.Game);
        }

        public void SelectSettingsTab(SettingsTab tab)
        {
            _activeSettingsTab = tab;
            if (generalTabPanel != null) generalTabPanel.SetActive(tab == SettingsTab.General);
            if (trackingTabPanel != null) trackingTabPanel.SetActive(tab == SettingsTab.Tracking);
            if (gameTabPanel != null) gameTabPanel.SetActive(tab == SettingsTab.Game);

            UpdateTabButtonVisual(btnTabGeneral, tab == SettingsTab.General);
            UpdateTabButtonVisual(btnTabTracking, tab == SettingsTab.Tracking);
            UpdateTabButtonVisual(btnTabGame, tab == SettingsTab.Game);
        }

        private static void UpdateTabButtonVisual(Button btn, bool isActive)
        {
            if (btn == null) return;
            Image img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = isActive
                    ? new Color(1.0f, 0.96f, 0.82f, 1.0f)
                    : new Color(0.55f, 0.48f, 0.40f, 0.88f);
            }
            Text lbl = btn.GetComponentInChildren<Text>(true);
            if (lbl != null)
            {
                lbl.color = isActive
                    ? new Color(1.0f, 0.90f, 0.28f, 1.0f)
                    : new Color(0.82f, 0.76f, 0.68f, 0.90f);
            }
        }

        private void Update()
        {
            bool escPressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                escPressed = true;
#else
            if (Input.GetKeyDown(KeyCode.Escape))
                escPressed = true;
#endif
            if (escPressed)
            {
                if (currentState == GameState.MainMenu && settingsPopupPanel != null && settingsPopupPanel.activeSelf)
                {
                    CloseSettingsPopup();
                    return;
                }
                if (currentState != GameState.MainMenu)
                {
                    ShowMainMenu();
                    return;
                }
            }

            if (currentState == GameState.MainMenu)
            {
                if (menuProfileBestText != null)
                {
                    menuProfileBestText.text = IsEnglish
                        ? $"NINJA PROFILE  •  BEST: {bestScore}"
                        : $"PROFIL NINJA  •  REKORD: {bestScore}";
                }
                if (menuStatusText != null && EventCameraManager.Instance != null)
                {
                    menuStatusText.text = IsEnglish
                        ? $"Slash a fruit with your hand to start! (RAW file: {EventCameraManager.Instance.activeRawFileName})"
                        : $"Przeciągnij ostrzem przez wybrany owoc, aby rozpocząć! (Plik RAW: {EventCameraManager.Instance.activeRawFileName})";
                }
                return;
            }

            if (currentState == GameState.Playing)
            {
                _nextSpawnTimer -= Time.deltaTime;
                if (_nextSpawnTimer <= 0f)
                {
                    SpawnFruitWave();
                    float difficultyFactor = Mathf.Clamp(1.0f - (score * 0.008f), 0.55f, 1.0f);
                    float rateDiv = Mathf.Max(0.35f, spawnRateMultiplier);
                    _nextSpawnTimer = (UnityEngine.Random.Range(spawnIntervalMin, spawnIntervalMax) * difficultyFactor) / rateDiv;
                }

                AnimateScorePop();
                AnimateComboBanner();
                UpdateHUD();
            }
        }

        private void AnimateScorePop()
        {
            if (scoreText == null) return;
            if (_scorePopTimer > 0f)
            {
                _scorePopTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_scorePopTimer / 0.22f);
                float scale = 1.0f + 0.36f * Mathf.Sin(t * Mathf.PI);
                scoreText.rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            else
            {
                scoreText.rectTransform.localScale = Vector3.one;
            }
        }

        private void AnimateComboBanner()
        {
            if (_comboDisplayTimer > 0f)
            {
                _comboDisplayTimer -= Time.deltaTime;
                float elapsed = _comboTotalDuration - _comboDisplayTimer;
                float norm = Mathf.Clamp01(elapsed / _comboTotalDuration);

                if (comboBannerRect != null)
                {
                    float scale;
                    if (norm < 0.18f)
                    {
                        float p = norm / 0.18f;
                        scale = Mathf.Lerp(0.35f, 1.24f, p);
                    }
                    else if (norm < 0.32f)
                    {
                        float p = (norm - 0.18f) / 0.14f;
                        scale = Mathf.Lerp(1.24f, 1.0f, p);
                    }
                    else
                    {
                        scale = 1.0f;
                    }
                    comboBannerRect.localScale = new Vector3(scale, scale, 1f);
                    comboBannerRect.anchoredPosition = _comboBaseAnchoredPos + new Vector2(0f, norm * 34f);
                }

                if (comboBannerGroup != null)
                {
                    comboBannerGroup.alpha = norm > 0.72f ? Mathf.Clamp01((1.0f - norm) / 0.28f) : 1.0f;
                }

                if (_comboDisplayTimer <= 0f)
                {
                    HideComboBannerImmediate();
                }
            }
        }

        private void HideComboBannerImmediate()
        {
            if (comboBannerGroup != null) comboBannerGroup.alpha = 0f;
            if (comboPopupText != null) comboPopupText.text = "";
            if (comboHeaderText != null) comboHeaderText.text = "";
        }

        private void ShowNinjaCallout(string header, string mainText, Color headerColor, Color mainColor, float duration = 1.15f)
        {
            _comboTotalDuration = duration;
            _comboDisplayTimer = duration;

            if (comboHeaderText != null)
            {
                comboHeaderText.text = header;
                comboHeaderText.color = headerColor;
            }
            if (comboPopupText != null)
            {
                comboPopupText.text = mainText;
                comboPopupText.color = mainColor;
            }
            if (comboBannerGroup != null)
            {
                comboBannerGroup.alpha = 1.0f;
            }
            if (comboBannerRect != null)
            {
                comboBannerRect.anchoredPosition = _comboBaseAnchoredPos;
                comboBannerRect.localScale = Vector3.one * 0.4f;
            }
        }

        public void ShowMainMenu()
        {
            currentState = GameState.MainMenu;
            ClearActiveFruits();
            HideComboBannerImmediate();

            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.StopStream();
            }

            if (menuBackgroundCanvasObj != null) menuBackgroundCanvasObj.SetActive(true);
            if (menuPanel != null) menuPanel.SetActive(true);
            if (hudPanel != null) hudPanel.SetActive(false);
            if (gameOverPanel != null) gameOverPanel.SetActive(false);
            if (settingsPopupPanel != null) settingsPopupPanel.SetActive(false);

            if (liveModeSelector != null)
            {
                liveModeSelector.gameObject.SetActive(true);
                string liveText = IsEnglish ? "LIVE CAMERA" : "KAMERA LIVE";
                liveModeSelector.SetArcTexts(liveText, liveText);
                liveModeSelector.Initialize(StartLiveCameraGame);
            }
            if (rawModeSelector != null)
            {
                rawModeSelector.gameObject.SetActive(true);
                rawModeSelector.SetArcTexts(
                    IsEnglish ? "OFFLINE MODE" : "TRYB OFFLINE",
                    IsEnglish ? "RAW FILE" : "PLIK RAW"
                );
                rawModeSelector.Initialize(StartTestRawRecordingGame);
            }
        }

        public void StartLiveCameraGame()
        {
            StartGameplay(EventStreamMode.LiveCamera);
        }

        public void StartTestRawRecordingGame()
        {
            StartGameplay(EventStreamMode.RawRecording);
        }

        public void RestartCurrentMode()
        {
            EventStreamMode mode = EventCameraManager.Instance != null
                ? EventCameraManager.Instance.currentMode
                : EventStreamMode.RawRecording;
            StartGameplay(mode);
        }

        public void ToggleSettingsPopup()
        {
            if (settingsPopupPanel != null)
            {
                bool nextState = !settingsPopupPanel.activeSelf;
                settingsPopupPanel.SetActive(nextState);
                if (nextState)
                {
                    SyncAllSettingsUI();
                    SelectSettingsTab(_activeSettingsTab);
                    if (liveModeSelector != null) liveModeSelector.Hide3DFruit();
                    if (rawModeSelector != null) rawModeSelector.Hide3DFruit();
                }
                else
                {
                    if (liveModeSelector != null) liveModeSelector.Show3DFruit();
                    if (rawModeSelector != null) rawModeSelector.Show3DFruit();
                }
            }
        }

        public void CloseSettingsPopup()
        {
            if (settingsPopupPanel != null)
            {
                settingsPopupPanel.SetActive(false);
            }
            if (currentState == GameState.MainMenu)
            {
                if (liveModeSelector != null) liveModeSelector.Show3DFruit();
                if (rawModeSelector != null) rawModeSelector.Show3DFruit();
            }
        }

        private void StartGameplay(EventStreamMode mode)
        {
            ClearActiveFruits();
            HideComboBannerImmediate();
            score = 0;
            lives = maxLives;
            _comboCount = 0;
            _comboDisplayTimer = 0f;
            _scorePopTimer = 0f;
            _wavesSinceLastBomb = 0;
            _nextSpawnTimer = 0.85f;
            currentState = GameState.Playing;

            if (liveModeSelector != null) liveModeSelector.Hide3DFruit();
            if (rawModeSelector != null) rawModeSelector.Hide3DFruit();

            if (menuBackgroundCanvasObj != null) menuBackgroundCanvasObj.SetActive(false);
            if (menuPanel != null) menuPanel.SetActive(false);
            if (settingsPopupPanel != null) settingsPopupPanel.SetActive(false);
            if (hudPanel != null) hudPanel.SetActive(true);
            if (gameOverPanel != null) gameOverPanel.SetActive(false);

            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.StartStream(mode);
            }

            UpdateHUD();
        }

        private void SpawnFruitWave()
        {
            if (fruitTypes == null || fruitTypes.Count == 0) return;

            int count = UnityEngine.Random.value < 0.35f ? 2 : 1;
            if (score > 15 && UnityEngine.Random.value < 0.2f) count = 3;

            Camera cam = Camera.main;
            float halfWidth = 6.5f;
            if (cam != null && cam.orthographic)
            {
                halfWidth = cam.orthographicSize * cam.aspect * 0.72f;
            }

            for (int i = 0; i < count; i++)
            {
                FruitDefinition def = fruitTypes[UnityEngine.Random.Range(0, fruitTypes.Count)];
                if (def.wholePrefab == null) continue;
                SpawnSingleProjectile(def, false, halfWidth, i * 0.45f);
            }

            if (bombsEnabled)
            {
                _wavesSinceLastBomb++;
                bool shouldSpawnBomb = bombDefinition != null && bombDefinition.wholePrefab != null &&
                                       (_wavesSinceLastBomb >= 7 || (_wavesSinceLastBomb >= 3 && UnityEngine.Random.value < bombSpawnChance));
                if (shouldSpawnBomb)
                {
                    _wavesSinceLastBomb = 0;
                    SpawnSingleProjectile(bombDefinition, true, halfWidth, 0.25f);
                }
            }
        }

        private void SpawnSingleProjectile(FruitDefinition def, bool isBomb, float halfWidth, float yOffset)
        {
            float spawnX = UnityEngine.Random.Range(-halfWidth, halfWidth);
            Vector3 pos = new Vector3(spawnX, spawnY - yOffset, spawnZ + UnityEngine.Random.Range(-0.25f, 0.25f));
            Quaternion rot = isBomb
                ? Quaternion.Euler(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-20f, 20f))
                : UnityEngine.Random.rotation;

            float sizeMult = isBomb ? 1.0f : Mathf.Clamp(fruitSizeMultiplier, 0.7f, 1.5f);
            GameObject obj = Instantiate(def.wholePrefab, pos, rot);
            obj.name = isBomb ? "Bomb_Projectile" : $"Fruit_{def.name}";
            obj.transform.localScale = Vector3.one * (def.scale * sizeMult);

            FruitItem item = obj.GetComponent<FruitItem>();
            if (item == null) item = obj.AddComponent<FruitItem>();

            item.fruitName = def.name;
            item.isBomb = isBomb;
            item.halfAPrefab = def.halfAPrefab;
            item.halfBPrefab = def.halfBPrefab;
            item.juiceColor = def.juiceColor;
            item.viewportHitRadius = isBomb ? 0.065f : (0.085f * sizeMult);

            float targetX = UnityEngine.Random.Range(-halfWidth * 0.45f, halfWidth * 0.45f);
            float vx = (targetX - spawnX) * UnityEngine.Random.Range(0.45f, 0.72f);
            float vy = UnityEngine.Random.Range(minLaunchSpeedY, maxLaunchSpeedY);

            item.velocity = new Vector3(vx, vy, 0f);
            item.angularVelocity = isBomb
                ? new Vector3(UnityEngine.Random.Range(-65f, 65f), UnityEngine.Random.Range(-140f, 140f), UnityEngine.Random.Range(-90f, 90f))
                : new Vector3(UnityEngine.Random.Range(-150f, 150f), UnityEngine.Random.Range(-180f, 180f), UnityEngine.Random.Range(-150f, 150f));
        }

        public void OnFruitSliced(FruitItem fruit, Vector3 worldPos)
        {
            if (currentState != GameState.Playing) return;

            float now = Time.time;
            if (now - _lastSliceTime <= 0.48f)
            {
                _comboCount++;
            }
            else
            {
                _comboCount = 1;
            }
            _lastSliceTime = now;

            int bonus = _comboCount >= 2 ? _comboCount : 1;
            int gained = fruit.scorePoints * bonus;
            score += gained;
            _scorePopTimer = 0.22f;

            if (score > bestScore)
            {
                bestScore = score;
                PlayerPrefs.SetInt("EventSlash_BestScore", bestScore);
            }

            if (_comboCount >= 2)
            {
                ShowNinjaCallout(
                    IsEnglish ? "NINJA TECHNIQUE!" : "TECHNIKA NINJA!",
                    IsEnglish ? $"COMBO x{_comboCount}!   +{gained} PTS" : $"KOMBO x{_comboCount}!   +{gained} PKT",
                    new Color(1.0f, 0.38f, 0.16f),
                    new Color(1.0f, 0.90f, 0.20f),
                    1.25f
                );
            }
            else
            {
                ShowNinjaCallout(
                    IsEnglish ? "NINJA SLASH!" : "CIĘCIE NINJA!",
                    $"+{gained}   {GetLocalizedFruitName(fruit.fruitName)}",
                    new Color(0.35f, 0.95f, 1.0f),
                    new Color(1.0f, 0.94f, 0.42f),
                    0.95f
                );
            }

            UpdateHUD();
        }

        public void OnBombSliced(FruitItem bomb, Vector3 worldPos)
        {
            if (currentState != GameState.Playing) return;
            _lastGameOverWasBomb = true;
            TriggerGameOver(IsEnglish ? "BOMB EXPLOSION!" : "WYBUCH BOMBY!");
        }

        public void OnFruitMissed(FruitItem fruit)
        {
            if (currentState != GameState.Playing) return;

            // If hearts/lives are disabled via the checkbox in Settings, falling fruits never deduct lives or end the game!
            if (!livesEnabled) return;

            lives--;
            ShowNinjaCallout(
                IsEnglish ? "LIFE LOST!" : "UTRATA ŻYCIA!",
                IsEnglish ? "FRUIT DROPPED! (-1)" : "UPUSZCZONO OWOC! (-1)",
                new Color(1.0f, 0.28f, 0.24f),
                new Color(1.0f, 0.55f, 0.45f),
                1.15f
            );

            UpdateHUD();

            if (lives <= 0)
            {
                _lastGameOverWasBomb = false;
                TriggerGameOver(IsEnglish ? "GAME OVER" : "KONIEC GRY");
            }
        }

        private void TriggerGameOver(string titleReason = "KONIEC GRY")
        {
            currentState = GameState.GameOver;
            ClearActiveFruits();
            HideComboBannerImmediate();

            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.StopStream();
            }

            if (liveModeSelector != null) liveModeSelector.Hide3DFruit();
            if (rawModeSelector != null) rawModeSelector.Hide3DFruit();

            if (hudPanel != null) hudPanel.SetActive(false);
            if (gameOverPanel != null) gameOverPanel.SetActive(true);

            if (gameOverTitleText != null)
            {
                gameOverTitleText.text = titleReason;
            }
            if (pointsLabelText != null)
            {
                pointsLabelText.text = IsEnglish ? "POINTS SCORED" : "ZDOBYTE PUNKTY";
            }
            if (gameOverScoreText != null)
            {
                gameOverScoreText.text = $"{score}";
            }
            if (gameOverBestText != null)
            {
                gameOverBestText.text = IsEnglish
                    ? $"BEST SCORE: {bestScore}"
                    : $"NAJLEPSZY WYNIK: {bestScore}";
            }
        }

        private void UpdateHUD()
        {
            if (scoreText != null)
            {
                scoreText.text = $"{score}";
            }
            if (bestScoreHudText != null)
            {
                bestScoreHudText.text = IsEnglish ? $"BEST: {bestScore}" : $"REKORD: {bestScore}";
            }

            if (livesText != null)
            {
                if (livesText.transform.parent != null && livesText.transform.parent.name == "NinjaLivesBrush")
                {
                    livesText.transform.parent.gameObject.SetActive(livesEnabled);
                }
                else
                {
                    livesText.gameObject.SetActive(livesEnabled);
                }

                if (livesEnabled)
                {
                    string marks = "";
                    for (int i = 0; i < maxLives; i++)
                    {
                        int lostCount = maxLives - lives;
                        marks += (i < lostCount) ? "<color=#FF2A2A>✖</color> " : "<color=#65FF45>♥</color> ";
                    }
                    livesText.text = marks.TrimEnd();
                }
            }
        }

        private void ClearActiveFruits()
        {
            FruitItem[] activeFruits = FindObjectsByType<FruitItem>(FindObjectsSortMode.None);
            foreach (FruitItem f in activeFruits)
            {
                if (f != null) Destroy(f.gameObject);
            }

            SlicedFruitHalf[] halves = FindObjectsByType<SlicedFruitHalf>(FindObjectsSortMode.None);
            foreach (SlicedFruitHalf h in halves)
            {
                if (h != null) Destroy(h.gameObject);
            }
        }

        // ==================== GENERAL TAB HANDLERS ====================

        public void OnLanguageChanged(int index)
        {
            languageIndex = Mathf.Clamp(index, 0, 1);
            PlayerPrefs.SetInt("EventSlash_Language", languageIndex);
            PlayerPrefs.Save();
            SyncAllSettingsUI();
        }

        // ==================== TRACKING TAB HANDLERS ====================

        public void OnMinHandSpeedChanged(float val)
        {
            float clamped = Mathf.Clamp(val, 80f, 320f);
            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.minHandSpeed = clamped;
            }
            PlayerPrefs.SetFloat("EventSlash_MinHandSpeed", clamped);
            if (minHandSpeedValueText != null)
            {
                minHandSpeedValueText.text = IsEnglish
                    ? $"MIN. HAND SPEED: {clamped:F0} px/s"
                    : $"MIN. PRĘDKOŚĆ RUCHU DŁONI: {clamped:F0} px/s";
            }
        }

        public void OnMinHandThicknessChanged(float val)
        {
            float clamped = Mathf.Clamp(val, 140f, 480f);
            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.minHandThickness = clamped;
            }
            PlayerPrefs.SetFloat("EventSlash_MinHandThickness", clamped);
            if (minHandThicknessValueText != null)
            {
                minHandThicknessValueText.text = IsEnglish
                    ? $"HAND DETECTION DENSITY: {clamped:F0}"
                    : $"GĘSTOŚĆ DETEKCJI DŁONI: {clamped:F0}";
            }
        }

        public void OnSensitivityChanged(float val)
        {
            float clamped = Mathf.Clamp(val, 0.4f, 2.0f);
            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.sensitivity = clamped;
            }
            PlayerPrefs.SetFloat("EventSlash_Sensitivity", clamped);
            if (sensitivityValueText != null)
            {
                sensitivityValueText.text = IsEnglish
                    ? $"BLADE HIT RADIUS: {clamped:F1}x"
                    : $"PROMIEŃ CIĘCIA DŁONI: {clamped:F1}x";
            }
        }

        public void ToggleMaxHands()
        {
            if (EventCameraManager.Instance == null) return;
            EventCameraManager.Instance.maxHands = EventCameraManager.Instance.maxHands >= 2 ? 1 : 2;
            PlayerPrefs.SetInt("EventSlash_MaxHands", EventCameraManager.Instance.maxHands);
            UpdateMaxHandsToggleUI();
        }

        private void UpdateMaxHandsToggleUI()
        {
            int mh = EventCameraManager.Instance != null ? EventCameraManager.Instance.maxHands : EventCameraManager.DefaultMaxHands;
            if (maxHandsToggleText != null)
            {
                if (IsEnglish)
                {
                    maxHandsToggleText.text = mh >= 2
                        ? "MAX HANDS: <color=#65FF45>2 HANDS</color>"
                        : "MAX HANDS: <color=#FFD245>1 HAND</color>";
                }
                else
                {
                    maxHandsToggleText.text = mh >= 2
                        ? "MAKS. DŁONIE: <color=#65FF45>2 DŁONIE</color>"
                        : "MAKS. DŁONIE: <color=#FFD245>1 DŁOŃ</color>";
                }
            }
        }

        public void ToggleSwayFilter()
        {
            if (EventCameraManager.Instance == null) return;
            EventCameraManager.Instance.swayFilterEnabled = !EventCameraManager.Instance.swayFilterEnabled;
            PlayerPrefs.SetInt("EventSlash_SwayFilter", EventCameraManager.Instance.swayFilterEnabled ? 1 : 0);
            UpdateSwayFilterToggleUI();
        }

        private void UpdateSwayFilterToggleUI()
        {
            bool enabled = EventCameraManager.Instance == null || EventCameraManager.Instance.swayFilterEnabled;
            if (swayFilterToggleText != null)
            {
                if (IsEnglish)
                {
                    swayFilterToggleText.text = enabled
                        ? "BODY FILTER: <color=#65FF45>ON</color>"
                        : "BODY FILTER: <color=#FF5545>OFF</color>";
                }
                else
                {
                    swayFilterToggleText.text = enabled
                        ? "FILTR CIAŁA: <color=#65FF45>WŁĄCZONY</color>"
                        : "FILTR CIAŁA: <color=#FF5545>WYŁĄCZONY</color>";
                }
            }
        }

        public void ToggleDenoiseEnabled()
        {
            if (EventCameraManager.Instance == null) return;
            EventCameraManager.Instance.denoiseEnabled = !EventCameraManager.Instance.denoiseEnabled;
            PlayerPrefs.SetInt("EventSlash_DenoiseEnabled", EventCameraManager.Instance.denoiseEnabled ? 1 : 0);
            UpdateDenoiseToggleUI();
        }

        private void UpdateDenoiseToggleUI()
        {
            bool enabled = EventCameraManager.Instance == null || EventCameraManager.Instance.denoiseEnabled;
            if (denoiseToggleText != null)
            {
                if (IsEnglish)
                {
                    denoiseToggleText.text = enabled
                        ? "NOISE REDUCTION: <color=#65FF45>ON</color>"
                        : "NOISE REDUCTION: <color=#FF5545>OFF</color>";
                }
                else
                {
                    denoiseToggleText.text = enabled
                        ? "REDUKCJA SZUMU: <color=#65FF45>WŁĄCZONA</color>"
                        : "REDUKCJA SZUMU: <color=#FF5545>WYŁĄCZONA</color>";
                }
            }
        }

        // ==================== GAME TAB HANDLERS ====================

        public void OnSpawnRateChanged(float val)
        {
            spawnRateMultiplier = Mathf.Clamp(val, 0.5f, 2.0f);
            PlayerPrefs.SetFloat("EventSlash_SpawnRate", spawnRateMultiplier);
            if (spawnRateValueText != null)
            {
                spawnRateValueText.text = IsEnglish
                    ? $"FRUIT SPAWN TEMPO: {spawnRateMultiplier:F1}x"
                    : $"TEMPO WYRZUTU OWOCÓW: {spawnRateMultiplier:F1}x";
            }
        }

        public void OnFruitSizeChanged(float val)
        {
            fruitSizeMultiplier = Mathf.Clamp(val, 0.7f, 1.5f);
            PlayerPrefs.SetFloat("EventSlash_FruitSize", fruitSizeMultiplier);
            if (fruitSizeValueText != null)
            {
                fruitSizeValueText.text = IsEnglish
                    ? $"FRUIT SIZE: {fruitSizeMultiplier:F1}x"
                    : $"ROZMIAR OWOCÓW: {fruitSizeMultiplier:F1}x";
            }
        }

        public void OnMaxLivesChanged(float val)
        {
            maxLives = Mathf.Clamp(Mathf.RoundToInt(val), 1, 5);
            PlayerPrefs.SetInt("EventSlash_MaxLives", maxLives);
            UpdateLivesUI();
        }

        public void ToggleLivesEnabled()
        {
            livesEnabled = !livesEnabled;
            PlayerPrefs.SetInt("EventSlash_LivesEnabled", livesEnabled ? 1 : 0);
            UpdateLivesUI();
        }

        private void UpdateLivesUI()
        {
            if (maxLivesSlider != null)
            {
                maxLivesSlider.interactable = livesEnabled;
            }
            if (maxLivesValueText != null)
            {
                if (IsEnglish)
                {
                    maxLivesValueText.text = livesEnabled
                        ? $"STARTING LIVES: {maxLives} ♥"
                        : "STARTING LIVES: <color=#FFD245>DISABLED</color>";
                }
                else
                {
                    maxLivesValueText.text = livesEnabled
                        ? $"LICZBA ŻYĆ NA START: {maxLives} ♥"
                        : "LICZBA ŻYĆ NA START: <color=#FFD245>WYŁĄCZONE</color>";
                }
            }
            if (livesToggleText != null)
            {
                livesToggleText.text = livesEnabled
                    ? "[<color=#65FF45>✔</color>] ♥"
                    : "[<color=#FF5545>✖</color>] ♥";
            }
        }

        public void OnBombChanceChanged(float val)
        {
            bombSpawnChance = Mathf.Clamp(val, 0.05f, 0.45f);
            PlayerPrefs.SetFloat("EventSlash_BombChance", bombSpawnChance);
            if (bombChanceValueText != null)
            {
                bombChanceValueText.text = IsEnglish
                    ? $"BOMB FREQUENCY: {Mathf.RoundToInt(bombSpawnChance * 100f)}%"
                    : $"CZĘSTOTLIWOŚĆ BOMB: {Mathf.RoundToInt(bombSpawnChance * 100f)}%";
            }
        }

        public void ToggleBombsEnabled()
        {
            bombsEnabled = !bombsEnabled;
            PlayerPrefs.SetInt("EventSlash_BombsEnabled", bombsEnabled ? 1 : 0);
            UpdateBombsToggleUI();
        }

        private void UpdateBombsToggleUI()
        {
            if (bombsToggleText != null)
            {
                if (IsEnglish)
                {
                    bombsToggleText.text = bombsEnabled
                        ? "BOMBS IN GAME: <color=#65FF45>ON</color>"
                        : "BOMBS IN GAME: <color=#FF5545>OFF</color>";
                }
                else
                {
                    bombsToggleText.text = bombsEnabled
                        ? "BOMBY W GRZE: <color=#65FF45>WŁĄCZONE</color>"
                        : "BOMBY W GRZE: <color=#FF5545>WYŁĄCZONE</color>";
                }
            }
        }

        // ==================== UI WIRING & RUNTIME TAB BUILDER ====================

        private void EnsureUIWired()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }

            // Automatically upgrade SettingsDojoBoard if the scene doesn't have the 3-tab layout (General + Tracking + Game) yet
            if (settingsPopupPanel != null)
            {
                Transform existingGeneral = settingsPopupPanel.transform.Find("SettingsDojoBoard/GeneralTabPanel");
                Transform existingLivesCheckbox = settingsPopupPanel.transform.Find("SettingsDojoBoard/GameTabPanel/Btn_ToggleLives");
                if (existingGeneral == null || existingLivesCheckbox == null)
                {
                    RebuildTabbedSettingsPopupRuntime();
                }
            }

            MenuFruitSelector[] selectors = FindObjectsByType<MenuFruitSelector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (MenuFruitSelector sel in selectors)
            {
                if (sel.name == "Ring_LiveMode") liveModeSelector = sel;
                else if (sel.name == "Ring_OfflineRawMode") rawModeSelector = sel;
            }

            Button[] allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Button b in allButtons)
            {
                switch (b.name)
                {
                    case "Btn_HudBackToMenu": btnHudBackToMenu = b; break;
                    case "Btn_GameOverMenu": btnGameOverMenu = b; break;
                    case "Btn_ToggleSettings": btnToggleSettings = b; break;
                    case "Btn_CloseSettings": btnCloseSettings = b; break;
                    case "Btn_ResetDefaults": btnResetDefaults = b; break;
                    case "Btn_TabGeneral": btnTabGeneral = b; break;
                    case "Btn_TabTracking": btnTabTracking = b; break;
                    case "Btn_TabGame": btnTabGame = b; break;
                    case "Btn_ToggleMaxHands": btnToggleMaxHands = b; break;
                    case "Btn_ToggleSwayFilter": btnToggleSwayFilter = b; break;
                    case "Btn_ToggleDenoise": btnToggleDenoise = b; break;
                    case "Btn_ToggleLives": btnToggleLives = b; break;
                    case "Btn_ToggleBombs": btnToggleBombs = b; break;
                }
            }

            Slider[] allSliders = FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Slider s in allSliders)
            {
                switch (s.name)
                {
                    case "MinHandSpeedSlider": minHandSpeedSlider = s; break;
                    case "MinHandThicknessSlider": minHandThicknessSlider = s; break;
                    case "SensitivitySlider": sensitivitySlider = s; break;
                    case "SpawnRateSlider": spawnRateSlider = s; break;
                    case "FruitSizeSlider": fruitSizeSlider = s; break;
                    case "MaxLivesSlider": maxLivesSlider = s; break;
                    case "BombChanceSlider": bombChanceSlider = s; break;
                }
            }

            Dropdown[] allDropdowns = FindObjectsByType<Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Dropdown d in allDropdowns)
            {
                if (d.name == "LanguageDropdown")
                {
                    languageDropdown = d;
                }
            }

            Text[] allTexts = FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Text t in allTexts)
            {
                switch (t.name)
                {
                    case "SettingsTitle": settingsTitleText = t; break;
                    case "LanguageLabelText": languageLabelText = t; break;
                    case "MinHandSpeedValueText": minHandSpeedValueText = t; break;
                    case "MinHandThicknessValueText": minHandThicknessValueText = t; break;
                    case "SensitivityValueText": sensitivityValueText = t; break;
                    case "SpawnRateValueText": spawnRateValueText = t; break;
                    case "FruitSizeValueText": fruitSizeValueText = t; break;
                    case "MaxLivesValueText": maxLivesValueText = t; break;
                    case "BombChanceValueText": bombChanceValueText = t; break;
                    case "PointsLabel": pointsLabelText = t; break;
                    case "GameOverTitle": gameOverTitleText = t; break;
                    case "GameOverScoreText": gameOverScoreText = t; break;
                    case "GameOverBestText": gameOverBestText = t; break;
                    case "BestScoreHudText": bestScoreHudText = t; break;
                    case "ProfileBestText": menuProfileBestText = t; break;
                    case "MenuStatusText": menuStatusText = t; break;
                    case "ComboHeaderText": comboHeaderText = t; break;
                    case "ComboPopupText": comboPopupText = t; break;
                }
            }

            if (settingsPopupPanel != null)
            {
                Transform board = settingsPopupPanel.transform.Find("SettingsDojoBoard");
                if (board != null)
                {
                    Transform gen = board.Find("GeneralTabPanel");
                    if (gen != null) generalTabPanel = gen.gameObject;
                    Transform trk = board.Find("TrackingTabPanel");
                    if (trk != null) trackingTabPanel = trk.gameObject;
                    Transform gm = board.Find("GameTabPanel");
                    if (gm != null) gameTabPanel = gm.gameObject;
                }
            }

            WireButton(btnHudBackToMenu, ShowMainMenu);
            WireButton(btnGameOverMenu, ShowMainMenu);
            WireButton(btnToggleSettings, ToggleSettingsPopup);
            WireButton(btnCloseSettings, CloseSettingsPopup);
            WireButton(btnResetDefaults, ResetSettingsToDefaults);
            WireButton(btnTabGeneral, SelectGeneralTab);
            WireButton(btnTabTracking, SelectTrackingTab);
            WireButton(btnTabGame, SelectGameTab);

            if (languageDropdown != null)
            {
                languageDropdown.onValueChanged.RemoveAllListeners();
                languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
            }

            if (btnToggleMaxHands != null)
            {
                WireButton(btnToggleMaxHands, ToggleMaxHands);
                if (maxHandsToggleText == null) maxHandsToggleText = btnToggleMaxHands.GetComponentInChildren<Text>(true);
            }

            if (btnToggleSwayFilter != null)
            {
                WireButton(btnToggleSwayFilter, ToggleSwayFilter);
                if (swayFilterToggleText == null) swayFilterToggleText = btnToggleSwayFilter.GetComponentInChildren<Text>(true);
            }

            if (btnToggleDenoise != null)
            {
                WireButton(btnToggleDenoise, ToggleDenoiseEnabled);
                if (denoiseToggleText == null) denoiseToggleText = btnToggleDenoise.GetComponentInChildren<Text>(true);
            }

            if (btnToggleLives != null)
            {
                WireButton(btnToggleLives, ToggleLivesEnabled);
                if (livesToggleText == null) livesToggleText = btnToggleLives.GetComponentInChildren<Text>(true);
            }

            if (btnToggleBombs != null)
            {
                WireButton(btnToggleBombs, ToggleBombsEnabled);
                if (bombsToggleText == null) bombsToggleText = btnToggleBombs.GetComponentInChildren<Text>(true);
            }

            WireSlider(minHandSpeedSlider, OnMinHandSpeedChanged);
            WireSlider(minHandThicknessSlider, OnMinHandThicknessChanged);
            WireSlider(sensitivitySlider, OnSensitivityChanged);
            WireSlider(spawnRateSlider, OnSpawnRateChanged);
            WireSlider(fruitSizeSlider, OnFruitSizeChanged);
            WireSlider(maxLivesSlider, OnMaxLivesChanged);
            WireSlider(bombChanceSlider, OnBombChanceChanged);
        }

        private static void WireButton(Button btn, UnityEngine.Events.UnityAction action)
        {
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
        }

        private static void WireSlider(Slider slider, UnityEngine.Events.UnityAction<float> action)
        {
            if (slider == null) return;
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(action);
        }

        /// <summary>
        /// Dynamically upgrades the SettingsDojoBoard at runtime if the scene hasn't been rebuilt yet,
        /// so the GENERAL/OGÓLNE, TRACKING/ŚLEDZENIE, and GAME/ROZGRYWKA tabs + Language Dropdown + DOMYŚLNE button work immediately on Play.
        /// </summary>
        private void RebuildTabbedSettingsPopupRuntime()
        {
            if (settingsPopupPanel == null) return;
            Transform board = settingsPopupPanel.transform.Find("SettingsDojoBoard");
            if (board == null) return;

            RectTransform boardRt = board.GetComponent<RectTransform>();
            if (boardRt != null)
            {
                boardRt.anchoredPosition = new Vector2(0f, 10f);
                boardRt.sizeDelta = new Vector2(960f, 720f);
            }

            Font font = menuProfileBestText != null ? menuProfileBestText.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Sprite bannerSprite = btnCloseSettings != null && btnCloseSettings.GetComponent<Image>() != null
                ? btnCloseSettings.GetComponent<Image>().sprite
                : null;

            for (int i = board.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(board.GetChild(i).gameObject);
            }

            settingsTitleText = CreateRuntimeText(board, "SettingsTitle", "USTAWIENIA", font, 54,
                new Color(1.0f, 0.88f, 0.32f), new Vector2(0f, 282f), new Vector2(640f, 70f));

            btnTabGeneral = CreateRuntimeButton(board, "Btn_TabGeneral", bannerSprite, "OGÓLNE", font, 25,
                new Color(1.0f, 0.94f, 0.58f), new Vector2(-265f, 205f), new Vector2(245f, 58f));
            btnTabTracking = CreateRuntimeButton(board, "Btn_TabTracking", bannerSprite, "ŚLEDZENIE", font, 25,
                new Color(1.0f, 0.94f, 0.58f), new Vector2(0f, 205f), new Vector2(245f, 58f));
            btnTabGame = CreateRuntimeButton(board, "Btn_TabGame", bannerSprite, "ROZGRYWKA", font, 25,
                new Color(1.0f, 0.94f, 0.58f), new Vector2(265f, 205f), new Vector2(245f, 58f));

            // --- TAB 1: GENERAL ---
            generalTabPanel = CreateRuntimeContainer(board, "GeneralTabPanel");

            languageLabelText = CreateRuntimeText(generalTabPanel.transform, "LanguageLabelText",
                "JĘZYK:", font, 30,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 75f), new Vector2(560f, 44f));

            languageDropdown = CreateRuntimeDropdown(generalTabPanel.transform, "LanguageDropdown",
                bannerSprite, font, new Vector2(0f, 5f), new Vector2(380f, 60f),
                new List<string> { "English", "Polski" }, languageIndex);

            // --- TAB 2: TRACKING ---
            trackingTabPanel = CreateRuntimeContainer(board, "TrackingTabPanel");

            minHandSpeedValueText = CreateRuntimeText(trackingTabPanel.transform, "MinHandSpeedValueText",
                "MIN. PRĘDKOŚĆ RUCHU DŁONI: 155 px/s", font, 25,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 132f), new Vector2(760f, 36f));
            minHandSpeedSlider = CreateRuntimeSlider(trackingTabPanel.transform, "MinHandSpeedSlider",
                new Vector2(0f, 96f), new Vector2(600f, 30f), 80f, 320f, 155f, false);

            minHandThicknessValueText = CreateRuntimeText(trackingTabPanel.transform, "MinHandThicknessValueText",
                "GĘSTOŚĆ DETEKCJI DŁONI: 260", font, 25,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 50f), new Vector2(760f, 36f));
            minHandThicknessSlider = CreateRuntimeSlider(trackingTabPanel.transform, "MinHandThicknessSlider",
                new Vector2(0f, 14f), new Vector2(600f, 30f), 140f, 480f, 260f, false);

            sensitivityValueText = CreateRuntimeText(trackingTabPanel.transform, "SensitivityValueText",
                "PROMIEŃ CIĘCIA DŁONI: 1.0x", font, 25,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, -32f), new Vector2(760f, 36f));
            sensitivitySlider = CreateRuntimeSlider(trackingTabPanel.transform, "SensitivitySlider",
                new Vector2(0f, -68f), new Vector2(600f, 30f), 0.4f, 2.0f, 1.0f, false);

            btnToggleMaxHands = CreateRuntimeButton(trackingTabPanel.transform, "Btn_ToggleMaxHands", bannerSprite,
                "MAKS. DŁONIE: <color=#65FF45>2 DŁONIE</color>", font, 22,
                new Color(1.0f, 0.95f, 0.82f), new Vector2(-205f, -132f), new Vector2(380f, 56f));
            maxHandsToggleText = btnToggleMaxHands.GetComponentInChildren<Text>(true);

            btnToggleSwayFilter = CreateRuntimeButton(trackingTabPanel.transform, "Btn_ToggleSwayFilter", bannerSprite,
                "FILTR CIAŁA: <color=#65FF45>WŁĄCZONY</color>", font, 22,
                new Color(1.0f, 0.95f, 0.82f), new Vector2(205f, -132f), new Vector2(380f, 56f));
            swayFilterToggleText = btnToggleSwayFilter.GetComponentInChildren<Text>(true);

            btnToggleDenoise = CreateRuntimeButton(trackingTabPanel.transform, "Btn_ToggleDenoise", bannerSprite,
                "REDUKCJA SZUMU: <color=#65FF45>WŁĄCZONA</color>", font, 22,
                new Color(1.0f, 0.95f, 0.82f), new Vector2(0f, -196f), new Vector2(460f, 56f));
            denoiseToggleText = btnToggleDenoise.GetComponentInChildren<Text>(true);

            // --- TAB 3: GAME ---
            gameTabPanel = CreateRuntimeContainer(board, "GameTabPanel");

            spawnRateValueText = CreateRuntimeText(gameTabPanel.transform, "SpawnRateValueText",
                "TEMPO WYRZUTU OWOCÓW: 1.0x", font, 24,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 136f), new Vector2(760f, 34f));
            spawnRateSlider = CreateRuntimeSlider(gameTabPanel.transform, "SpawnRateSlider",
                new Vector2(0f, 102f), new Vector2(600f, 28f), 0.5f, 2.0f, 1.0f, false);

            fruitSizeValueText = CreateRuntimeText(gameTabPanel.transform, "FruitSizeValueText",
                "ROZMIAR OWOCÓW: 1.0x", font, 24,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 60f), new Vector2(760f, 34f));
            fruitSizeSlider = CreateRuntimeSlider(gameTabPanel.transform, "FruitSizeSlider",
                new Vector2(0f, 26f), new Vector2(600f, 28f), 0.7f, 1.5f, 1.0f, false);

            maxLivesValueText = CreateRuntimeText(gameTabPanel.transform, "MaxLivesValueText",
                "LICZBA ŻYĆ NA START: 3 ♥", font, 24,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, -16f), new Vector2(760f, 34f));
            maxLivesSlider = CreateRuntimeSlider(gameTabPanel.transform, "MaxLivesSlider",
                new Vector2(-65f, -50f), new Vector2(470f, 28f), 1f, 5f, 3f, true);
            btnToggleLives = CreateRuntimeButton(gameTabPanel.transform, "Btn_ToggleLives", bannerSprite,
                "[<color=#65FF45>✔</color>] ♥", font, 22,
                new Color(1.0f, 0.95f, 0.82f), new Vector2(240f, -50f), new Vector2(115f, 44f));
            livesToggleText = btnToggleLives.GetComponentInChildren<Text>(true);

            bombChanceValueText = CreateRuntimeText(gameTabPanel.transform, "BombChanceValueText",
                "CZĘSTOTLIWOŚĆ BOMB: 18%", font, 24,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, -92f), new Vector2(760f, 34f));
            bombChanceSlider = CreateRuntimeSlider(gameTabPanel.transform, "BombChanceSlider",
                new Vector2(0f, -126f), new Vector2(600f, 28f), 0.05f, 0.45f, 0.18f, false);

            btnToggleBombs = CreateRuntimeButton(gameTabPanel.transform, "Btn_ToggleBombs", bannerSprite,
                "BOMBY W GRZE: <color=#65FF45>WŁĄCZONE</color>", font, 23,
                new Color(1.0f, 0.95f, 0.82f), new Vector2(0f, -190f), new Vector2(480f, 56f));
            bombsToggleText = btnToggleBombs.GetComponentInChildren<Text>(true);

            // --- FOOTER BUTTONS (DOMYŚLNE + ZAMKNIJ) ---
            btnResetDefaults = CreateRuntimeButton(board, "Btn_ResetDefaults", bannerSprite,
                "DOMYŚLNE", font, 28,
                new Color(1.0f, 0.82f, 0.45f), new Vector2(-195f, -275f), new Vector2(330f, 66f));

            btnCloseSettings = CreateRuntimeButton(board, "Btn_CloseSettings", bannerSprite,
                "ZAMKNIJ", font, 28,
                new Color(1.0f, 0.94f, 0.58f), new Vector2(195f, -275f), new Vector2(330f, 66f));
        }

        private static GameObject CreateRuntimeContainer(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return go;
        }

        private static Text CreateRuntimeText(Transform parent, string name, string content, Font font, int fontSize, Color color, Vector2 pos, Vector2 size)
        {
            GameObject txtObj = new GameObject(name);
            txtObj.transform.SetParent(parent, false);
            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Text txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.supportRichText = true;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;

            Outline ol = txtObj.AddComponent<Outline>();
            ol.effectColor = new Color(0.16f, 0.05f, 0.01f, 0.96f);
            ol.effectDistance = new Vector2(2.8f, -2.8f);

            Shadow sh = txtObj.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.90f);
            sh.effectDistance = new Vector2(4.0f, -4.0f);
            return txt;
        }

        private static Button CreateRuntimeButton(Transform parent, string name, Sprite sprite, string label, Font font, int fontSize, Color textColor, Vector2 pos, Vector2 size)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image img = btnObj.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : new Color(0.45f, 0.24f, 0.10f, 0.95f);
            img.raycastTarget = true;

            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;

            CreateRuntimeText(btnObj.transform, "Label", label, font, fontSize, textColor, Vector2.zero, size);
            return btn;
        }

        private static Dropdown CreateRuntimeDropdown(
            Transform parent,
            string name,
            Sprite bgSprite,
            Font font,
            Vector2 pos,
            Vector2 size,
            List<string> options,
            int defaultIndex)
        {
            GameObject ddObj = new GameObject(name);
            ddObj.transform.SetParent(parent, false);
            RectTransform rt = ddObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            Image bgImg = ddObj.AddComponent<Image>();
            bgImg.sprite = bgSprite;
            bgImg.color = bgSprite != null ? Color.white : new Color(0.32f, 0.17f, 0.07f, 0.98f);
            bgImg.raycastTarget = true;

            Dropdown dd = ddObj.AddComponent<Dropdown>();
            dd.targetGraphic = bgImg;

            Text captionLabel = CreateRuntimeText(ddObj.transform, "Label",
                options[Mathf.Clamp(defaultIndex, 0, options.Count - 1)], font, 26,
                new Color(1.0f, 0.94f, 0.58f), new Vector2(-15f, 0f), new Vector2(size.x - 60f, size.y));

            CreateRuntimeText(ddObj.transform, "Arrow", "▼", font, 22,
                new Color(1.0f, 0.88f, 0.32f), new Vector2(size.x * 0.5f - 34f, 0f), new Vector2(40f, size.y));

            // Dropdown Template (inactive until clicked)
            GameObject template = new GameObject("Template");
            template.transform.SetParent(ddObj.transform, false);
            RectTransform tmplRt = template.AddComponent<RectTransform>();
            tmplRt.anchorMin = new Vector2(0f, 0f);
            tmplRt.anchorMax = new Vector2(1f, 0f);
            tmplRt.pivot = new Vector2(0.5f, 1f);
            tmplRt.anchoredPosition = new Vector2(0f, -4f);
            tmplRt.sizeDelta = new Vector2(0f, 124f);

            Image tmplImg = template.AddComponent<Image>();
            tmplImg.color = new Color(0.18f, 0.09f, 0.03f, 0.98f);

            ScrollRect sr = template.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;

            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(template.transform, false);
            RectTransform vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.sizeDelta = Vector2.zero;
            vpRt.pivot = new Vector2(0f, 1f);

            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            Image vpImg = viewport.AddComponent<Image>();
            vpImg.color = Color.white;

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 60f);

            GameObject item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            RectTransform itemRt = item.AddComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.anchoredPosition = Vector2.zero;
            itemRt.sizeDelta = new Vector2(0f, 58f);

            GameObject itemBg = new GameObject("Item Background");
            itemBg.transform.SetParent(item.transform, false);
            RectTransform itemBgRt = itemBg.AddComponent<RectTransform>();
            itemBgRt.anchorMin = Vector2.zero;
            itemBgRt.anchorMax = Vector2.one;
            itemBgRt.sizeDelta = Vector2.zero;
            Image itemBgImg = itemBg.AddComponent<Image>();
            itemBgImg.color = new Color(0.28f, 0.15f, 0.06f, 0.98f);

            Text itemCheck = CreateRuntimeText(item.transform, "Item Checkmark", "✔", font, 22,
                new Color(0.40f, 0.96f, 0.32f), new Vector2(-size.x * 0.5f + 32f, 0f), new Vector2(40f, 54f));

            Text itemLabel = CreateRuntimeText(item.transform, "Item Label", "Option", font, 24,
                new Color(1.0f, 0.96f, 0.84f), Vector2.zero, new Vector2(size.x - 80f, 54f));

            Toggle itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBgImg;
            itemToggle.graphic = itemCheck;
            itemToggle.isOn = true;

            sr.content = contentRt;
            sr.viewport = vpRt;

            dd.template = tmplRt;
            dd.captionText = captionLabel;
            dd.itemText = itemLabel;
            dd.ClearOptions();
            dd.AddOptions(options);
            dd.SetValueWithoutNotify(Mathf.Clamp(defaultIndex, 0, options.Count - 1));
            dd.RefreshShownValue();

            template.SetActive(false);
            return dd;
        }

        private static Slider CreateRuntimeSlider(Transform parent, string name, Vector2 pos, Vector2 size, float minVal, float maxVal, float defaultVal, bool wholeNumbers)
        {
            GameObject sliderObj = new GameObject(name);
            sliderObj.transform.SetParent(parent, false);
            RectTransform rt = sliderObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderObj.transform, false);
            RectTransform bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.2f);
            bgRt.anchorMax = new Vector2(1f, 0.8f);
            bgRt.sizeDelta = Vector2.zero;
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.20f, 0.11f, 0.04f, 1f);

            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.2f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.8f);
            fillAreaRt.sizeDelta = new Vector2(-8f, 0f);

            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRt = fill.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.35f, 0.92f, 0.25f, 1f);
            fillImg.raycastTarget = false;

            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderObj.transform, false);
            RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.sizeDelta = new Vector2(-8f, 0f);

            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRt = handle.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.sizeDelta = new Vector2(22f, 0f);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = new Color(1.0f, 0.88f, 0.32f, 1f);

            Slider slider = sliderObj.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = wholeNumbers;
            slider.minValue = minVal;
            slider.maxValue = maxVal;
            slider.value = defaultVal;

            return slider;
        }
    }
}
