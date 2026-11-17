using System;
using System.Collections;
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
        public GameObject piecePrefab;
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
        public const float DefaultMusicVolume = 0.80f;
        public const float DefaultSfxVolume = 1.00f;
        public const bool DefaultBombsEnabled = true;
        public const float DefaultBombSpawnChance = 0.18f;
        public const float DefaultSpawnRateMultiplier = 1.0f;
        public const float DefaultFruitSizeMultiplier = 1.0f;
        public const int DefaultMaxLives = 3;
        public const bool DefaultLivesEnabled = true;

        [Header("General & Audio Settings")]
        [Tooltip("0 = English, 1 = Polski")]
        public int languageIndex = DefaultLanguageIndex;
        [Range(0f, 1f)]
        public float musicVolume = DefaultMusicVolume;
        [Range(0f, 1f)]
        public float sfxVolume = DefaultSfxVolume;
        public AudioClip menuMusicClip;
        public AudioClip lossCricketsClip;
        public AudioClip fruitSlicedClip;
        public AudioClip objectThrownClip;

        [Header("Fruit Prefabs (Whole + Sliced Halves)")]
        public List<FruitDefinition> fruitTypes = new List<FruitDefinition>();

        [Header("Bomb Prefab (Whole + Exploded Halves)")]
        public FruitDefinition bombDefinition;
        public bool bombsEnabled = DefaultBombsEnabled;
        [Range(0.05f, 0.45f)]
        public float bombSpawnChance = DefaultBombSpawnChance;

        [Header("Pomegranate Prefab (Multi-Slice Frenzy)")]
        public FruitDefinition pomegranateDefinition;

        [Header("Dragonfruit Prefab (Very Rare Side-Launched Golden Bonus)")]
        public FruitDefinition dragonfruitDefinition;

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
        public Slider musicVolumeSlider;
        public Text musicVolumeValueText;
        public Slider sfxVolumeSlider;
        public Text sfxVolumeValueText;

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
        private int _wavesSinceLastPomegranate;
        private int _wavesSinceLastDragonfruit;
        private bool _lastGameOverWasBomb;

        // Pomegranate Frenzy camera & slow-motion state
        private FruitItem _activePomegranate;
        private float _targetTimeScale = 1.0f;
        private Vector3 _baseCamPos = new Vector3(0f, 0f, -10f);
        private float _baseCamOrtho = 5.0f;
        private Vector3 _currentCamFocus = new Vector3(0f, 0f, -10f);
        private Vector3 _targetCamFocus = new Vector3(0f, 0f, -10f);
        private float _currentCamOrtho = 5.0f;
        private float _targetCamOrtho = 5.0f;
        private float _currentCamTilt;
        private float _targetCamTilt;
        private float _camShakeTimer;
        private float _camShakeIntensity;
        private static Sprite _pomegranateSplashSprite;
        private static Sprite _katanaSlashBeamSprite;
        private static Sprite _solidWhiteMaskSprite;
        private GameObject _activeScreenSplashObj;
        private RectTransform _logoRootRect;
        private Image _logoKatanaBeamImg;
        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private AudioSource _thrownSfxSource;

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
            EnsureStrawberryInFruitPool();
            EnsurePomegranateDefinitionReady();
            EnsureDragonfruitDefinitionReady();
            EnsureAudioReady();
            EnsureUIWired();
            SyncAllSettingsUI();
            SelectSettingsTab(SettingsTab.General);
            ShowMainMenu();
        }

        private void LoadSavedSettings()
        {
            bestScore = PlayerPrefs.GetInt("EventSlash_BestScore", 0);
            languageIndex = Mathf.Clamp(PlayerPrefs.GetInt("EventSlash_Language", DefaultLanguageIndex), 0, 1);
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("EventSlash_MusicVolume", DefaultMusicVolume));
            sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("EventSlash_SfxVolume", DefaultSfxVolume));

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
                cam.minHandSpeed = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_MinHandSpeed", EventCameraManager.DefaultMinHandSpeed), 80f, 800f);
                cam.minHandThickness = Mathf.Clamp(PlayerPrefs.GetFloat("EventSlash_MinHandThickness", EventCameraManager.DefaultMinHandThickness), 140f, 480f);
                cam.maxHands = Mathf.Clamp(PlayerPrefs.GetInt("EventSlash_MaxHands", EventCameraManager.DefaultMaxHands), 0, 2);
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
                    musicVolume = DefaultMusicVolume;
                    sfxVolume = DefaultSfxVolume;
                    PlayerPrefs.SetInt("EventSlash_Language", languageIndex);
                    PlayerPrefs.SetFloat("EventSlash_MusicVolume", musicVolume);
                    PlayerPrefs.SetFloat("EventSlash_SfxVolume", sfxVolume);
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
                    case "TRUSKAWKA":
                    case "STRAWBERRY":
                        return "STRAWBERRY";
                    case "SMOCZY OWOC":
                    case "DRAGONFRUIT":
                        return "DRAGONFRUIT";
                    case "GRANAT":
                    case "POMEGRANATE":
                        return "POMEGRANATE";
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
                    case "STRAWBERRY":
                    case "TRUSKAWKA":
                        return "TRUSKAWKA";
                    case "DRAGONFRUIT":
                    case "SMOCZY OWOC":
                        return "SMOCZY OWOC";
                    case "POMEGRANATE":
                    case "GRANAT":
                        return "GRANAT";
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

            if (musicVolumeSlider != null) musicVolumeSlider.SetValueWithoutNotify(musicVolume);
            OnMusicVolumeChanged(musicVolume);

            if (sfxVolumeSlider != null) sfxVolumeSlider.SetValueWithoutNotify(sfxVolume);
            OnSfxVolumeChanged(sfxVolume);

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
                if (_logoRootRect != null)
                {
                    float t = Time.unscaledTime;
                    float bobY = Mathf.Sin(t * 2.1f) * 4.5f;
                    float tiltZ = 4.5f + Mathf.Sin(t * 1.4f) * 0.85f;
                    _logoRootRect.anchoredPosition = new Vector2(-450f, 362f + bobY);
                    _logoRootRect.localRotation = Quaternion.Euler(0f, 0f, tiltZ);
                }
                if (_logoKatanaBeamImg != null)
                {
                    float pulse = 0.88f + 0.12f * Mathf.Sin(Time.unscaledTime * 5.5f);
                    _logoKatanaBeamImg.color = new Color(1f, 1f, 1f, pulse);
                }
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
                // Pause wave timer while a Pomegranate Frenzy is actively being sliced
                if (_activePomegranate == null || !_activePomegranate.IsInPomegranateFrenzy)
                {
                    _nextSpawnTimer -= Time.deltaTime;
                    if (_nextSpawnTimer <= 0f)
                    {
                        SpawnFruitWave();
                        float difficultyFactor = Mathf.Clamp(1.0f - (score * 0.008f), 0.55f, 1.0f);
                        float rateDiv = Mathf.Max(0.35f, spawnRateMultiplier);
                        _nextSpawnTimer = (UnityEngine.Random.Range(spawnIntervalMin, spawnIntervalMax) * difficultyFactor) / rateDiv;
                    }
                }

                UpdateCameraAndTimeEffects();
                AnimateScorePop();
                AnimateComboBanner();
                UpdateHUD();
            }
        }

        private void UpdateCameraAndTimeEffects()
        {
            float udt = Time.unscaledDeltaTime;

            if (_activePomegranate == null || !_activePomegranate.IsInPomegranateFrenzy)
            {
                _targetTimeScale = 1.0f;
                _targetCamOrtho = _baseCamOrtho;
                _targetCamTilt = 0f;
                _targetCamFocus = _baseCamPos;
            }
            else
            {
                _targetCamFocus = new Vector3(
                    Mathf.Clamp(_activePomegranate.transform.position.x * 0.30f, -1.7f, 1.7f),
                    Mathf.Clamp(_activePomegranate.transform.position.y * 0.25f, -0.95f, 0.95f),
                    _baseCamPos.z
                );
            }

            Time.timeScale = Mathf.Lerp(Time.timeScale, _targetTimeScale, udt * 12.0f);
            if (Mathf.Abs(Time.timeScale - _targetTimeScale) < 0.01f)
            {
                Time.timeScale = _targetTimeScale;
            }
            Time.fixedDeltaTime = 0.02f * Time.timeScale;

            Camera cam = Camera.main;
            if (cam == null) return;

            _currentCamOrtho = Mathf.Lerp(_currentCamOrtho, _targetCamOrtho, udt * 9.5f);
            _currentCamTilt = Mathf.Lerp(_currentCamTilt, _targetCamTilt, udt * 9.5f);
            _currentCamFocus = Vector3.Lerp(_currentCamFocus, _targetCamFocus, udt * 9.5f);

            Vector3 shakeOffset = Vector3.zero;
            float shakeRoll = 0f;
            if (_camShakeTimer > 0f)
            {
                _camShakeTimer -= udt;
                float damper = Mathf.Clamp01(_camShakeTimer / 0.20f);
                Vector2 rnd = UnityEngine.Random.insideUnitCircle * (_camShakeIntensity * damper);
                shakeOffset = new Vector3(rnd.x, rnd.y, 0f);
                shakeRoll = UnityEngine.Random.Range(-1.8f, 1.8f) * (_camShakeIntensity * 3.5f * damper);
            }

            cam.orthographicSize = _currentCamOrtho;
            cam.transform.position = _currentCamFocus + shakeOffset;
            cam.transform.rotation = Quaternion.Euler(0f, 0f, _currentCamTilt + shakeRoll);
        }

        private void ResetTimeAndCameraImmediate()
        {
            _activePomegranate = null;
            _targetTimeScale = 1.0f;
            Time.timeScale = 1.0f;
            Time.fixedDeltaTime = 0.02f;

            _currentCamOrtho = _baseCamOrtho;
            _targetCamOrtho = _baseCamOrtho;
            _currentCamTilt = 0f;
            _targetCamTilt = 0f;
            _currentCamFocus = _baseCamPos;
            _targetCamFocus = _baseCamPos;
            _camShakeTimer = 0f;
            _camShakeIntensity = 0f;

            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.orthographicSize = _baseCamOrtho;
                cam.transform.position = _baseCamPos;
                cam.transform.rotation = Quaternion.identity;
            }
        }

        private void AnimateScorePop()
        {
            if (scoreText == null) return;
            if (_scorePopTimer > 0f)
            {
                _scorePopTimer -= Time.unscaledDeltaTime;
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
                _comboDisplayTimer -= Time.unscaledDeltaTime;
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
            ResetTimeAndCameraImmediate();
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

            StartMenuMusic();
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
            StopMenuMusic();
            ResetTimeAndCameraImmediate();
            ClearActiveFruits();
            HideComboBannerImmediate();
            score = 0;
            lives = maxLives;
            _comboCount = 0;
            _comboDisplayTimer = 0f;
            _scorePopTimer = 0f;
            _wavesSinceLastBomb = 0;
            _wavesSinceLastPomegranate = 0;
            _wavesSinceLastDragonfruit = 0;
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
                SpawnSingleProjectile(def, false, false, halfWidth, i * 0.45f);
            }

            if (bombsEnabled)
            {
                _wavesSinceLastBomb++;
                bool shouldSpawnBomb = bombDefinition != null && bombDefinition.wholePrefab != null &&
                                       (_wavesSinceLastBomb >= 7 || (_wavesSinceLastBomb >= 3 && UnityEngine.Random.value < bombSpawnChance));
                if (shouldSpawnBomb)
                {
                    _wavesSinceLastBomb = 0;
                    SpawnSingleProjectile(bombDefinition, true, false, halfWidth, 0.25f);
                }
            }

            // Occasionally spawn the multi-slice Pomegranate (rare special event!)
            _wavesSinceLastPomegranate++;
            bool canSpawnPom = _activePomegranate == null &&
                               pomegranateDefinition != null &&
                               pomegranateDefinition.wholePrefab != null &&
                               (_wavesSinceLastPomegranate >= 26 || (_wavesSinceLastPomegranate >= 15 && UnityEngine.Random.value < 0.06f));
            if (canSpawnPom)
            {
                _wavesSinceLastPomegranate = 0;
                SpawnSingleProjectile(pomegranateDefinition, false, true, halfWidth * 0.65f, 0.15f);
            }

            // Very rarely spawn the golden Dragonfruit thrown horizontally from the left or right side!
            _wavesSinceLastDragonfruit++;
            bool canSpawnDragon = dragonfruitDefinition != null &&
                                  dragonfruitDefinition.wholePrefab != null &&
                                  (_wavesSinceLastDragonfruit >= 112 || (_wavesSinceLastDragonfruit >= 64 && UnityEngine.Random.value < 0.0125f));
            if (canSpawnDragon)
            {
                _wavesSinceLastDragonfruit = 0;
                SpawnDragonfruitFromSide(halfWidth);
            }
        }

        private void SpawnDragonfruitFromSide(float halfWidth)
        {
            if (dragonfruitDefinition == null || dragonfruitDefinition.wholePrefab == null)
                return;

            float side = UnityEngine.Random.value < 0.5f ? -1.0f : 1.0f;
            float spawnX = side * (halfWidth * 1.48f);
            float sideY = UnityEngine.Random.Range(-0.8f, 1.85f);
            Vector3 pos = new Vector3(spawnX, sideY, spawnZ - 0.18f);
            Quaternion rot = UnityEngine.Random.rotation;

            float sizeMult = Mathf.Clamp(fruitSizeMultiplier, 0.7f, 1.5f);
            GameObject obj = Instantiate(dragonfruitDefinition.wholePrefab, pos, rot);
            obj.SetActive(true);
            obj.name = "Dragonfruit_Projectile";
            obj.transform.localScale = Vector3.one * (dragonfruitDefinition.scale * sizeMult);

            FruitItem item = obj.GetComponent<FruitItem>();
            if (item == null) item = obj.AddComponent<FruitItem>();

            item.fruitName = dragonfruitDefinition.name;
            item.isBomb = false;
            item.isPomegranate = false;
            item.isDragonfruit = true;
            item.scorePoints = 100;
            item.halfAPrefab = dragonfruitDefinition.halfAPrefab;
            item.halfBPrefab = dragonfruitDefinition.halfBPrefab;
            item.juiceColor = dragonfruitDefinition.juiceColor;
            item.viewportHitRadius = 0.095f * sizeMult;

            // Thrown from the side across the screen with a graceful high-speed arc
            item.gravity = -9.2f;
            float vx = -side * UnityEngine.Random.Range(11.8f, 14.5f);
            float vy = UnityEngine.Random.Range(4.6f, 6.4f);
            item.velocity = new Vector3(vx, vy, 0f);
            item.angularVelocity = new Vector3(
                UnityEngine.Random.Range(-190f, 190f),
                UnityEngine.Random.Range(-220f, 220f),
                UnityEngine.Random.Range(-190f, 190f)
            );

            item.ApplyGoldenDragonfruitAura(0.088f);
        }

        private void SpawnSingleProjectile(FruitDefinition def, bool isBomb, bool isPomegranate, float halfWidth, float yOffset)
        {
            float spawnX = UnityEngine.Random.Range(-halfWidth, halfWidth);
            Vector3 pos = new Vector3(spawnX, spawnY - yOffset, spawnZ + UnityEngine.Random.Range(-0.25f, 0.25f));
            Quaternion rot = isBomb
                ? Quaternion.Euler(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 360f), UnityEngine.Random.Range(-20f, 20f))
                : UnityEngine.Random.rotation;

            float sizeMult = isBomb ? 1.0f : Mathf.Clamp(fruitSizeMultiplier, 0.7f, 1.5f);
            GameObject obj = Instantiate(def.wholePrefab, pos, rot);
            obj.SetActive(true);
            obj.name = isBomb ? "Bomb_Projectile" : (isPomegranate ? "Pomegranate_Projectile" : $"Fruit_{def.name}");
            obj.transform.localScale = Vector3.one * (def.scale * sizeMult);

            FruitItem item = obj.GetComponent<FruitItem>();
            if (item == null) item = obj.AddComponent<FruitItem>();

            item.fruitName = def.name;
            item.isBomb = isBomb;
            item.isPomegranate = isPomegranate;
            item.halfAPrefab = def.halfAPrefab;
            item.halfBPrefab = def.halfBPrefab;
            item.piecePrefab = def.piecePrefab;
            item.juiceColor = def.juiceColor;
            item.viewportHitRadius = isBomb ? 0.065f : (isPomegranate ? 0.105f * sizeMult : 0.085f * sizeMult);

            float targetX = UnityEngine.Random.Range(-halfWidth * 0.40f, halfWidth * 0.40f);
            float vx = (targetX - spawnX) * UnityEngine.Random.Range(0.45f, 0.68f);
            float vy = isPomegranate
                ? UnityEngine.Random.Range(13.8f, 14.8f)
                : UnityEngine.Random.Range(minLaunchSpeedY, maxLaunchSpeedY);

            item.velocity = new Vector3(vx, vy, 0f);
            item.angularVelocity = isBomb
                ? new Vector3(UnityEngine.Random.Range(-65f, 65f), UnityEngine.Random.Range(-140f, 140f), UnityEngine.Random.Range(-90f, 90f))
                : new Vector3(UnityEngine.Random.Range(-150f, 150f), UnityEngine.Random.Range(-180f, 180f), UnityEngine.Random.Range(-150f, 150f));

            if (isPomegranate)
            {
                _activePomegranate = item;
            }
            else if (!isBomb && UnityEngine.Random.value < 0.12f)
            {
                // Occasionally spawn a bonus fruit with a bold 2D white outline that awards 5x points!
                item.scorePoints = 5;
                item.Apply2DWhiteOutline(0.07f);
            }
        }

        public void OnFruitSliced(FruitItem fruit, Vector3 worldPos)
        {
            if (currentState != GameState.Playing) return;

            PlayFruitSlicedSfx();

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
            else if (fruit.isDragonfruit)
            {
                ShowNinjaCallout(
                    IsEnglish ? "RARE DRAGONFRUIT!" : "RZADKI SMOCZY OWOC!",
                    $"+{gained}   {GetLocalizedFruitName(fruit.fruitName)}",
                    new Color(1.0f, 0.84f, 0.14f),
                    new Color(1.0f, 0.96f, 0.45f),
                    1.25f
                );
            }
            else if (fruit.isBonusOutlined)
            {
                ShowNinjaCallout(
                    IsEnglish ? "BONUS FRUIT!" : "BONUSOWY OWOC!",
                    $"+{gained}   {GetLocalizedFruitName(fruit.fruitName)}",
                    Color.white,
                    new Color(1.0f, 0.92f, 0.25f),
                    1.05f
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

        public void OnPomegranateSliced(FruitItem pom, int sliceCount, Vector2 slashDir)
        {
            if (currentState != GameState.Playing) return;

            PlayFruitSlicedSfx();

            _activePomegranate = pom;
            _targetTimeScale = 0.25f;

            // Zoom in closer, tilt camera, and trigger a crisp camera shake on every slice!
            _targetCamOrtho = Mathf.Clamp(4.05f - sliceCount * 0.045f, 3.60f, 4.10f);
            float baseTilt = 5.5f;
            float rock = (sliceCount % 2 == 0 ? 1.8f : -1.8f);
            _targetCamTilt = baseTilt + rock;
            _camShakeTimer = 0.19f;
            _camShakeIntensity = Mathf.Clamp(0.20f + sliceCount * 0.015f, 0.20f, 0.40f);

            score += 1;
            _scorePopTimer = 0.22f;
            if (score > bestScore)
            {
                bestScore = score;
                PlayerPrefs.SetInt("EventSlash_BestScore", bestScore);
            }

            ShowNinjaCallout(
                IsEnglish ? "POMEGRANATE FRENZY!" : "FURIA GRANATU!",
                IsEnglish ? $"SLASH x{sliceCount}!" : $"CIĘCIE x{sliceCount}!",
                new Color(1.0f, 0.22f, 0.42f),
                new Color(1.0f, 0.92f, 0.25f),
                0.85f
            );

            UpdateHUD();
        }

        public void OnPomegranateExploded(FruitItem pom, int totalSlices)
        {
            if (currentState != GameState.Playing) return;

            _activePomegranate = null;
            _targetTimeScale = 1.0f;
            Time.timeScale = 1.0f;
            Time.fixedDeltaTime = 0.02f;

            _targetCamOrtho = _baseCamOrtho;
            _targetCamTilt = 0f;
            _targetCamFocus = _baseCamPos;
            _camShakeTimer = 0.42f;
            _camShakeIntensity = 0.52f;

            Vector3 origin = pom != null ? pom.transform.position : Vector3.zero;
            SpawnPomegranateScreenSplash(origin);

            // Slice every active fruit on screen and make all active bombs disappear!
            FruitItem[] allItems = FindObjectsByType<FruitItem>(FindObjectsSortMode.None);
            foreach (FruitItem item in allItems)
            {
                if (item == null || item == pom) continue;
                if (item.isBomb)
                {
                    item.DisappearBombWithSmoke();
                }
                else if (!item.isPomegranate)
                {
                    item.ForceSliceFromExplosion(origin);
                }
            }

            int bonus = Mathf.Max(3, totalSlices);
            score += bonus;
            _scorePopTimer = 0.28f;
            if (score > bestScore)
            {
                bestScore = score;
                PlayerPrefs.SetInt("EventSlash_BestScore", bestScore);
            }

            ShowNinjaCallout(
                IsEnglish ? "MEGA EXPLOSION!" : "MEGA WYBUCH!",
                IsEnglish ? $"{totalSlices} HITS!   +{bonus} PTS" : $"{totalSlices} CIĘĆ!   +{bonus} PKT",
                new Color(1.0f, 0.18f, 0.35f),
                new Color(1.0f, 0.95f, 0.28f),
                1.45f
            );

            UpdateHUD();
        }

        public void OnBombSliced(FruitItem bomb, Vector3 worldPos)
        {
            if (currentState != GameState.Playing) return;
            ResetTimeAndCameraImmediate();
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
            StopMenuMusic();
            PlayLossCricketsSfx();
            ResetTimeAndCameraImmediate();
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

            if (_activeScreenSplashObj != null)
            {
                Destroy(_activeScreenSplashObj);
                _activeScreenSplashObj = null;
            }
        }

        private void SpawnPomegranateScreenSplash(Vector3 worldOrigin)
        {
            if (hudPanel == null) return;

            if (_activeScreenSplashObj != null)
            {
                Destroy(_activeScreenSplashObj);
            }

            Sprite splashSprite = GetOrCreatePomegranateSplashSprite();

            GameObject container = new GameObject("PomegranateScreenSplash");
            container.transform.SetParent(hudPanel.transform, false);
            container.transform.SetAsFirstSibling();

            RectTransform rt = container.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            CanvasGroup cg = container.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;

            // Convert world explosion origin into reference 1920x1080 canvas coordinates
            Camera cam = Camera.main;
            float aspect = cam != null ? cam.aspect : (16f / 9f);
            float normX = Mathf.Clamp(worldOrigin.x / (10.0f * aspect), -0.35f, 0.35f);
            float normY = Mathf.Clamp(worldOrigin.y / 10.0f, -0.30f, 0.30f);
            Vector2 centerAnchored = new Vector2(normX * 1920f, normY * 1080f);

            // 1. Central large semi-transparent ruby juice splatter
            CreateSplashBlotch(
                container.transform,
                splashSprite,
                centerAnchored,
                new Vector2(1050f, 1050f),
                UnityEngine.Random.Range(0f, 360f),
                new Color(0.88f, 0.04f, 0.16f, 0.52f)
            );

            // 2. Surrounding smaller splashes across the screen
            Vector2[] offsets =
            {
                new Vector2(-460f, 240f),
                new Vector2(480f, 210f),
                new Vector2(-420f, -250f),
                new Vector2(440f, -230f)
            };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2 pos = centerAnchored * 0.5f + offsets[i] + new Vector2(UnityEngine.Random.Range(-70f, 70f), UnityEngine.Random.Range(-55f, 55f));
                float sz = UnityEngine.Random.Range(420f, 620f);
                CreateSplashBlotch(
                    container.transform,
                    splashSprite,
                    pos,
                    new Vector2(sz, sz),
                    UnityEngine.Random.Range(0f, 360f),
                    new Color(0.92f, 0.06f, 0.18f, 0.44f)
                );
            }

            _activeScreenSplashObj = container;
            StartCoroutine(AnimateScreenSplashCoroutine(cg, container));
        }

        private static void CreateSplashBlotch(Transform parent, Sprite sprite, Vector2 anchoredPos, Vector2 size, float rotDeg, Color col)
        {
            GameObject go = new GameObject("SplashBlotch");
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotDeg);

            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = col;
            img.raycastTarget = false;
        }

        private IEnumerator AnimateScreenSplashCoroutine(CanvasGroup cg, GameObject splashObj)
        {
            // Fast pop-in (0.07s)
            float t = 0f;
            while (t < 0.07f)
            {
                if (cg == null) yield break;
                t += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Clamp01(t / 0.07f);
                yield return null;
            }
            if (cg != null) cg.alpha = 1.0f;

            // Hold on screen for 1.0 second
            float hold = 0f;
            while (hold < 1.0f)
            {
                if (cg == null) yield break;
                hold += Time.unscaledDeltaTime;
                yield return null;
            }

            // Smoothly fade out over 0.75s
            float fade = 0f;
            const float fadeDuration = 0.75f;
            while (fade < fadeDuration)
            {
                if (cg == null) yield break;
                fade += Time.unscaledDeltaTime;
                cg.alpha = 1.0f - Mathf.Clamp01(fade / fadeDuration);
                yield return null;
            }

            if (splashObj != null)
            {
                if (_activeScreenSplashObj == splashObj)
                    _activeScreenSplashObj = null;
                Destroy(splashObj);
            }
        }

        private static Sprite GetOrCreatePomegranateSplashSprite()
        {
            if (_pomegranateSplashSprite != null)
                return _pomegranateSplashSprite;

            const int size = 256;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] cols = new Color[size * size];

            // Precompute satellite droplet positions around the main splatter
            Vector2[] droplets =
            {
                new Vector2(0.58f, 0.24f), new Vector2(-0.62f, 0.28f),
                new Vector2(0.48f, -0.52f), new Vector2(-0.54f, -0.46f),
                new Vector2(0.18f, 0.68f), new Vector2(-0.22f, -0.66f),
                new Vector2(0.72f, -0.12f), new Vector2(-0.74f, -0.08f),
                new Vector2(0.36f, 0.56f), new Vector2(-0.42f, 0.54f)
            };
            float[] dropletRadii = { 0.11f, 0.09f, 0.10f, 0.12f, 0.08f, 0.09f, 0.07f, 0.08f, 0.06f, 0.07f };

            for (int y = 0; y < size; y++)
            {
                float ny = (y / (float)(size - 1)) * 2f - 1f;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x / (float)(size - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    float ang = Mathf.Atan2(ny, nx);

                    // First design with gentler, less-spikey radial waves + smooth Perlin noise modulation
                    float rays = Mathf.Sin(ang * 5f) * 0.055f
                               + Mathf.Cos(ang * 8f + 1.1f) * 0.040f
                               + Mathf.Sin(ang * 13f + 2.4f) * 0.022f;
                    float noise = Mathf.PerlinNoise(nx * 2.8f + 4.1f, ny * 2.8f + 7.3f) * 0.26f;
                    float maxR = 0.42f + rays + noise;

                    float alpha = 0f;
                    if (r < maxR)
                    {
                        float edgeSoft = Mathf.Clamp01((maxR - r) / 0.08f);
                        float rimBoost = Mathf.Lerp(0.72f, 1.0f, Mathf.Clamp01(r / Mathf.Max(0.01f, maxR)));
                        alpha = edgeSoft * rimBoost;
                    }

                    // Add satellite droplets
                    Vector2 p = new Vector2(nx, ny);
                    for (int d = 0; d < droplets.Length; d++)
                    {
                        float dist = Vector2.Distance(p, droplets[d]);
                        if (dist < dropletRadii[d])
                        {
                            float dAlpha = Mathf.Clamp01((dropletRadii[d] - dist) / 0.03f);
                            alpha = Mathf.Max(alpha, dAlpha * 0.92f);
                        }
                    }

                    cols[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }

            tex.SetPixels(cols);
            tex.Apply();

            _pomegranateSplashSprite = Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                100f
            );
            return _pomegranateSplashSprite;
        }

        // ==================== GENERAL TAB & AUDIO HANDLERS ====================

        public void OnLanguageChanged(int index)
        {
            languageIndex = Mathf.Clamp(index, 0, 1);
            PlayerPrefs.SetInt("EventSlash_Language", languageIndex);
            PlayerPrefs.Save();
            SyncAllSettingsUI();
        }

        public void OnMusicVolumeChanged(float val)
        {
            musicVolume = Mathf.Clamp01(val);
            PlayerPrefs.SetFloat("EventSlash_MusicVolume", musicVolume);
            if (_musicSource != null)
            {
                _musicSource.volume = musicVolume;
            }
            if (musicVolumeValueText != null)
            {
                int pct = Mathf.RoundToInt(musicVolume * 100f);
                musicVolumeValueText.text = IsEnglish
                    ? $"MUSIC VOLUME: {pct}%"
                    : $"GŁOŚNOŚĆ MUZYKI: {pct}%";
            }
        }

        public void OnSfxVolumeChanged(float val)
        {
            sfxVolume = Mathf.Clamp01(val);
            PlayerPrefs.SetFloat("EventSlash_SfxVolume", sfxVolume);
            if (_sfxSource != null)
            {
                _sfxSource.volume = sfxVolume;
            }
            if (_thrownSfxSource != null)
            {
                _thrownSfxSource.volume = sfxVolume * 0.85f;
            }
            if (sfxVolumeValueText != null)
            {
                int pct = Mathf.RoundToInt(sfxVolume * 100f);
                sfxVolumeValueText.text = IsEnglish
                    ? $"SFX VOLUME: {pct}%"
                    : $"EFEKTY DŹWIĘKOWE: {pct}%";
            }
        }

        private void EnsureAudioReady()
        {
#if UNITY_EDITOR
            if (menuMusicClip == null)
                menuMusicClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Music/local_forecast.wav");
            if (lossCricketsClip == null)
                lossCricketsClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/crickets.wav");
            if (fruitSlicedClip == null)
                fruitSlicedClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/fruit_sliced.wav");
            if (objectThrownClip == null)
                objectThrownClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/object_thrown.wav");
#endif

            if (_musicSource == null)
            {
                GameObject musicObj = new GameObject("MenuMusicAudioSource");
                musicObj.transform.SetParent(transform, false);
                _musicSource = musicObj.AddComponent<AudioSource>();
                _musicSource.playOnAwake = false;
                _musicSource.loop = true;
                _musicSource.spatialBlend = 0f;
                _musicSource.volume = musicVolume;
            }

            if (_sfxSource == null)
            {
                GameObject sfxObj = new GameObject("SfxAudioSource");
                sfxObj.transform.SetParent(transform, false);
                _sfxSource = sfxObj.AddComponent<AudioSource>();
                _sfxSource.playOnAwake = false;
                _sfxSource.loop = false;
                _sfxSource.spatialBlend = 0f;
                _sfxSource.volume = sfxVolume;
            }

            if (_thrownSfxSource == null)
            {
                GameObject thrownObj = new GameObject("ThrownObjectSfxAudioSource");
                thrownObj.transform.SetParent(transform, false);
                _thrownSfxSource = thrownObj.AddComponent<AudioSource>();
                _thrownSfxSource.playOnAwake = false;
                _thrownSfxSource.loop = false;
                _thrownSfxSource.spatialBlend = 0f;
                _thrownSfxSource.volume = sfxVolume * 0.85f;
            }
        }

        public void StartMenuMusic()
        {
            EnsureAudioReady();
            if (_musicSource == null || menuMusicClip == null) return;
            _musicSource.volume = musicVolume;
            if (_musicSource.clip != menuMusicClip)
            {
                _musicSource.clip = menuMusicClip;
            }
            if (!_musicSource.isPlaying)
            {
                _musicSource.Play();
            }
        }

        public void StopMenuMusic()
        {
            if (_musicSource != null && _musicSource.isPlaying)
            {
                _musicSource.Stop();
            }
        }

        public void PlayLossCricketsSfx()
        {
            EnsureAudioReady();
            if (_sfxSource != null && lossCricketsClip != null && sfxVolume > 0.001f)
            {
                _sfxSource.PlayOneShot(lossCricketsClip, sfxVolume);
            }
        }

        public void PlayFruitSlicedSfx()
        {
            EnsureAudioReady();
            if (_sfxSource != null && fruitSlicedClip != null && sfxVolume > 0.001f)
            {
                _sfxSource.PlayOneShot(fruitSlicedClip, sfxVolume);
            }
        }

        public void PlayObjectThrownSfx()
        {
            EnsureAudioReady();
            if (_thrownSfxSource != null && objectThrownClip != null && sfxVolume > 0.001f)
            {
                _thrownSfxSource.Stop();
                _thrownSfxSource.clip = objectThrownClip;
                _thrownSfxSource.volume = sfxVolume * 0.85f;
                _thrownSfxSource.Play();
            }
        }

        // ==================== TRACKING TAB HANDLERS ====================

        public void OnMinHandSpeedChanged(float val)
        {
            float clamped = Mathf.Clamp(val, 80f, 800f);
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
            int cur = EventCameraManager.Instance.maxHands;
            // Cycle: 1 hand -> 2 hands -> 0 (no limit) -> 1 hand
            int next = cur == 1 ? 2 : (cur == 2 ? 0 : 1);
            EventCameraManager.Instance.maxHands = next;
            PlayerPrefs.SetInt("EventSlash_MaxHands", next);
            UpdateMaxHandsToggleUI();
        }

        private void UpdateMaxHandsToggleUI()
        {
            int mh = EventCameraManager.Instance != null ? EventCameraManager.Instance.maxHands : EventCameraManager.DefaultMaxHands;
            if (maxHandsToggleText != null)
            {
                if (IsEnglish)
                {
                    if (mh == 1)
                        maxHandsToggleText.text = "MAX HANDS: <color=#FFD245>1 HAND</color>";
                    else if (mh == 2)
                        maxHandsToggleText.text = "MAX HANDS: <color=#65FF45>2 HANDS</color>";
                    else
                        maxHandsToggleText.text = "MAX HANDS: <color=#45E6FF>NO LIMIT</color>";
                }
                else
                {
                    if (mh == 1)
                        maxHandsToggleText.text = "MAKS. DŁONIE: <color=#FFD245>1 DŁOŃ</color>";
                    else if (mh == 2)
                        maxHandsToggleText.text = "MAKS. DŁONIE: <color=#65FF45>2 DŁONIE</color>";
                    else
                        maxHandsToggleText.text = "MAKS. DŁONIE: <color=#45E6FF>BEZ LIMITU</color>";
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

            // Automatically upgrade SettingsDojoBoard if the scene doesn't have the 3-tab layout (General + Tracking + Game) or Audio sliders yet
            if (settingsPopupPanel != null)
            {
                Transform existingGeneral = settingsPopupPanel.transform.Find("SettingsDojoBoard/GeneralTabPanel");
                Transform existingMusicSlider = settingsPopupPanel.transform.Find("SettingsDojoBoard/GeneralTabPanel/MusicVolumeSlider");
                Transform existingLivesCheckbox = settingsPopupPanel.transform.Find("SettingsDojoBoard/GameTabPanel/Btn_ToggleLives");
                if (existingGeneral == null || existingMusicSlider == null || existingLivesCheckbox == null)
                {
                    RebuildTabbedSettingsPopupRuntime();
                }
            }

            UpgradeMainMenuLogoRuntime();

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
                    case "MusicVolumeSlider": musicVolumeSlider = s; break;
                    case "SfxVolumeSlider": sfxVolumeSlider = s; break;
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
                    case "MusicVolumeValueText": musicVolumeValueText = t; break;
                    case "SfxVolumeValueText": sfxVolumeValueText = t; break;
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

            if (minHandSpeedSlider != null)
            {
                minHandSpeedSlider.maxValue = 800f;
            }
            WireSlider(musicVolumeSlider, OnMusicVolumeChanged);
            WireSlider(sfxVolumeSlider, OnSfxVolumeChanged);
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
                "JĘZYK:", font, 28,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 128f), new Vector2(560f, 40f));

            languageDropdown = CreateRuntimeDropdown(generalTabPanel.transform, "LanguageDropdown",
                bannerSprite, font, new Vector2(0f, 70f), new Vector2(380f, 56f),
                new List<string> { "English", "Polski" }, languageIndex);

            musicVolumeValueText = CreateRuntimeText(generalTabPanel.transform, "MusicVolumeValueText",
                "GŁOŚNOŚĆ MUZYKI: 80%", font, 25,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, -6f), new Vector2(760f, 36f));
            musicVolumeSlider = CreateRuntimeSlider(generalTabPanel.transform, "MusicVolumeSlider",
                new Vector2(0f, -42f), new Vector2(600f, 30f), 0f, 1f, musicVolume, false);

            sfxVolumeValueText = CreateRuntimeText(generalTabPanel.transform, "SfxVolumeValueText",
                "EFEKTY DŹWIĘKOWE: 100%", font, 25,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, -96f), new Vector2(760f, 36f));
            sfxVolumeSlider = CreateRuntimeSlider(generalTabPanel.transform, "SfxVolumeSlider",
                new Vector2(0f, -132f), new Vector2(600f, 30f), 0f, 1f, sfxVolume, false);

            // Keep dropdown last in sibling order so its popup list renders above the audio sliders
            languageDropdown.transform.SetAsLastSibling();

            // --- TAB 2: TRACKING ---
            trackingTabPanel = CreateRuntimeContainer(board, "TrackingTabPanel");

            minHandSpeedValueText = CreateRuntimeText(trackingTabPanel.transform, "MinHandSpeedValueText",
                "MIN. PRĘDKOŚĆ RUCHU DŁONI: 155 px/s", font, 25,
                new Color(1.0f, 0.96f, 0.84f), new Vector2(0f, 132f), new Vector2(760f, 36f));
            minHandSpeedSlider = CreateRuntimeSlider(trackingTabPanel.transform, "MinHandSpeedSlider",
                new Vector2(0f, 96f), new Vector2(600f, 30f), 80f, 800f, 155f, false);

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

        private void EnsureStrawberryInFruitPool()
        {
            if (fruitTypes == null)
            {
                fruitTypes = new List<FruitDefinition>();
            }

            foreach (FruitDefinition def in fruitTypes)
            {
                if (def != null && !string.IsNullOrEmpty(def.name))
                {
                    string n = def.name.Trim().ToUpperInvariant();
                    if ((n == "TRUSKAWKA" || n == "STRAWBERRY") && def.wholePrefab != null)
                        return;
                }
            }

            FruitDefinition strawberryDef = new FruitDefinition
            {
                name = "Truskawka",
                juiceColor = new Color(0.92f, 0.08f, 0.16f),
                scale = 1.38f
            };

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Material skinMat = CreateLitMaterial(litShader, new Color(0.90f, 0.06f, 0.12f), 0.72f);
            Material fleshMat = CreateLitMaterial(litShader, new Color(0.90f, 0.06f, 0.12f), 0.65f);
            Material seedMat = CreateLitMaterial(litShader, new Color(0.96f, 0.82f, 0.22f), 0.65f);
            Material leafMat = CreateLitMaterial(litShader, new Color(0.18f, 0.58f, 0.12f), 0.58f);

#if UNITY_EDITOR
            GameObject wholePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Strawberry_Whole.prefab");
            GameObject halfAPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Strawberry_Half_A.prefab");
            GameObject halfBPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Strawberry_Half_B.prefab");
            if (wholePrefab != null && halfAPrefab != null && halfBPrefab != null)
            {
                strawberryDef.wholePrefab = wholePrefab;
                strawberryDef.halfAPrefab = halfAPrefab;
                strawberryDef.halfBPrefab = halfBPrefab;
                fruitTypes.Add(strawberryDef);
                return;
            }

            GameObject wholeFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Strawberry_Whole.fbx");
            GameObject halfAFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Strawberry_Half_A.fbx");
            GameObject halfBFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Strawberry_Half_B.fbx");
            if (wholeFbx != null && halfAFbx != null && halfBFbx != null)
            {
                GameObject fbxHolder = new GameObject("StrawberryFbxTemplates");
                fbxHolder.transform.SetParent(transform, false);
                fbxHolder.SetActive(false);

                Dictionary<string, Material> strawMatMap = new Dictionary<string, Material>
                {
                    { "Mat_Strawberry_Skin", skinMat },
                    { "Mat_Strawberry_Flesh", fleshMat },
                    { "Mat_Strawberry_Seed", seedMat },
                    { "Mat_Leaf_Green", leafMat }
                };

                GameObject wInst = Instantiate(wholeFbx, fbxHolder.transform);
                wInst.name = "Strawberry_Whole";
                ApplyMappedMaterials(wInst, strawMatMap, new[] { skinMat, seedMat, leafMat });

                GameObject aInst = Instantiate(halfAFbx, fbxHolder.transform);
                aInst.name = "Strawberry_Half_A";
                ApplyMappedMaterials(aInst, strawMatMap, new[] { skinMat, fleshMat, seedMat, leafMat });

                GameObject bInst = Instantiate(halfBFbx, fbxHolder.transform);
                bInst.name = "Strawberry_Half_B";
                ApplyMappedMaterials(bInst, strawMatMap, new[] { skinMat, fleshMat, seedMat, leafMat });

                strawberryDef.wholePrefab = wInst;
                strawberryDef.halfAPrefab = aInst;
                strawberryDef.halfBPrefab = bInst;
                fruitTypes.Add(strawberryDef);
                return;
            }
#endif

            // Runtime procedural fallback if FBX/Prefab is not present
            GameObject holder = new GameObject("StrawberryRuntimeTemplates");
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);

            GameObject wholeFallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wholeFallback.name = "Strawberry_Whole_Template";
            wholeFallback.transform.SetParent(holder.transform, false);
            wholeFallback.transform.localScale = new Vector3(0.95f, 1.15f, 0.95f);
            Destroy(wholeFallback.GetComponent<Collider>());
            wholeFallback.GetComponent<MeshRenderer>().sharedMaterial = skinMat;

            GameObject halfFallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halfFallback.name = "Strawberry_Half_Template";
            halfFallback.transform.SetParent(holder.transform, false);
            halfFallback.transform.localScale = new Vector3(0.48f, 1.12f, 0.92f);
            Destroy(halfFallback.GetComponent<Collider>());
            halfFallback.GetComponent<MeshRenderer>().sharedMaterial = skinMat;

            strawberryDef.wholePrefab = wholeFallback;
            strawberryDef.halfAPrefab = halfFallback;
            strawberryDef.halfBPrefab = halfFallback;
            fruitTypes.Add(strawberryDef);
        }

        private void EnsurePomegranateDefinitionReady()
        {
            if (pomegranateDefinition != null && pomegranateDefinition.wholePrefab != null && pomegranateDefinition.piecePrefab != null)
            {
                return;
            }

            pomegranateDefinition = new FruitDefinition
            {
                name = "Granat",
                juiceColor = new Color(0.92f, 0.06f, 0.18f),
                scale = 1.48f
            };

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Material skinMat = CreateLitMaterial(litShader, new Color(0.76f, 0.06f, 0.16f), 0.65f);
            Material crownMat = CreateLitMaterial(litShader, new Color(0.56f, 0.04f, 0.10f), 0.50f);
            Material pithMat = CreateLitMaterial(litShader, new Color(0.96f, 0.88f, 0.74f), 0.35f);
            Material arilMat = CreateLitMaterial(litShader, new Color(0.94f, 0.04f, 0.15f), 0.85f);

#if UNITY_EDITOR
            GameObject wholePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Pomegranate_Whole.prefab");
            GameObject halfAPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Pomegranate_Half_A.prefab");
            GameObject halfBPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Pomegranate_Half_B.prefab");
            GameObject piecePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Pomegranate_Piece.prefab");
            if (wholePrefab != null && piecePrefab != null)
            {
                pomegranateDefinition.wholePrefab = wholePrefab;
                pomegranateDefinition.halfAPrefab = halfAPrefab;
                pomegranateDefinition.halfBPrefab = halfBPrefab;
                pomegranateDefinition.piecePrefab = piecePrefab;
                return;
            }

            GameObject wholeFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Pomegranate_Whole.fbx");
            GameObject pieceFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Pomegranate_Piece.fbx");
            if (wholeFbx != null && pieceFbx != null)
            {
                GameObject fbxHolder = new GameObject("PomegranateFbxTemplates");
                fbxHolder.transform.SetParent(transform, false);
                fbxHolder.SetActive(false);

                Dictionary<string, Material> pomMatMap = new Dictionary<string, Material>
                {
                    { "Mat_Pomegranate_Skin", skinMat },
                    { "Mat_Pomegranate_Crown", crownMat },
                    { "Mat_Pomegranate_Pith", pithMat },
                    { "Mat_Pomegranate_Aril", arilMat }
                };

                GameObject wInst = Instantiate(wholeFbx, fbxHolder.transform);
                wInst.name = "Pomegranate_Whole";
                ApplyMappedMaterials(wInst, pomMatMap, new[] { skinMat, crownMat });

                GameObject pInst = Instantiate(pieceFbx, fbxHolder.transform);
                pInst.name = "Pomegranate_Piece";
                ApplyMappedMaterials(pInst, pomMatMap, new[] { skinMat, pithMat, arilMat });

                pomegranateDefinition.wholePrefab = wInst;
                pomegranateDefinition.halfAPrefab = pInst;
                pomegranateDefinition.halfBPrefab = pInst;
                pomegranateDefinition.piecePrefab = pInst;
                return;
            }
#endif

            // Build procedural 3D Pomegranate & Piece templates at runtime as a universal fallback

            GameObject templateHolder = new GameObject("PomegranateRuntimeTemplates");
            templateHolder.transform.SetParent(transform, false);
            templateHolder.SetActive(false);

            // 1. Whole Pomegranate template (body sphere + 6-pointed calyx crown)
            GameObject wholeObj = new GameObject("Pomegranate_Whole_Template");
            wholeObj.transform.SetParent(templateHolder.transform, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(wholeObj.transform, false);
            body.transform.localScale = new Vector3(1.12f, 1.06f, 1.12f);
            Destroy(body.GetComponent<Collider>());
            body.GetComponent<MeshRenderer>().sharedMaterial = skinMat;

            GameObject crownBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            crownBase.name = "CrownBase";
            crownBase.transform.SetParent(wholeObj.transform, false);
            crownBase.transform.localPosition = new Vector3(0f, 0.54f, 0f);
            crownBase.transform.localScale = new Vector3(0.34f, 0.10f, 0.34f);
            Destroy(crownBase.GetComponent<Collider>());
            crownBase.GetComponent<MeshRenderer>().sharedMaterial = crownMat;

            for (int i = 0; i < 6; i++)
            {
                float ang = i * 60f * Mathf.Deg2Rad;
                GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tip.name = $"CrownPetal_{i}";
                tip.transform.SetParent(wholeObj.transform, false);
                tip.transform.localPosition = new Vector3(Mathf.Cos(ang) * 0.14f, 0.63f, Mathf.Sin(ang) * 0.14f);
                tip.transform.localRotation = Quaternion.Euler(22f, -i * 60f, 45f);
                tip.transform.localScale = new Vector3(0.11f, 0.15f, 0.06f);
                Destroy(tip.GetComponent<Collider>());
                tip.GetComponent<MeshRenderer>().sharedMaterial = crownMat;
            }

            // 2. Exploded Pomegranate Piece template (crimson rind wedge + pith + glossy ruby aril seeds)
            GameObject pieceObj = new GameObject("Pomegranate_Piece_Template");
            pieceObj.transform.SetParent(templateHolder.transform, false);

            GameObject rindChunk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rindChunk.name = "RindWedge";
            rindChunk.transform.SetParent(pieceObj.transform, false);
            rindChunk.transform.localScale = new Vector3(0.46f, 0.28f, 0.38f);
            Destroy(rindChunk.GetComponent<Collider>());
            rindChunk.GetComponent<MeshRenderer>().sharedMaterial = skinMat;

            GameObject pithLayer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pithLayer.name = "PithCore";
            pithLayer.transform.SetParent(pieceObj.transform, false);
            pithLayer.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            pithLayer.transform.localScale = new Vector3(0.38f, 0.22f, 0.32f);
            Destroy(pithLayer.GetComponent<Collider>());
            pithLayer.GetComponent<MeshRenderer>().sharedMaterial = pithMat;

            Vector3[] arilOffsets =
            {
                new Vector3(0f, 0.15f, 0f),
                new Vector3(0.10f, 0.12f, 0.05f),
                new Vector3(-0.10f, 0.12f, 0.04f),
                new Vector3(0.06f, 0.12f, -0.07f),
                new Vector3(-0.07f, 0.11f, -0.06f)
            };
            for (int i = 0; i < arilOffsets.Length; i++)
            {
                GameObject seed = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                seed.name = $"Aril_{i}";
                seed.transform.SetParent(pieceObj.transform, false);
                seed.transform.localPosition = arilOffsets[i];
                seed.transform.localScale = new Vector3(0.13f, 0.15f, 0.11f);
                Destroy(seed.GetComponent<Collider>());
                seed.GetComponent<MeshRenderer>().sharedMaterial = arilMat;
            }

            pomegranateDefinition.wholePrefab = wholeObj;
            pomegranateDefinition.halfAPrefab = pieceObj;
            pomegranateDefinition.halfBPrefab = pieceObj;
            pomegranateDefinition.piecePrefab = pieceObj;
        }

        private void EnsureDragonfruitDefinitionReady()
        {
            if (dragonfruitDefinition != null && dragonfruitDefinition.wholePrefab != null)
            {
                return;
            }

            dragonfruitDefinition = new FruitDefinition
            {
                name = "Smoczy Owoc",
                juiceColor = new Color(0.95f, 0.12f, 0.48f),
                scale = 1.42f
            };

            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Material skinMat = CreateLitMaterial(litShader, new Color(0.94f, 0.07f, 0.40f), 0.68f);
            Material scaleTipMat = CreateLitMaterial(litShader, new Color(0.38f, 0.84f, 0.14f), 0.58f);
            Material fleshMat = CreateLitMaterial(litShader, new Color(0.97f, 0.97f, 0.98f), 0.55f);
            Material seedMat = CreateLitMaterial(litShader, new Color(0.05f, 0.05f, 0.05f), 0.80f);

#if UNITY_EDITOR
            GameObject wholePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Dragonfruit_Whole.prefab");
            GameObject halfAPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Dragonfruit_Half_A.prefab");
            GameObject halfBPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Fruits/Dragonfruit_Half_B.prefab");
            if (wholePrefab != null && halfAPrefab != null && halfBPrefab != null)
            {
                dragonfruitDefinition.wholePrefab = wholePrefab;
                dragonfruitDefinition.halfAPrefab = halfAPrefab;
                dragonfruitDefinition.halfBPrefab = halfBPrefab;
                return;
            }

            GameObject wholeFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Dragonfruit_Whole.fbx");
            GameObject halfAFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Dragonfruit_Half_A.fbx");
            GameObject halfBFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Fruits/Dragonfruit_Half_B.fbx");
            if (wholeFbx != null && halfAFbx != null && halfBFbx != null)
            {
                GameObject fbxHolder = new GameObject("DragonfruitFbxTemplates");
                fbxHolder.transform.SetParent(transform, false);
                fbxHolder.SetActive(false);

                Dictionary<string, Material> dragonMatMap = new Dictionary<string, Material>
                {
                    { "Mat_Dragonfruit_Skin", skinMat },
                    { "Mat_Dragonfruit_ScaleTip", scaleTipMat },
                    { "Mat_Dragonfruit_Flesh", fleshMat },
                    { "Mat_Seed_Black", seedMat }
                };

                GameObject wInst = Instantiate(wholeFbx, fbxHolder.transform);
                wInst.name = "Dragonfruit_Whole";
                ApplyMappedMaterials(wInst, dragonMatMap, new[] { skinMat, scaleTipMat });

                GameObject aInst = Instantiate(halfAFbx, fbxHolder.transform);
                aInst.name = "Dragonfruit_Half_A";
                ApplyMappedMaterials(aInst, dragonMatMap, new[] { skinMat, scaleTipMat, fleshMat, seedMat });

                GameObject bInst = Instantiate(halfBFbx, fbxHolder.transform);
                bInst.name = "Dragonfruit_Half_B";
                ApplyMappedMaterials(bInst, dragonMatMap, new[] { skinMat, scaleTipMat, fleshMat, seedMat });

                dragonfruitDefinition.wholePrefab = wInst;
                dragonfruitDefinition.halfAPrefab = aInst;
                dragonfruitDefinition.halfBPrefab = bInst;
                return;
            }
#endif

            // Runtime procedural fallback
            GameObject holder = new GameObject("DragonfruitRuntimeTemplates");
            holder.transform.SetParent(transform, false);
            holder.SetActive(false);

            GameObject wholeFallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            wholeFallback.name = "Dragonfruit_Whole_Template";
            wholeFallback.transform.SetParent(holder.transform, false);
            wholeFallback.transform.localScale = new Vector3(0.95f, 1.25f, 0.95f);
            Destroy(wholeFallback.GetComponent<Collider>());
            wholeFallback.GetComponent<MeshRenderer>().sharedMaterial = skinMat;

            GameObject halfFallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halfFallback.name = "Dragonfruit_Half_Template";
            halfFallback.transform.SetParent(holder.transform, false);
            halfFallback.transform.localScale = new Vector3(0.48f, 1.22f, 0.92f);
            Destroy(halfFallback.GetComponent<Collider>());
            halfFallback.GetComponent<MeshRenderer>().sharedMaterial = fleshMat;

            dragonfruitDefinition.wholePrefab = wholeFallback;
            dragonfruitDefinition.halfAPrefab = halfFallback;
            dragonfruitDefinition.halfBPrefab = halfFallback;
        }

        private static void ApplyMappedMaterials(GameObject inst, Dictionary<string, Material> matMap, Material[] fallbackOrder)
        {
            MeshRenderer mr = inst.GetComponentInChildren<MeshRenderer>();
            if (mr == null) return;

            Material[] shared = mr.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                string cleanName = shared[i] != null ? shared[i].name.Split('.')[0].Replace(" (Instance)", "").Trim() : "";
                if (!string.IsNullOrEmpty(cleanName) && matMap.TryGetValue(cleanName, out Material mapped))
                {
                    shared[i] = mapped;
                }
                else if (fallbackOrder != null && i < fallbackOrder.Length)
                {
                    shared[i] = fallbackOrder[i];
                }
            }
            mr.sharedMaterials = shared;
        }

        private static Material CreateLitMaterial(Shader shader, Color col, float smoothness)
        {
            Material m = new Material(shader);
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        private void UpgradeMainMenuLogoRuntime()
        {
            if (menuPanel == null) return;
            Transform logoTr = menuPanel.transform.Find("FruitNinjaStyleLogo");
            if (logoTr == null) return;

            _logoRootRect = logoTr.GetComponent<RectTransform>();
            if (_logoRootRect != null)
            {
                _logoRootRect.anchoredPosition = new Vector2(-450f, 362f);
                _logoRootRect.sizeDelta = new Vector2(960f, 240f);
                _logoRootRect.localRotation = Quaternion.Euler(0f, 0f, 4.5f);
            }

            // Find existing font & sprites from the scene
            Font font = null;
            Text existingTxt = logoTr.GetComponentInChildren<Text>(true);
            if (existingTxt != null) font = existingTxt.font;
            if (font == null && menuProfileBestText != null) font = menuProfileBestText.font;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Sprite melonIconSprite = null;
            if (hudPanel != null)
            {
                Transform melonTr = hudPanel.transform.Find("NinjaScoreWidget/WatermelonScoreIcon");
                if (melonTr != null && melonTr.GetComponent<Image>() != null)
                {
                    melonIconSprite = melonTr.GetComponent<Image>().sprite;
                }
            }

            for (int i = logoTr.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(logoTr.GetChild(i).gameObject);
            }

            Sprite splashSprite = GetOrCreatePomegranateSplashSprite();
            Sprite katanaSprite = GetOrCreateKatanaSlashBeamSprite();
            Sprite maskSprite = GetOrCreateSolidWhiteMaskSprite();

            // 1. Subtle Fruit Juice Splatters behind the letters (no brown box!)
            CreateLogoImage(logoTr, "JuiceSplash_Left", splashSprite,
                new Color(0.95f, 0.12f, 0.22f, 0.72f), new Vector2(-215f, 12f), new Vector2(310f, 220f), 18f);
            CreateLogoImage(logoTr, "JuiceSplash_Right", splashSprite,
                new Color(1.00f, 0.50f, 0.06f, 0.68f), new Vector2(180f, -6f), new Vector2(320f, 220f), -28f);

            // 2. Sliced Watermelon Emblem tucked behind the center-top of the logo
            if (melonIconSprite != null)
            {
                CreateLogoImage(logoTr, "LogoWatermelonEmblem", melonIconSprite,
                    Color.white, new Vector2(-12f, 48f), new Vector2(128f, 128f), -14f);
            }

            // 3. 3D Multi-Layered "EVENT" Typography (Deep Block Shadow + Fiery Orange Bevel + Sunburst Gold Face)
            Vector2 eventPos = new Vector2(-205f, 8f);
            Vector2 eventSize = new Vector2(490f, 165f);

            Text evShadow = CreateLogoTextLayer(logoTr, "Logo_EVENT_BlockShadow", "EVENT", font, 118,
                new Color(0.10f, 0.03f, 0.01f, 0.98f), eventPos + new Vector2(7f, -9f), eventSize, 3.0f, new Color(0.05f, 0.01f, 0f));
            evShadow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 2.0f);

            Text evBevel = CreateLogoTextLayer(logoTr, "Logo_EVENT_OrangeBevel", "EVENT", font, 118,
                new Color(1.00f, 0.34f, 0.06f, 1.0f), eventPos + new Vector2(3.5f, -4.5f), eventSize, 3.5f, new Color(0.22f, 0.05f, 0.01f));
            evBevel.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 2.0f);

            Text evMain = CreateLogoTextLayer(logoTr, "Logo_EVENT_GoldFace", "EVENT", font, 118,
                new Color(1.00f, 0.90f, 0.18f, 1.0f), eventPos, eventSize, 4.2f, new Color(0.20f, 0.06f, 0.01f));
            evMain.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 2.0f);

            // 4. Katana-Sliced "SLASH" Typography using Stencil Masks with a clean cut gap so halves never overlap
            GameObject slashContainer = new GameObject("Logo_SLASH_SlicedContainer");
            slashContainer.transform.SetParent(logoTr, false);
            RectTransform slashRt = slashContainer.AddComponent<RectTransform>();
            slashRt.anchorMin = new Vector2(0.5f, 0.5f);
            slashRt.anchorMax = new Vector2(0.5f, 0.5f);
            slashRt.pivot = new Vector2(0.5f, 0.5f);
            slashRt.anchoredPosition = new Vector2(175f, 2f);
            slashRt.sizeDelta = new Vector2(520f, 160f);
            slashRt.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

            // Bottom Half Stencil Mask (0% .. 47% of 160px -> midpoint at y = -42.4px)
            GameObject botMaskObj = CreateStencilSliceMask(slashContainer.transform, "Slash_BottomHalfMask",
                maskSprite, new Vector2(0f, 0f), new Vector2(1f, 0.47f));

            Vector2 botTextCenter = new Vector2(-5f, 40.4f);
            CreateLogoTextLayer(botMaskObj.transform, "SlashBot_BlockShadow", "SLASH", font, 118,
                new Color(0.03f, 0.07f, 0.16f, 0.98f), botTextCenter + new Vector2(6f, -8f), new Vector2(520f, 160f), 2.5f, new Color(0.01f, 0.03f, 0.08f));
            CreateLogoTextLayer(botMaskObj.transform, "SlashBot_CyanBevel", "SLASH", font, 118,
                new Color(0.10f, 0.68f, 1.00f, 1.0f), botTextCenter + new Vector2(3f, -4f), new Vector2(520f, 160f), 2.8f, new Color(0.04f, 0.12f, 0.24f));
            CreateLogoTextLayer(botMaskObj.transform, "SlashBot_SteelFace", "SLASH", font, 118,
                new Color(0.92f, 0.98f, 1.00f, 1.0f), botTextCenter, new Vector2(520f, 160f), 3.2f, new Color(0.05f, 0.12f, 0.22f));

            // Top Half Stencil Mask (53% .. 100% of 160px -> midpoint at y = +42.4px), shifted right (+8px) along cut
            GameObject topMaskObj = CreateStencilSliceMask(slashContainer.transform, "Slash_TopHalfMask",
                maskSprite, new Vector2(0f, 0.53f), new Vector2(1f, 1f));

            Vector2 topTextCenter = new Vector2(8f, -40.4f);
            CreateLogoTextLayer(topMaskObj.transform, "SlashTop_BlockShadow", "SLASH", font, 118,
                new Color(0.03f, 0.07f, 0.16f, 0.98f), topTextCenter + new Vector2(6f, -8f), new Vector2(520f, 160f), 2.5f, new Color(0.01f, 0.03f, 0.08f));
            CreateLogoTextLayer(topMaskObj.transform, "SlashTop_CyanBevel", "SLASH", font, 118,
                new Color(0.15f, 0.78f, 1.00f, 1.0f), topTextCenter + new Vector2(3f, -4f), new Vector2(520f, 160f), 2.8f, new Color(0.04f, 0.12f, 0.24f));
            CreateLogoTextLayer(topMaskObj.transform, "SlashTop_SteelFace", "SLASH", font, 118,
                new Color(1.00f, 1.00f, 1.00f, 1.0f), topTextCenter, new Vector2(520f, 160f), 3.2f, new Color(0.05f, 0.12f, 0.22f));

            // 5. Crisp Glowing Katana Blade Streak passing cleanly through the gap between the top and bottom halves
            _logoKatanaBeamImg = CreateLogoImage(slashContainer.transform, "KatanaSlashBeam", katanaSprite,
                Color.white, new Vector2(0f, 0f), new Vector2(620f, 32f), 0f);
        }

        private static GameObject CreateStencilSliceMask(Transform parent, string name, Sprite maskSprite, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject maskObj = new GameObject(name);
            maskObj.transform.SetParent(parent, false);
            RectTransform rt = maskObj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = maskObj.AddComponent<Image>();
            img.sprite = maskSprite;
            img.color = Color.white;
            img.raycastTarget = false;

            Mask mask = maskObj.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            return maskObj;
        }

        private static Sprite GetOrCreateSolidWhiteMaskSprite()
        {
            if (_solidWhiteMaskSprite != null)
                return _solidWhiteMaskSprite;

            Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] cols = new Color[16];
            for (int i = 0; i < 16; i++) cols[i] = Color.white;
            tex.SetPixels(cols);
            tex.Apply();

            _solidWhiteMaskSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            return _solidWhiteMaskSprite;
        }

        private static Image CreateLogoImage(Transform parent, string name, Sprite sprite, Color col, Vector2 pos, Vector2 size, float rotZ)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            rt.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            Image img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = col;
            img.raycastTarget = false;
            return img;
        }

        private static Text CreateLogoTextLayer(Transform parent, string name, string content, Font font, int fontSize, Color col, Vector2 pos, Vector2 size, float outlineWidth, Color outlineCol)
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
            txt.fontStyle = FontStyle.Italic;
            txt.color = col;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;

            Outline ol = txtObj.AddComponent<Outline>();
            ol.effectColor = new Color(outlineCol.r, outlineCol.g, outlineCol.b, 0.96f);
            ol.effectDistance = new Vector2(outlineWidth, -outlineWidth);
            return txt;
        }

        private static Sprite GetOrCreateKatanaSlashBeamSprite()
        {
            if (_katanaSlashBeamSprite != null)
                return _katanaSlashBeamSprite;

            const int w = 512;
            const int h = 64;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] cols = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float ny = (y / (float)(h - 1)) * 2f - 1f;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x / (float)(w - 1)) * 2f - 1f;

                    // Taper thickness toward both tips (-1 and +1) like a razor-sharp katana arc
                    float xEnv = Mathf.Pow(Mathf.Max(0f, 1.0f - nx * nx), 1.35f);
                    float curveOffset = Mathf.Sin(nx * 2.2f) * 0.08f;
                    float dy = Mathf.Abs(ny - curveOffset);

                    float coreWidth = 0.13f * xEnv;
                    float glowWidth = 0.68f * xEnv;

                    // Bright cross-star glint near the impact point (nx ~ 0.18)
                    float glintX = Mathf.Exp(-Mathf.Pow((nx - 0.18f) * 14f, 2f));
                    glowWidth += glintX * 0.32f;

                    Color c = Color.clear;
                    if (dy < glowWidth && glowWidth > 0.001f)
                    {
                        float coreFactor = Mathf.Clamp01(1.0f - (dy / Mathf.Max(0.001f, coreWidth)));
                        float glowFactor = Mathf.Pow(Mathf.Clamp01(1.0f - (dy / glowWidth)), 2.2f);

                        Color cyanGlow = new Color(0.18f, 0.88f, 1.00f, 1.0f);
                        Color whiteCore = new Color(1.00f, 1.00f, 1.00f, 1.0f);

                        c = Color.Lerp(cyanGlow, whiteCore, Mathf.Clamp01(coreFactor + glintX * 0.65f));
                        c.a = Mathf.Clamp01(glowFactor * 0.95f + coreFactor * 0.85f);
                    }

                    cols[y * w + x] = c;
                }
            }

            tex.SetPixels(cols);
            tex.Apply();

            _katanaSlashBeamSprite = Sprite.Create(
                tex,
                new Rect(0, 0, w, h),
                new Vector2(0.5f, 0.5f),
                100f
            );
            return _katanaSlashBeamSprite;
        }
    }
}
