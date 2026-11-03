using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EventSlash
{
    /// <summary>
    /// Interactive Fruit Ninja style mode-selection station:
    /// Spins a 3D fruit inside a rotating glowing UI ring. When sliced by hand/mouse (or clicked),
    /// splits the 3D fruit into its two half models (Half_A and Half_B) with a juice splash
    /// and invokes the mode callback after a short slice animation.
    /// </summary>
    public class MenuFruitSelector : MonoBehaviour, IPointerDownHandler
    {
        [Header("UI Ring & 3D Fruit References")]
        public RectTransform ringRectTransform;
        public RectTransform rotatingRingGraphic;
        public string stationName = "LIVE";
        public GameObject wholeFruitPrefab;
        public GameObject halfAPrefab;
        public GameObject halfBPrefab;
        public Color juiceColor = Color.red;
        public float fruitScale = 1.65f;
        public float fruitWorldZ = -8.8f;
        public float ringRotateSpeed = -28f;
        public float viewportSliceRadius = 0.105f;

        private GameObject _fruitHolder;
        private GameObject _currentWholeFruit;
        private bool _isSliced;
        private float _readyTime;
        private Action _onSelectedCallback;
        private Camera _mainCam;

        public void Initialize(Action onSelected)
        {
            _onSelectedCallback = onSelected;
            _mainCam = Camera.main;
            _readyTime = Time.unscaledTime + 0.55f;
            RespawnWholeFruit();
        }

        public void RespawnWholeFruit()
        {
            _isSliced = false;
            if (ringRectTransform != null)
            {
                ringRectTransform.localScale = Vector3.one;
            }

            if (_fruitHolder == null)
            {
                _fruitHolder = new GameObject($"MenuFruit3D_{stationName}");
            }
            _fruitHolder.SetActive(true);

            if (_currentWholeFruit != null)
            {
                Destroy(_currentWholeFruit);
            }

            if (wholeFruitPrefab != null)
            {
                _currentWholeFruit = Instantiate(wholeFruitPrefab, _fruitHolder.transform);
                _currentWholeFruit.transform.localPosition = Vector3.zero;
                _currentWholeFruit.transform.localRotation = Quaternion.Euler(18f, 25f, -15f);
                _currentWholeFruit.transform.localScale = Vector3.one * fruitScale;
            }

            Sync3DPositionWithRing();
        }

        public void Hide3DFruit()
        {
            if (_fruitHolder != null)
            {
                _fruitHolder.SetActive(false);
            }
        }

        public void Show3DFruit()
        {
            if (_fruitHolder != null && !_isSliced)
            {
                _fruitHolder.SetActive(true);
            }
        }

        private void Sync3DPositionWithRing()
        {
            if (_fruitHolder == null || ringRectTransform == null) return;
            Vector3 ringWorld = ringRectTransform.position;
            _fruitHolder.transform.position = new Vector3(ringWorld.x, ringWorld.y, fruitWorldZ);
        }

        private void Update()
        {
            if (_mainCam == null) _mainCam = Camera.main;

            Sync3DPositionWithRing();

            if (rotatingRingGraphic != null)
            {
                rotatingRingGraphic.Rotate(0f, 0f, ringRotateSpeed * Time.deltaTime);
            }

            if (_currentWholeFruit != null && !_isSliced)
            {
                _currentWholeFruit.transform.Rotate(new Vector3(25f, 75f, 35f) * Time.deltaTime, Space.Self);
            }

            if (_isSliced || Time.unscaledTime < _readyTime || _fruitHolder == null || !_fruitHolder.activeInHierarchy || _mainCam == null)
                return;

            if (EventSlashGameManager.Instance != null &&
                EventSlashGameManager.Instance.settingsPopupPanel != null &&
                EventSlashGameManager.Instance.settingsPopupPanel.activeInHierarchy)
            {
                return;
            }

            Vector3 vp = _mainCam.WorldToViewportPoint(_fruitHolder.transform.position);
            Vector2 vp2 = new Vector2(vp.x, vp.y);

            // 1. Check slash via EventCameraManager (hand motion in event camera OR mouse drag blade)
            if (EventCameraManager.Instance != null &&
                EventCameraManager.Instance.CheckFruitSliced(vp2, viewportSliceRadius, out Vector2 slashDir))
            {
                TriggerSlice(slashDir);
                return;
            }

            // 2. Also allow direct mouse swipe/click over the 3D fruit
            bool mouseClicked = false;
            Vector2 mousePos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                mouseClicked = true;
                mousePos = Mouse.current.position.ReadValue();
            }
#else
            if (Input.GetMouseButtonDown(0))
            {
                mouseClicked = true;
                mousePos = Input.mousePosition;
            }
#endif
            if (mouseClicked)
            {
                Vector2 mouseVp = new Vector2(
                    mousePos.x / Mathf.Max(1f, Screen.width),
                    mousePos.y / Mathf.Max(1f, Screen.height)
                );
                if (Vector2.Distance(mouseVp, vp2) <= viewportSliceRadius * 0.95f)
                {
                    TriggerSlice(new Vector2(1f, 0.45f).normalized);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (EventSlashGameManager.Instance != null &&
                EventSlashGameManager.Instance.settingsPopupPanel != null &&
                EventSlashGameManager.Instance.settingsPopupPanel.activeInHierarchy)
            {
                return;
            }
            if (!_isSliced)
            {
                TriggerSlice(new Vector2(1f, 0.45f).normalized);
            }
        }

        public void TriggerSlice(Vector2 slashDirection)
        {
            if (_isSliced) return;
            _isSliced = true;

            if (_currentWholeFruit != null)
            {
                Destroy(_currentWholeFruit);
                _currentWholeFruit = null;
            }

            Vector3 spawnPos = _fruitHolder != null ? _fruitHolder.transform.position : transform.position;
            Vector3 sepDir = new Vector3(-slashDirection.y, slashDirection.x, 0f).normalized;
            if (sepDir.x < 0f) sepDir = -sepDir;

            Quaternion sliceRot = Quaternion.Euler(0f, 15f, Mathf.Atan2(slashDirection.y, slashDirection.x) * Mathf.Rad2Deg);

            if (halfAPrefab != null)
            {
                GameObject halfA = Instantiate(halfAPrefab, spawnPos - sepDir * 0.22f, sliceRot * Quaternion.Euler(0f, -28f, 0f));
                halfA.transform.localScale = Vector3.one * fruitScale;
                SlicedFruitHalf moverA = halfA.AddComponent<SlicedFruitHalf>();
                moverA.velocity = new Vector3(-sepDir.x * 3.2f - 1.2f, 3.2f, -0.4f);
                moverA.angularVelocity = new Vector3(120f, -210f, 180f);
            }

            if (halfBPrefab != null)
            {
                GameObject halfB = Instantiate(halfBPrefab, spawnPos + sepDir * 0.22f, sliceRot * Quaternion.Euler(0f, 28f, 0f));
                halfB.transform.localScale = Vector3.one * fruitScale;
                SlicedFruitHalf moverB = halfB.AddComponent<SlicedFruitHalf>();
                moverB.velocity = new Vector3(sepDir.x * 3.2f + 1.2f, 3.2f, -0.4f);
                moverB.angularVelocity = new Vector3(-120f, 210f, -180f);
            }

            SpawnMenuSliceSplash(spawnPos);
            StartCoroutine(AnimateRingAndInvokeCoroutine());
        }

        private void SpawnMenuSliceSplash(Vector3 worldPos)
        {
            GameObject fxObj = new GameObject($"MenuSplash_{stationName}");
            fxObj.transform.position = worldPos + new Vector3(0f, 0f, -0.5f);

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.0f, 8.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.34f);
            main.startColor = juiceColor;
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 32) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                Material pMat = new Material(particleShader);
                pMat.color = juiceColor;
                if (pMat.HasProperty("_BaseColor")) pMat.SetColor("_BaseColor", juiceColor);
                psr.material = pMat;
            }

            ps.Play();
            Destroy(fxObj, 1.0f);
        }

        private IEnumerator AnimateRingAndInvokeCoroutine()
        {
            float elapsed = 0f;
            float duration = 0.55f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                if (ringRectTransform != null)
                {
                    float scale = 1.0f + Mathf.Sin(t * Mathf.PI) * 0.22f;
                    ringRectTransform.localScale = Vector3.one * scale;
                }
                yield return null;
            }

            if (ringRectTransform != null)
            {
                ringRectTransform.localScale = Vector3.one;
            }

            _onSelectedCallback?.Invoke();
        }

        private void OnDisable()
        {
            if (_fruitHolder != null)
            {
                _fruitHolder.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_fruitHolder != null)
            {
                Destroy(_fruitHolder);
            }
        }
    }
}
