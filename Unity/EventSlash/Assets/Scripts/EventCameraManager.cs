using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Debug = UnityEngine.Debug;

namespace EventSlash
{
    public enum EventStreamMode
    {
        LiveCamera,
        RawRecording
    }

    public struct SlashCluster
    {
        public Vector2 viewportPos;
        public Vector2 velocity;
        public float intensity;
    }

    public class EventCameraManager : MonoBehaviour
    {
        public static EventCameraManager Instance { get; private set; }

        [Header("Event Camera Settings")]
        public Renderer screenRenderer;
        public int tcpPort = 5055;
        [Range(0.2f, 2.5f)]
        public float sensitivity = 1.0f;
        public bool denoiseEnabled = true;

        [Header("Runtime Status")]
        public EventStreamMode currentMode = EventStreamMode.RawRecording;
        public bool isConnected;
        public int camWidth = 640;
        public int camHeight = 480;
        public string statusMessage = "Gotowy";
        public string activeRawFileName = "";
        private string _resolvedRawFileArg = "";

        private Texture2D _eventTexture;
        private byte[] _latestFrameBytes;
        private bool _hasNewFrame;
        private readonly object _dataLock = new object();

        public const int GridW = 32;
        public const int GridH = 24;
        private readonly byte[] _motionGrid = new byte[GridW * GridH];
        private readonly List<SlashCluster> _activeClusters = new List<SlashCluster>();

        private Process _pythonProcess;
        private Thread _receiveThread;
        private volatile bool _running;

        // Mouse/touch fallback blade for convenient desktop testing alongside event camera
        private Vector2 _prevMouseViewport;
        private Vector2 _mouseVelocity;
        private bool _mouseDragging;

        private void Awake()
        {
            Application.runInBackground = true;
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            InitializeDefaultTexture();
            DiscoverRawRecording();
        }

        private void InitializeDefaultTexture()
        {
            _eventTexture = new Texture2D(camWidth, camHeight, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            byte[] wood = new byte[camWidth * camHeight * 3];
            for (int y = 0; y < camHeight; y++)
            {
                float ny = y / (float)camHeight;
                for (int x = 0; x < camWidth; x++)
                {
                    float nx = x / (float)camWidth;
                    float plankPos = nx * 8.0f;
                    float plankIdx = Mathf.Floor(plankPos);
                    float plankFrac = plankPos - plankIdx;

                    float grain1 = Mathf.Sin(ny * 38.0f + plankIdx * 1.9f + Mathf.Sin(nx * 45.0f + plankIdx * 3.1f) * 1.6f) * 0.5f + 0.5f;
                    float grain2 = Mathf.Cos(ny * 110.0f + nx * 24.0f + plankIdx * 2.7f) * 0.5f + 0.5f;
                    float plankTone = 0.94f + 0.10f * Mathf.Sin(plankIdx * 2.3f + 0.5f);
                    float tone = (0.86f + 0.10f * grain1 + 0.05f * grain2) * plankTone;

                    float seamDist = Mathf.Min(plankFrac, 1.0f - plankFrac);
                    float seamFactor = Mathf.Clamp(seamDist / 0.022f, 0.68f, 1.0f);
                    tone *= seamFactor;

                    int idx = (y * camWidth + x) * 3;
                    wood[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(218f * tone), 0, 255);
                    wood[idx + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(178f * tone), 0, 255);
                    wood[idx + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(124f * tone), 0, 255);
                }
            }
            _eventTexture.LoadRawTextureData(wood);
            _eventTexture.Apply(false);
            ApplyTextureToScreen();
        }

        public void ApplyTextureToScreen()
        {
            if (screenRenderer != null && _eventTexture != null)
            {
                Material mat = screenRenderer.material;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", _eventTexture);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", _eventTexture);
            }
        }

