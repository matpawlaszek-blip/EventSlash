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

    public class EventSlashGameManager : MonoBehaviour
    {
        public static EventSlashGameManager Instance { get; private set; }

        [Header("Fruit Prefabs (Whole + Sliced Halves)")]
        public List<FruitDefinition> fruitTypes = new List<FruitDefinition>();

        [Header("Bomb Prefab (Whole + Exploded Halves)")]
        public FruitDefinition bombDefinition;
        public bool bombsEnabled = true;
        [Range(0f, 1f)]
        public float bombSpawnChance = 0.18f;

        [Header("Gameplay Spawning Settings")]
        public float spawnIntervalMin = 1.15f;
        public float spawnIntervalMax = 2.10f;
        public float spawnY = -5.3f;
        public float spawnZ = 0.0f;
        public float minLaunchSpeedY = 13.6f;
        public float maxLaunchSpeedY = 16.0f;
        public int maxLives = 3;

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
        public Text gameOverScoreText;
        public Text gameOverBestText;

        [Header("Menu & Settings Elements")]
        public Text menuStatusText;
        public Text menuProfileBestText;
        public Slider sensitivitySlider;
        public Text sensitivityValueText;
        public Text bombsToggleText;
        public Text denoiseToggleText;

        [Header("Interactive 3D Fruit Selectors (Main Menu Only)")]
        public MenuFruitSelector liveModeSelector;
        public MenuFruitSelector rawModeSelector;

        [Header("Buttons (Explicitly Wired)")]
        public Button btnHudBackToMenu;
        public Button btnGameOverMenu;
        public Button btnToggleSettings;
        public Button btnCloseSettings;
        public Button btnToggleBombs;
        public Button btnToggleDenoise;

        private float _nextSpawnTimer;
        private float _lastSliceTime;
        private int _comboCount;
        private float _comboDisplayTimer;
        private float _comboTotalDuration = 1.15f;
        private Vector2 _comboBaseAnchoredPos = new Vector2(0f, 315f);
        private float _scorePopTimer;
        private int _wavesSinceLastBomb;

        private void Awake()
        {
            Application.runInBackground = true;
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            bestScore = PlayerPrefs.GetInt("EventSlash_BestScore", 0);
            bombsEnabled = PlayerPrefs.GetInt("EventSlash_BombsEnabled", 1) == 1;
            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.denoiseEnabled = PlayerPrefs.GetInt("EventSlash_DenoiseEnabled", 1) == 1;
            }
        }

        private void Start()
        {
            if (comboBannerRect != null)
            {
                _comboBaseAnchoredPos = comboBannerRect.anchoredPosition;
            }
            EnsureUIWired();
            ShowMainMenu();
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
                    menuProfileBestText.text = $"PROFIL NINJA  •  REKORD: {bestScore}";
                }
                if (menuStatusText != null && EventCameraManager.Instance != null)
                {
                    menuStatusText.text = $"Przeciągnij ostrzem przez wybrany owoc, aby rozpocząć! (Plik RAW: {EventCameraManager.Instance.activeRawFileName})";
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
                    _nextSpawnTimer = UnityEngine.Random.Range(spawnIntervalMin, spawnIntervalMax) * difficultyFactor;
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
                liveModeSelector.Initialize(StartLiveCameraGame);
            }
            if (rawModeSelector != null)
            {
                rawModeSelector.gameObject.SetActive(true);
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

            // Occasionally launch a Bomb alongside the fruit wave (only if bombs are enabled in Settings!)
            if (bombsEnabled)
            {
                _wavesSinceLastBomb++;
                bool shouldSpawnBomb = bombDefinition != null && bombDefinition.wholePrefab != null &&
                                       (_wavesSinceLastBomb >= 7 || (_wavesSinceLastBomb >= 4 && UnityEngine.Random.value < bombSpawnChance));
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

            GameObject obj = Instantiate(def.wholePrefab, pos, rot);
            obj.name = isBomb ? "Bomb_Projectile" : $"Fruit_{def.name}";
            obj.transform.localScale = Vector3.one * def.scale;

            FruitItem item = obj.GetComponent<FruitItem>();
            if (item == null) item = obj.AddComponent<FruitItem>();

            item.fruitName = def.name;
            item.isBomb = isBomb;
            item.halfAPrefab = def.halfAPrefab;
            item.halfBPrefab = def.halfBPrefab;
            item.juiceColor = def.juiceColor;
            item.viewportHitRadius = isBomb ? 0.065f : 0.085f;

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
                    "TECHNIKA NINJA!",
                    $"COMBO x{_comboCount}!   +{gained} PKT",
                    new Color(1.0f, 0.38f, 0.16f),
                    new Color(1.0f, 0.90f, 0.20f),
                    1.25f
                );
            }
            else
            {
                ShowNinjaCallout(
                    "CIĘCIE NINJA!",
                    $"+{gained}   {fruit.fruitName.ToUpper()}",
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

            // Slicing a bomb immediately ends the game!
            TriggerGameOver("WYBUCH BOMBY!");
        }

        public void OnFruitMissed(FruitItem fruit)
        {
            if (currentState != GameState.Playing) return;

            lives--;
            ShowNinjaCallout(
                "UTRATA ŻYCIA!",
                "UPUSZCZONO OWOC! (-1)",
                new Color(1.0f, 0.28f, 0.24f),
                new Color(1.0f, 0.55f, 0.45f),
                1.15f
            );

            UpdateHUD();

            if (lives <= 0)
            {
                TriggerGameOver("KONIEC GRY");
            }
        }

        private void TriggerGameOver(string titleReason = "KONIEC GRY")
        {
            currentState = GameState.GameOver;
            ClearActiveFruits();
            HideComboBannerImmediate();

            // Stop the event camera / video stream immediately so nothing keeps running or slicing!
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
            if (gameOverScoreText != null)
            {
                gameOverScoreText.text = $"{score}";
            }
            if (gameOverBestText != null)
            {
                gameOverBestText.text = $"NAJLEPSZY WYNIK: {bestScore}";
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
                bestScoreHudText.text = $"REKORD: {bestScore}";
            }

            if (livesText != null)
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

        public void OnSensitivityChanged(float val)
        {
            if (EventCameraManager.Instance != null)
            {
                EventCameraManager.Instance.sensitivity = val;
            }
            if (sensitivityValueText != null)
            {
                sensitivityValueText.text = $"CZUŁOŚĆ DŁONI: {val:F1}x";
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
                bombsToggleText.text = bombsEnabled
                    ? "BOMBY W GRZE: <color=#65FF45>WŁĄCZONE</color>"
                    : "BOMBY W GRZE: <color=#FF5545>WYŁĄCZONE</color>";
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
                denoiseToggleText.text = enabled
                    ? "REDUKCJA SZUMU: <color=#65FF45>WŁĄCZONA</color>"
                    : "REDUKCJA SZUMU: <color=#FF5545>WYŁĄCZONA</color>";
            }
        }

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

            Button[] allButtons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Button b in allButtons)
            {
                if (b.name == "Btn_HudBackToMenu") btnHudBackToMenu = b;
                else if (b.name == "Btn_GameOverMenu") btnGameOverMenu = b;
                else if (b.name == "Btn_ToggleSettings") btnToggleSettings = b;
                else if (b.name == "Btn_CloseSettings") btnCloseSettings = b;
                else if (b.name == "Btn_ToggleBombs") btnToggleBombs = b;
                else if (b.name == "Btn_ToggleDenoise") btnToggleDenoise = b;
            }

            if (btnHudBackToMenu != null)
            {
                btnHudBackToMenu.onClick.RemoveAllListeners();
                btnHudBackToMenu.onClick.AddListener(ShowMainMenu);
            }

            if (btnGameOverMenu != null)
            {
                btnGameOverMenu.onClick.RemoveAllListeners();
                btnGameOverMenu.onClick.AddListener(ShowMainMenu);
            }

            if (btnToggleSettings != null)
            {
                btnToggleSettings.onClick.RemoveAllListeners();
                btnToggleSettings.onClick.AddListener(ToggleSettingsPopup);
            }

            if (btnCloseSettings != null)
            {
                btnCloseSettings.onClick.RemoveAllListeners();
                btnCloseSettings.onClick.AddListener(CloseSettingsPopup);
            }

            if (btnToggleBombs != null)
            {
                btnToggleBombs.onClick.RemoveAllListeners();
                btnToggleBombs.onClick.AddListener(ToggleBombsEnabled);
                if (bombsToggleText == null)
                {
                    bombsToggleText = btnToggleBombs.GetComponentInChildren<Text>(true);
                }
            }
            UpdateBombsToggleUI();

            if (btnToggleDenoise != null)
            {
                btnToggleDenoise.onClick.RemoveAllListeners();
                btnToggleDenoise.onClick.AddListener(ToggleDenoiseEnabled);
                if (denoiseToggleText == null)
                {
                    denoiseToggleText = btnToggleDenoise.GetComponentInChildren<Text>(true);
                }
            }
            UpdateDenoiseToggleUI();

            if (sensitivitySlider != null)
            {
                sensitivitySlider.onValueChanged.RemoveAllListeners();
                sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
                OnSensitivityChanged(sensitivitySlider.value);
            }
        }
    }
}