        public string GetEnvironmentFolderPath()
        {
            DirectoryInfo cur = new DirectoryInfo(Application.dataPath);
            while (cur != null)
            {
                try
                {
                    DirectoryInfo[] matches = cur.GetDirectories("Konfiguracja*");
                    if (matches.Length > 0)
                    {
                        return matches[0].FullName;
                    }
                }
                catch (Exception)
                {
                    // Ignore permission errors on root drives
                }
                cur = cur.Parent;
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Konfiguracja środowiska"));
        }

        public string GetConfigFilePath(string envDir)
        {
            DirectoryInfo cur = new DirectoryInfo(Application.dataPath);
            while (cur != null)
            {
                try
                {
                    string candidate = Path.Combine(cur.FullName, "config.txt");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception)
                {
                    // Ignore permission errors on root drives
                }
                cur = cur.Parent;
            }

            if (!string.IsNullOrEmpty(envDir))
            {
                string inEnv = Path.Combine(envDir, "config.txt");
                if (File.Exists(inEnv))
                {
                    return inEnv;
                }
                DirectoryInfo envParent = Directory.GetParent(envDir);
                if (envParent != null)
                {
                    string inRoot = Path.Combine(envParent.FullName, "config.txt");
                    if (File.Exists(inRoot))
                    {
                        return inRoot;
                    }
                }
            }
            return null;
        }

        private void ReadConfigValues(string envDir, out string configuredPython, out string configuredRawFile, out string configDir)
        {
            configuredPython = null;
            configuredRawFile = null;
            configDir = null;

            string configPath = GetConfigFilePath(envDir);
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
                return;

            try
            {
                configDir = Path.GetDirectoryName(configPath);
                string[] lines = File.ReadAllLines(configPath);
                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    string line = rawLine.Trim();
                    if (line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("//"))
                        continue;

                    int eqIdx = line.IndexOf('=');
                    if (eqIdx <= 0)
                        continue;

                    string key = line.Substring(0, eqIdx).Trim().ToUpperInvariant();
                    string val = line.Substring(eqIdx + 1).Trim().Trim('"', '\'').Trim();
                    if (string.IsNullOrEmpty(val))
                        continue;

                    if (key == "PYTHON_EXE" || key == "PYTHON_PATH" || key == "VENV_PATH")
                    {
                        configuredPython = val;
                    }
                    else if (key == "RAW_FILE" || key == "RAW_PATH")
                    {
                        configuredRawFile = val;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EventCameraManager] Could not read config.txt: {e.Message}");
            }
        }

        private static string TryResolvePythonCandidate(string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath))
                return null;

            try
            {
                if (File.Exists(candidatePath))
                    return Path.GetFullPath(candidatePath);

                if (Directory.Exists(candidatePath))
                {
                    string inScripts = Path.Combine(candidatePath, "Scripts", "python.exe");
                    if (File.Exists(inScripts))
                        return Path.GetFullPath(inScripts);

                    string directExe = Path.Combine(candidatePath, "python.exe");
                    if (File.Exists(directExe))
                        return Path.GetFullPath(directExe);
                }
            }
            catch (Exception)
            {
                // Ignore invalid path syntax
            }
            return null;
        }

        private string ResolvePythonExePath(string envDir, string configuredPython, string configDir)
        {
            string defaultPython = Path.Combine(envDir, ".venv", "Scripts", "python.exe");

            if (!string.IsNullOrEmpty(configuredPython))
            {
                if (Path.IsPathRooted(configuredPython))
                {
                    string resolved = TryResolvePythonCandidate(configuredPython);
                    if (resolved != null)
                        return resolved;
                }
                else
                {
                    string fromEnv = TryResolvePythonCandidate(Path.Combine(envDir, configuredPython));
                    if (fromEnv != null)
                        return fromEnv;

                    if (!string.IsNullOrEmpty(configDir))
                    {
                        string fromConfigDir = TryResolvePythonCandidate(Path.Combine(configDir, configuredPython));
                        if (fromConfigDir != null)
                            return fromConfigDir;
                    }
                }

                // If the path in config.txt comes from another PC and does not exist locally,
                // fall back to the local .venv if present so the project still runs smoothly.
                if (File.Exists(defaultPython))
                {
                    Debug.LogWarning($"[EventCameraManager] PYTHON_EXE from config.txt not found ('{configuredPython}'). Falling back to local '{defaultPython}'.");
                    return defaultPython;
                }

                return configuredPython;
            }

            return defaultPython;
        }

        public void DiscoverRawRecording()
        {
            try
            {
                string envDir = GetEnvironmentFolderPath();
                string rawDir = Path.Combine(envDir, "raw_recordings");

                ReadConfigValues(envDir, out _, out string configuredRawFile, out string configDir);

                if (!string.IsNullOrEmpty(configuredRawFile))
                {
                    string inRawDir = Path.Combine(rawDir, configuredRawFile);
                    string inEnvDir = Path.Combine(envDir, configuredRawFile);
                    string inConfigDir = !string.IsNullOrEmpty(configDir) ? Path.Combine(configDir, configuredRawFile) : null;

                    if (File.Exists(inRawDir))
                    {
                        activeRawFileName = Path.GetFileName(inRawDir);
                        _resolvedRawFileArg = $"raw_recordings/{activeRawFileName}";
                        return;
                    }
                    if (File.Exists(inEnvDir))
                    {
                        activeRawFileName = Path.GetFileName(inEnvDir);
                        _resolvedRawFileArg = configuredRawFile.Replace('\\', '/');
                        return;
                    }
                    if (!string.IsNullOrEmpty(inConfigDir) && File.Exists(inConfigDir))
                    {
                        activeRawFileName = Path.GetFileName(inConfigDir);
                        _resolvedRawFileArg = Path.GetFullPath(inConfigDir);
                        return;
                    }
                    if (Path.IsPathRooted(configuredRawFile) && File.Exists(configuredRawFile))
                    {
                        activeRawFileName = Path.GetFileName(configuredRawFile);
                        string fullRawDir = Path.GetFullPath(rawDir).TrimEnd('\\', '/');
                        string fullFileDir = Path.GetDirectoryName(Path.GetFullPath(configuredRawFile))?.TrimEnd('\\', '/') ?? "";
                        if (string.Equals(fullRawDir, fullFileDir, StringComparison.OrdinalIgnoreCase))
                        {
                            _resolvedRawFileArg = $"raw_recordings/{activeRawFileName}";
                        }
                        else
                        {
                            _resolvedRawFileArg = Path.GetFullPath(configuredRawFile);
                        }
                        return;
                    }

                    // Even if the file isn't on disk yet, keep the name from config.txt
                    activeRawFileName = Path.GetFileName(configuredRawFile);
                    _resolvedRawFileArg = $"raw_recordings/{activeRawFileName}";
                    Debug.LogWarning($"[EventCameraManager] RAW_FILE from config.txt not found ('{configuredRawFile}'). Scanning raw_recordings/ as fallback.");
                }

                if (Directory.Exists(rawDir))
                {
                    string[] files = Directory.GetFiles(rawDir, "*.raw");
                    if (files.Length > 0)
                    {
                        activeRawFileName = Path.GetFileName(files[0]);
                        _resolvedRawFileArg = $"raw_recordings/{activeRawFileName}";
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EventCameraManager] Could not scan raw_recordings: {e.Message}");
            }
        }

        public void StartStream(EventStreamMode mode)
        {
            StopStream();
            currentMode = mode;
            DiscoverRawRecording();

            string envDir = GetEnvironmentFolderPath();
            ReadConfigValues(envDir, out string configuredPython, out _, out string configDir);
            string pythonExe = ResolvePythonExePath(envDir, configuredPython, configDir);
            string serverScript = Path.Combine(envDir, "event_stream_server.py");

            if (!File.Exists(pythonExe))
            {
                statusMessage = $"Brak pliku Python: {pythonExe}";
                Debug.LogError(statusMessage);
                return;
            }

            string modeArg = mode == EventStreamMode.LiveCamera ? "live" : "raw";
            string rawRelativePath = !string.IsNullOrEmpty(_resolvedRawFileArg)
                ? _resolvedRawFileArg
                : $"raw_recordings/{activeRawFileName}";
            int denoiseArg = denoiseEnabled ? 1 : 0;
            string args = $"\"{serverScript}\" --mode {modeArg} --file \"{rawRelativePath}\" --port {tcpPort} --denoise {denoiseArg}";

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = args,
                    WorkingDirectory = envDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                psi.EnvironmentVariables["PATH"] = $"C:\\Program Files\\Prophesee\\bin;C:\\Program Files\\Prophesee\\third_party\\bin;{currentPath}";

                string currentPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "";
                psi.EnvironmentVariables["PYTHONPATH"] = $"C:\\Program Files\\Prophesee\\lib\\python3\\site-packages;{currentPythonPath}";
                psi.EnvironmentVariables["MV_HAL_PLUGIN_SEARCH_MODE"] = "PLUGIN_PATH_ONLY";
                psi.EnvironmentVariables["MV_HAL_PLUGIN_PATH"] = "C:\\Program Files\\CenturyArks\\plugins";
                psi.EnvironmentVariables["MV_HAL_DEBUG_PLUGIN"] = "1";

                _pythonProcess = new Process { StartInfo = psi };
                _pythonProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.Log($"[MetavisionPy] {e.Data}");
                };
                _pythonProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.LogWarning($"[MetavisionPy Err] {e.Data}");
                };

                _pythonProcess.Start();
                _pythonProcess.BeginOutputReadLine();
                _pythonProcess.BeginErrorReadLine();

                statusMessage = mode == EventStreamMode.LiveCamera
                    ? "Łączenie z kamerą eventową (LIVE)..."
                    : $"Odtwarzanie pliku RAW: {activeRawFileName}";
            }
            catch (Exception e)
            {
                statusMessage = $"Błąd uruchamiania środowiska kamery: {e.Message}";
                Debug.LogError(statusMessage);
            }

            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = "EventCameraReceiver"
            };
            _receiveThread.Start();
        }

        public void StopStream()
        {
            _running = false;
            isConnected = false;

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(300);
                _receiveThread = null;
            }

            if (_pythonProcess != null)
            {
                try
                {
                    if (!_pythonProcess.HasExited)
                    {
                        _pythonProcess.Kill();
                        _pythonProcess.WaitForExit(300);
                    }
                }
                catch (Exception)
                {
                    // Ignore process exit race
                }
                finally
                {
                    _pythonProcess.Dispose();
                    _pythonProcess = null;
                }
            }

            lock (_dataLock)
            {
                Array.Clear(_motionGrid, 0, _motionGrid.Length);
                _activeClusters.Clear();
            }
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    return false;
                offset += read;
            }
            return true;
        }

        private void ReceiveLoop()
        {
            byte[] headerBuf = new byte[18];
            byte[] gridBuf = new byte[GridW * GridH];
            byte[] clusterBuf = new byte[16 * 20];

            while (_running)
            {
                try
                {
                    using (TcpClient client = new TcpClient())
                    {
                        client.NoDelay = true;
                        IAsyncResult ar = client.BeginConnect("127.0.0.1", tcpPort, null, null);
                        bool connected = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(600));
                        if (!connected || !client.Connected)
                        {
                            Thread.Sleep(150);
                            continue;
                        }
                        client.EndConnect(ar);
                        isConnected = true;

                        using (NetworkStream stream = client.GetStream())
                        {
                            while (_running && client.Connected)
                            {
                                if (!ReadExact(stream, headerBuf, 18))
                                    break;

                                if (headerBuf[0] != 'E' || headerBuf[1] != 'V' || headerBuf[2] != 'S' || headerBuf[3] != 'L')
                                    break;

                                ushort width = BitConverter.ToUInt16(headerBuf, 4);
                                ushort height = BitConverter.ToUInt16(headerBuf, 6);
                                ushort gw = BitConverter.ToUInt16(headerBuf, 8);
                                ushort gh = BitConverter.ToUInt16(headerBuf, 10);
                                ushort clusterCount = BitConverter.ToUInt16(headerBuf, 12);
                                int frameLen = BitConverter.ToInt32(headerBuf, 14);

                                int clusterBytes = clusterCount * 20;
                                if (clusterBytes > 0)
                                {
                                    if (clusterBuf.Length < clusterBytes)
                                        clusterBuf = new byte[clusterBytes];
                                    if (!ReadExact(stream, clusterBuf, clusterBytes))
                                        break;
                                }

                                int gridBytes = gw * gh;
                                if (gridBuf.Length != gridBytes)
                                    gridBuf = new byte[gridBytes];
                                if (!ReadExact(stream, gridBuf, gridBytes))
                                    break;

                                byte[] frameTemp = new byte[frameLen];
                                if (!ReadExact(stream, frameTemp, frameLen))
                                    break;

                                lock (_dataLock)
                                {
                                    camWidth = width;
                                    camHeight = height;
                                    _latestFrameBytes = frameTemp;
                                    _hasNewFrame = true;

                                    if (gridBytes == _motionGrid.Length)
                                        Buffer.BlockCopy(gridBuf, 0, _motionGrid, 0, gridBytes);

                                    _activeClusters.Clear();
                                    for (int i = 0; i < clusterCount; i++)
                                    {
                                        int b = i * 20;
                                        float u = BitConverter.ToSingle(clusterBuf, b);
                                        float v = BitConverter.ToSingle(clusterBuf, b + 4);
                                        float vu = BitConverter.ToSingle(clusterBuf, b + 8);
                                        float vv = BitConverter.ToSingle(clusterBuf, b + 12);
                                        float intensity = BitConverter.ToSingle(clusterBuf, b + 16);
                                        _activeClusters.Add(new SlashCluster
                                        {
                                            viewportPos = new Vector2(u, v),
                                            velocity = new Vector2(vu, vv),
                                            intensity = intensity
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    isConnected = false;
                    Thread.Sleep(200);
                }
            }
        }

        private void Update()
        {
            UpdateMouseFallback();

            byte[] frameToApply = null;
            int w = camWidth;
            int h = camHeight;

            lock (_dataLock)
            {
                if (_hasNewFrame && _latestFrameBytes != null)
                {
                    frameToApply = _latestFrameBytes;
                    _hasNewFrame = false;
                }
            }

            if (frameToApply != null)
            {
                if (_eventTexture == null || _eventTexture.width != w || _eventTexture.height != h)
                {
                    _eventTexture = new Texture2D(w, h, TextureFormat.RGB24, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    ApplyTextureToScreen();
                }

                if (frameToApply.Length == w * h * 3)
                {
                    _eventTexture.LoadRawTextureData(frameToApply);
                    _eventTexture.Apply(false);
                }
            }
        }

        private void UpdateMouseFallback()
        {
            bool pressed = false;
            Vector2 mousePos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                pressed = true;
                mousePos = Mouse.current.position.ReadValue();
            }
#else
            if (Input.GetMouseButton(0))
            {
                pressed = true;
                mousePos = Input.mousePosition;
            }
#endif
            if (pressed)
            {
                Vector2 cur = new Vector2(
                    Mathf.Clamp01(mousePos.x / Mathf.Max(1f, Screen.width)),
                    Mathf.Clamp01(mousePos.y / Mathf.Max(1f, Screen.height))
                );
                if (_mouseDragging)
                {
                    _mouseVelocity = (cur - _prevMouseViewport) / Mathf.Max(0.001f, Time.deltaTime);
                }
                else
                {
                    _mouseDragging = true;
                    _mouseVelocity = Vector2.zero;
                }
                _prevMouseViewport = cur;
            }
            else
            {
                _mouseDragging = false;
                _mouseVelocity = Vector2.zero;
            }
        }

        public void GetActiveClusters(List<SlashCluster> targetList)
        {
            targetList.Clear();
            lock (_dataLock)
            {
                targetList.AddRange(_activeClusters);
            }
            if (_mouseDragging && _mouseVelocity.sqrMagnitude > 0.05f)
            {
                targetList.Add(new SlashCluster
                {
                    viewportPos = _prevMouseViewport,
                    velocity = _mouseVelocity,
                    intensity = 1.0f
                });
            }
        }

        /// <summary>
        /// Checks whether a fruit at normalized viewport coordinates (u, v) is sliced by hand motion
        /// in the event camera stream (or mouse slash during desktop testing).
        /// </summary>
        public bool CheckFruitSliced(Vector2 fruitViewportPos, float viewportRadius, out Vector2 slashDirection)
        {
            slashDirection = Vector2.right;

            if (fruitViewportPos.x < 0.02f || fruitViewportPos.x > 0.98f ||
                fruitViewportPos.y < 0.04f || fruitViewportPos.y > 0.98f)
            {
                return false;
            }

            // 1. Check mouse slash fallback
            if (_mouseDragging && _mouseVelocity.magnitude > 0.35f)
            {
                float dist = Vector2.Distance(fruitViewportPos, _prevMouseViewport);
                if (dist <= viewportRadius * 1.15f)
                {
                    slashDirection = _mouseVelocity.normalized;
                    return true;
                }
            }

            // 2. Check verified tracked hand clusters ONLY ("no hand detected -> no registration")
            float radiusScale = Mathf.Clamp(sensitivity, 0.5f, 1.6f) * 1.25f;

            lock (_dataLock)
            {
                if (_activeClusters.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < _activeClusters.Count; i++)
                {
                    SlashCluster c = _activeClusters[i];
                    if (Vector2.Distance(fruitViewportPos, c.viewportPos) <= viewportRadius * radiusScale)
                    {
                        slashDirection = c.velocity.sqrMagnitude > 0.01f
                            ? c.velocity.normalized
                            : new Vector2(fruitViewportPos.x - c.viewportPos.x, fruitViewportPos.y - c.viewportPos.y).normalized;
                        if (slashDirection.sqrMagnitude < 0.01f)
                            slashDirection = Vector2.up;
                        return true;
                    }
                }
            }

            return false;
        }

        private void OnDisable()
        {
            StopStream();
        }

        private void OnApplicationQuit()
        {
            StopStream();
        }
    }
}
