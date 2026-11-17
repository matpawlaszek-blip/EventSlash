using System.Collections.Generic;
using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Represents a whole 3D fruit, bomb, or multi-slice Pomegranate launched upward from the bottom of the screen.
    /// Detects hand slashes from the EventCameraManager:
    /// - Normal fruit: splits into Half_A and Half_B with a juice splash.
    /// - Bonus outlined fruit: has a 2D white silhouette outline and awards extra points (3x).
    /// - Bomb: explodes and ends the game if sliced; harmlessly falls off if avoided.
    /// - Pomegranate: enters a slow-motion multi-slice frenzy where the camera zooms, tilts, and shakes
    ///   on every slash, then explodes into many 3D pomegranate pieces shot to every side,
    ///   slicing all active fruits and making all active bombs disappear.
    /// </summary>
    public class FruitItem : MonoBehaviour
    {
        [Header("Fruit / Bomb / Pomegranate Identity & Sliced Models")]
        public string fruitName = "Fruit";
        public bool isBomb;
        public bool isPomegranate;
        public bool isDragonfruit;
        public bool isBonusOutlined;
        public GameObject halfAPrefab;
        public GameObject halfBPrefab;
        public GameObject piecePrefab;
        public Color juiceColor = new Color(0.95f, 0.15f, 0.2f);
        public Vector3 localCutSeparationAxis = Vector3.right;
        public float viewportHitRadius = 0.085f;
        public int scorePoints = 1;

        private static Material _whiteOutlineMaterial;
        private static Material _goldOutlineMaterial;
        private readonly List<Transform> _outlineRingTransforms = new List<Transform>();
        private readonly List<Transform> _outlineSourceTransforms = new List<Transform>();
        private readonly List<Vector2> _outlineScreenDirs = new List<Vector2>();
        private float _outlineWorldThickness = 0.07f;
        private ParticleSystem _bonusTrailPs;
        private TrailRenderer _goldenRibbonTrail;

        [Header("Ballistics")]
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float gravity = -13.5f;
        public float missThresholdY = -6.0f;

        private bool _hasEnteredScreen;
        private bool _isSliced;
        private Camera _mainCam;

        // Pomegranate multi-slice frenzy state
        private bool _isInPomegranateFrenzy;
        private int _pomegranateSliceCount;
        private float _pomegranateFrenzyRemaining;
        private float _pomegranateHitCooldown;
        private float _pomegranatePulseTimer;
        private Vector3 _pomegranateBaseScale;

        public bool IsInPomegranateFrenzy => _isInPomegranateFrenzy;
        public int PomegranateSliceCount => _pomegranateSliceCount;

        private void Start()
        {
            _mainCam = Camera.main;
            _pomegranateBaseScale = transform.localScale;
        }

        private void Update()
        {
            if (_isSliced) return;

            if (_mainCam == null) _mainCam = Camera.main;
            if (_mainCam == null) return;

            if (_isInPomegranateFrenzy)
            {
                UpdatePomegranateFrenzy();
                return;
            }

            float dt = Time.deltaTime;
            velocity.y += gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(angularVelocity * dt, Space.Self);

            Vector2 quadVp = GetNormalizedQuadCoords(transform.position);
            if (quadVp.y > 0.06f && quadVp.x > 0.04f && quadVp.x < 0.96f)
            {
                _hasEnteredScreen = true;
            }

            // Check hand slash detection while item is visible on screen
            if (_hasEnteredScreen && quadVp.y > 0.02f && quadVp.y < 0.98f && quadVp.x > 0.02f && quadVp.x < 0.98f)
            {
                if (CheckSlicedAtPosition(quadVp, viewportHitRadius, out Vector2 slashDir))
                {
                    SliceFruit(slashDir);
                    return;
                }
            }

            // Check if item fell off the bottom or exited the far side of the screen without being sliced
            bool exitedSide = _hasEnteredScreen && isDragonfruit && (quadVp.x < -0.15f || quadVp.x > 1.15f);
            if (exitedSide || (_hasEnteredScreen && velocity.y < 0f && transform.position.y < missThresholdY))
            {
                // Bombs, Pomegranates, and side-launched Dragonfruits do not deduct a life when missed
                if (!isBomb && !isPomegranate && !isDragonfruit && EventSlashGameManager.Instance != null)
                {
                    EventSlashGameManager.Instance.OnFruitMissed(this);
                }
                Destroy(gameObject);
            }
            else if (!_hasEnteredScreen && (transform.position.y < missThresholdY - 3.0f || Mathf.Abs(transform.position.x) > 18.0f))
            {
                Destroy(gameObject);
            }
        }

        private void LateUpdate()
        {
            if ((!isBonusOutlined && !isDragonfruit) || _outlineRingTransforms.Count == 0)
                return;

            float pulseMult = isDragonfruit ? (1.0f + 0.18f * Mathf.Sin(Time.time * 10.0f)) : 1.0f;
            float thickness = _outlineWorldThickness * pulseMult;

            // Keep the 8 silhouette copies offset in 2D screen (X, Y) and pushed behind the fruit in +Z
            // so the 3D fruit always renders cleanly in front while a bold 2D border surrounds its contour.
            for (int i = 0; i < _outlineRingTransforms.Count; i++)
            {
                Transform outTr = _outlineRingTransforms[i];
                Transform srcTr = _outlineSourceTransforms[i];
                if (outTr == null || srcTr == null) continue;

                Vector2 dir = _outlineScreenDirs[i];
                outTr.position = srcTr.position + new Vector3(
                    dir.x * thickness,
                    dir.y * thickness,
                    0.85f
                );
                outTr.rotation = srcTr.rotation;
            }
        }

        private Vector2 GetNormalizedQuadCoords(Vector3 worldPos)
        {
            float baseHeight = 10.0f;
            float aspect = _mainCam != null ? _mainCam.aspect : (16f / 9f);
            float baseWidth = baseHeight * aspect;
            return new Vector2(
                worldPos.x / baseWidth + 0.5f,
                worldPos.y / baseHeight + 0.5f
            );
        }

        private bool CheckSlicedAtPosition(Vector2 quadVp, float radius, out Vector2 slashDir)
        {
            slashDir = Vector2.right;
            if (EventCameraManager.Instance == null)
                return false;

            if (EventCameraManager.Instance.CheckFruitSliced(quadVp, radius, out slashDir))
                return true;

            if (_mainCam != null)
            {
                Vector3 camVp = _mainCam.WorldToViewportPoint(transform.position);
                Vector2 camVp2 = new Vector2(camVp.x, camVp.y);
                if ((camVp2 - quadVp).sqrMagnitude > 0.0001f &&
                    EventCameraManager.Instance.CheckFruitSliced(camVp2, radius, out slashDir))
                {
                    return true;
                }
            }
            return false;
        }

        private void UpdatePomegranateFrenzy()
        {
            float udt = Time.unscaledDeltaTime;
            _pomegranateFrenzyRemaining -= udt;
            if (_pomegranateHitCooldown > 0f)
            {
                _pomegranateHitCooldown -= udt;
            }

            // Suspend gravity and keep the pomegranate hovering in the sweet spot for rapid multi-slicing
            velocity *= Mathf.Pow(0.015f, udt);
            float targetY = Mathf.Clamp(transform.position.y, -0.8f, 1.4f);
            float targetX = Mathf.Clamp(transform.position.x, -3.8f, 3.8f);
            Vector3 targetPos = new Vector3(targetX, targetY, transform.position.z);
            transform.position = Vector3.Lerp(transform.position, targetPos + velocity * udt, udt * 4.5f);

            transform.Rotate(angularVelocity * (udt * 1.85f), Space.Self);

            // Squash-and-stretch scale pulse on every slash + pre-explosion tremor
            float tremor = Mathf.Clamp01(1.0f - (_pomegranateFrenzyRemaining / 2.35f)) * 0.08f;
            float pulse = 0f;
            if (_pomegranatePulseTimer > 0f)
            {
                _pomegranatePulseTimer -= udt;
                float t = Mathf.Clamp01(_pomegranatePulseTimer / 0.15f);
                pulse = Mathf.Sin(t * Mathf.PI) * 0.26f;
            }
            float jitter = Mathf.Sin(Time.unscaledTime * 55f) * tremor;
            transform.localScale = _pomegranateBaseScale * (1.0f + pulse + jitter);

            // Check for additional rapid slashes while frenzy is active
            if (_pomegranateHitCooldown <= 0f)
            {
                Vector2 quadVp = GetNormalizedQuadCoords(transform.position);
                if (CheckSlicedAtPosition(quadVp, viewportHitRadius * 1.45f, out Vector2 slashDir))
                {
                    RegisterPomegranateHit(slashDir);
                }
            }

            // Explode into pieces when frenzy timer finishes
            if (_pomegranateFrenzyRemaining <= 0f)
            {
                ExplodePomegranate();
            }
        }

        public void SliceFruit(Vector2 slashDirection)
        {
            if (_isSliced) return;

            if (slashDirection.sqrMagnitude < 0.001f)
            {
                slashDirection = Vector2.right;
            }
            slashDirection.Normalize();

            if (isPomegranate)
            {
                if (!_isInPomegranateFrenzy)
                {
                    _isInPomegranateFrenzy = true;
                    _pomegranateFrenzyRemaining = 2.35f;
                    _pomegranateSliceCount = 0;
                    _pomegranateBaseScale = transform.localScale;
                    velocity *= 0.12f;
                }
                RegisterPomegranateHit(slashDirection);
                return;
            }

            _isSliced = true;

            // Compute a separation direction in world space perpendicular to the slash
            Vector3 worldSepDir = new Vector3(-slashDirection.y, slashDirection.x, 0f).normalized;
            if (worldSepDir.x < 0f)
            {
                worldSepDir = -worldSepDir;
            }

            // Orient the spawned halves so their cut plane faces the camera and splits cleanly
            Quaternion sliceRotation = Quaternion.Euler(0f, 15f, Mathf.Atan2(slashDirection.y, slashDirection.x) * Mathf.Rad2Deg);
            float splitImpulse = isBomb ? 4.5f : 2.8f;

            if (halfAPrefab != null)
            {
                GameObject halfA = Instantiate(halfAPrefab, transform.position - worldSepDir * 0.18f, sliceRotation * Quaternion.Euler(0f, -25f, 0f));
                halfA.transform.localScale = transform.localScale;
                SlicedFruitHalf moverA = halfA.GetComponent<SlicedFruitHalf>();
                if (moverA == null) moverA = halfA.AddComponent<SlicedFruitHalf>();
                moverA.velocity = new Vector3(velocity.x * 0.5f - worldSepDir.x * splitImpulse, Mathf.Max(velocity.y * 0.4f, 1.8f) + 1.2f, -0.3f);
                moverA.angularVelocity = new Vector3(Random.Range(-140f, 140f), -180f, Random.Range(120f, 240f));
            }

            if (halfBPrefab != null)
            {
                GameObject halfB = Instantiate(halfBPrefab, transform.position + worldSepDir * 0.18f, sliceRotation * Quaternion.Euler(0f, 25f, 0f));
                halfB.transform.localScale = transform.localScale;
                SlicedFruitHalf moverB = halfB.GetComponent<SlicedFruitHalf>();
                if (moverB == null) moverB = halfB.AddComponent<SlicedFruitHalf>();
                moverB.velocity = new Vector3(velocity.x * 0.5f + worldSepDir.x * splitImpulse, Mathf.Max(velocity.y * 0.4f, 1.8f) + 1.2f, -0.3f);
                moverB.angularVelocity = new Vector3(Random.Range(-140f, 140f), 180f, Random.Range(-240f, -120f));
            }

            if (isBomb)
            {
                SpawnBombExplosion(transform.position);
                if (EventSlashGameManager.Instance != null)
                {
                    EventSlashGameManager.Instance.OnBombSliced(this, transform.position);
                }
            }
            else
            {
                SpawnJuiceSplash(transform.position, slashDirection);
                if (EventSlashGameManager.Instance != null)
                {
                    EventSlashGameManager.Instance.OnFruitSliced(this, transform.position);
                }
            }

            if (_bonusTrailPs != null)
            {
                _bonusTrailPs.transform.SetParent(null, true);
                var em = _bonusTrailPs.emission;
                em.enabled = false;
                Destroy(_bonusTrailPs.gameObject, 0.8f);
                _bonusTrailPs = null;
            }

            if (_goldenRibbonTrail != null)
            {
                _goldenRibbonTrail.transform.SetParent(null, true);
                _goldenRibbonTrail.emitting = false;
                Destroy(_goldenRibbonTrail.gameObject, 0.8f);
                _goldenRibbonTrail = null;
            }

            Destroy(gameObject);
        }

        private void RegisterPomegranateHit(Vector2 slashDir)
        {
            _pomegranateSliceCount++;
            _pomegranateHitCooldown = 0.095f;
            _pomegranatePulseTimer = 0.15f;

            // Nudge pomegranate slightly along the slash and spin faster with each cut
            transform.position += new Vector3(slashDir.x * 0.12f, slashDir.y * 0.10f, 0f);
            angularVelocity = new Vector3(
                Random.Range(-260f, 260f),
                Random.Range(-320f, 320f),
                Random.Range(-260f, 260f)
            );

            SpawnPomegranateSlashFX(transform.position, slashDir);

            // Also fling out 2-3 small pomegranate chunks on every hit for visceral feedback
            SpawnPomegranateBurstPieces(Random.Range(2, 4), 10.0f, 15.0f, 0.55f);

            if (EventSlashGameManager.Instance != null)
            {
                EventSlashGameManager.Instance.OnPomegranateSliced(this, _pomegranateSliceCount, slashDir);
            }
        }

        private void ExplodePomegranate()
        {
            if (_isSliced) return;
            _isSliced = true;
            _isInPomegranateFrenzy = false;

            // Blast 26 pomegranate pieces outward in 360 degrees to every side (no gravity!)
            int pieceCount = Mathf.Clamp(22 + _pomegranateSliceCount, 24, 36);
            SpawnPomegranateBurstPieces(pieceCount, 14.5f, 25.0f, 0.95f);
            SpawnPomegranateMegaExplosionFX(transform.position);

            if (EventSlashGameManager.Instance != null)
            {
                EventSlashGameManager.Instance.OnPomegranateExploded(this, _pomegranateSliceCount);
            }

            Destroy(gameObject);
        }

        private void SpawnPomegranateBurstPieces(int count, float minSpeed, float maxSpeed, float scaleMult)
        {
            for (int i = 0; i < count; i++)
            {
                float baseAngle = (i / (float)count) * Mathf.PI * 2.0f;
                float angle = baseAngle + Random.Range(-0.18f, 0.18f);
                float speed = Random.Range(minSpeed, maxSpeed);
                float vz = Random.Range(-2.2f, 1.5f);

                Vector3 dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 spawnPos = transform.position + dir * Random.Range(0.08f, 0.28f) + new Vector3(0f, 0f, Random.Range(-0.25f, 0.25f));
                Quaternion rot = Random.rotation;

                GameObject pieceObj;
                if (piecePrefab != null)
                {
                    pieceObj = Instantiate(piecePrefab, spawnPos, rot);
                }
                else if (halfAPrefab != null)
                {
                    pieceObj = Instantiate(halfAPrefab, spawnPos, rot);
                }
                else
                {
                    pieceObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    pieceObj.transform.position = spawnPos;
                    pieceObj.transform.rotation = rot;
                    Collider col = pieceObj.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                }

                pieceObj.name = "Pomegranate_ExplodedPiece";
                float s = Random.Range(0.75f, 1.25f) * scaleMult;
                pieceObj.transform.localScale = _pomegranateBaseScale * s;

                SlicedFruitHalf mover = pieceObj.GetComponent<SlicedFruitHalf>();
                if (mover == null) mover = pieceObj.AddComponent<SlicedFruitHalf>();

                // Shot straight outward to every side — gravity = 0 so they do NOT fall down like normal fruit halves!
                mover.gravity = 0.0f;
                mover.maxLifetime = 1.8f;
                mover.velocity = new Vector3(dir.x * speed, dir.y * speed, vz);
                mover.angularVelocity = new Vector3(
                    Random.Range(-540f, 540f),
                    Random.Range(-540f, 540f),
                    Random.Range(-540f, 540f)
                );
            }
        }

        /// <summary>
        /// Called when a Pomegranate explodes and automatically slices every other fruit on screen.
        /// </summary>
        public void ForceSliceFromExplosion(Vector3 explosionOrigin)
        {
            if (_isSliced || isBomb || isPomegranate) return;
            Vector2 dir = new Vector2(
                transform.position.x - explosionOrigin.x,
                transform.position.y - explosionOrigin.y
            );
            if (dir.sqrMagnitude < 0.001f)
            {
                dir = Random.insideUnitCircle.normalized;
            }
            SliceFruit(dir.normalized);
        }

        /// <summary>
        /// Called when a Pomegranate explodes and makes all active bombs harmlessly vanish.
        /// </summary>
        public void DisappearBombWithSmoke()
        {
            if (_isSliced) return;
            _isSliced = true;
            SpawnBombDisappearSmokeFX(transform.position);
            Destroy(gameObject);
        }

        private void SpawnPomegranateSlashFX(Vector3 worldPos, Vector2 slashDir)
        {
            GameObject fxObj = new GameObject("PomegranateSlashFX");
            fxObj.transform.position = worldPos + new Vector3(0f, 0f, -0.6f);

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.45f;
            main.loop = false;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.48f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6.0f, 15.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.34f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.12f, 0.28f),
                new Color(1.0f, 0.78f, 0.85f)
            );
            main.gravityModifier = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 26) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.28f;

            AssignParticleMaterial(fxObj, new Color(0.95f, 0.08f, 0.22f));
            ps.Play();
            Destroy(fxObj, 1.0f);
        }

        private void SpawnPomegranateMegaExplosionFX(Vector3 worldPos)
        {
            GameObject fxObj = new GameObject("PomegranateMegaExplosionFX");
            fxObj.transform.position = worldPos + new Vector3(0f, 0f, -0.7f);

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.9f;
            main.loop = false;
            main.useUnscaledTime = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10.0f, 24.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.20f, 0.52f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.96f, 0.05f, 0.18f),
                new Color(1.0f, 0.55f, 0.22f)
            );
            main.gravityModifier = 0.0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 95) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.42f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            AssignParticleMaterial(fxObj, new Color(0.95f, 0.08f, 0.22f));
            ps.Play();
            Destroy(fxObj, 1.5f);
        }

        private void SpawnBombDisappearSmokeFX(Vector3 worldPos)
        {
            GameObject fxObj = new GameObject("BombDisappearSmokeFX");
            fxObj.transform.position = worldPos + new Vector3(0f, 0f, -0.5f);

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.70f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 6.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.78f, 0.82f, 0.9f),
                new Color(0.35f, 0.38f, 0.42f, 0.6f)
            );
            main.gravityModifier = -0.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;

            AssignParticleMaterial(fxObj, new Color(0.68f, 0.72f, 0.76f));
            ps.Play();
            Destroy(fxObj, 1.2f);
        }

        private static void AssignParticleMaterial(GameObject fxObj, Color col)
        {
            ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
            if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                Material pMat = new Material(particleShader);
                pMat.color = col;
                if (pMat.HasProperty("_BaseColor")) pMat.SetColor("_BaseColor", col);
                psr.material = pMat;
            }
        }

        private void SpawnBombExplosion(Vector3 worldPos)
        {
            GameObject fxObj = new GameObject("BombExplosionFX");
            fxObj.transform.position = worldPos + new Vector3(0f, 0f, -0.6f);

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.8f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5.0f, 14.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1.0f, 0.85f, 0.15f), new Color(1.0f, 0.22f, 0.05f));
            main.gravityModifier = 0.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 70;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 55) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            AssignParticleMaterial(fxObj, new Color(1.0f, 0.45f, 0.08f));
            ps.Play();
            Destroy(fxObj, 1.5f);
        }

        private void SpawnJuiceSplash(Vector3 worldPos, Vector2 slashDir)
        {
            GameObject fxObj = new GameObject($"JuiceSplash_{fruitName}");
            fxObj.transform.position = worldPos + new Vector3(0f, 0f, -0.5f);

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            main.startColor = juiceColor;
            main.gravityModifier = 1.6f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            AssignParticleMaterial(fxObj, juiceColor);
            ps.Play();
            Destroy(fxObj, 1.2f);
        }

        /// <summary>
        /// Creates a bold, flat unlit 2D white silhouette outline around the 3D fruit by rendering
        /// an 8-direction 2D screen-space offset ring of pure-white unlit copies directly behind the 3D mesh.
        /// Works on any single- or multi-submesh FBX model without requiring CPU mesh read/write.
        /// </summary>
        public void Apply2DWhiteOutline(float outlineThickness = 0.07f)
        {
            isBonusOutlined = true;
            _outlineWorldThickness = outlineThickness;

            if (_whiteOutlineMaterial == null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
                if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");

                if (unlitShader != null)
                {
                    _whiteOutlineMaterial = new Material(unlitShader);
                    _whiteOutlineMaterial.color = Color.white;
                    if (_whiteOutlineMaterial.HasProperty("_BaseColor"))
                        _whiteOutlineMaterial.SetColor("_BaseColor", Color.white);
                }
            }

            if (_whiteOutlineMaterial == null) return;

            _outlineRingTransforms.Clear();
            _outlineSourceTransforms.Clear();
            _outlineScreenDirs.Clear();

            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
            const int directions = 8;

            foreach (MeshFilter mf in filters)
            {
                if (mf == null || mf.sharedMesh == null || mf.gameObject.name.StartsWith("Outline2D_"))
                    continue;

                Mesh srcMesh = mf.sharedMesh;
                int subCount = Mathf.Max(1, srcMesh.subMeshCount);
                Material[] whiteMats = new Material[subCount];
                for (int s = 0; s < subCount; s++)
                {
                    whiteMats[s] = _whiteOutlineMaterial;
                }

                for (int d = 0; d < directions; d++)
                {
                    float ang = (d / (float)directions) * Mathf.PI * 2.0f;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                    GameObject outlineObj = new GameObject($"Outline2D_{d}");
                    outlineObj.transform.SetParent(mf.transform, false);
                    outlineObj.transform.localPosition = Vector3.zero;
                    outlineObj.transform.localRotation = Quaternion.identity;
                    outlineObj.transform.localScale = Vector3.one * 1.025f;

                    MeshFilter outMf = outlineObj.AddComponent<MeshFilter>();
                    outMf.sharedMesh = srcMesh;

                    MeshRenderer outMr = outlineObj.AddComponent<MeshRenderer>();
                    outMr.sharedMaterials = whiteMats;
                    outMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    outMr.receiveShadows = false;

                    _outlineRingTransforms.Add(outlineObj.transform);
                    _outlineSourceTransforms.Add(mf.transform);
                    _outlineScreenDirs.Add(dir);
                }
            }

            AttachWhiteParticleTrail();
        }

        private void AttachWhiteParticleTrail()
        {
            if (_bonusTrailPs != null) return;

            GameObject trailObj = new GameObject("BonusWhiteParticleTrail");
            trailObj.transform.SetParent(transform, false);
            trailObj.transform.localPosition = new Vector3(0f, 0f, 0.25f);

            ParticleSystem ps = trailObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.48f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 1.0f, 1.0f, 0.82f),
                new Color(0.92f, 0.96f, 1.0f, 0.45f)
            );
            main.gravityModifier = 0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 8f;
            emission.rateOverDistance = 3.2f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.24f;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            AssignParticleMaterial(trailObj, new Color(1.0f, 1.0f, 1.0f, 0.85f));
            ps.Play();
            _bonusTrailPs = ps;
        }

        /// <summary>
        /// Applies a glowing golden 2D silhouette outline and a thick golden ribbon + sparkle particle trail
        /// for the rare side-launched Dragonfruit.
        /// </summary>
        public void ApplyGoldenDragonfruitAura(float outlineThickness = 0.088f)
        {
            isDragonfruit = true;
            isBonusOutlined = true;
            _outlineWorldThickness = outlineThickness;

            if (_goldOutlineMaterial == null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");
                if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");

                if (unlitShader != null)
                {
                    Color goldCol = new Color(1.00f, 0.84f, 0.14f, 1.0f);
                    _goldOutlineMaterial = new Material(unlitShader);
                    _goldOutlineMaterial.color = goldCol;
                    if (_goldOutlineMaterial.HasProperty("_BaseColor"))
                        _goldOutlineMaterial.SetColor("_BaseColor", goldCol);
                }
            }

            if (_goldOutlineMaterial != null)
            {
                _outlineRingTransforms.Clear();
                _outlineSourceTransforms.Clear();
                _outlineScreenDirs.Clear();

                MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
                const int directions = 8;

                foreach (MeshFilter mf in filters)
                {
                    if (mf == null || mf.sharedMesh == null || mf.gameObject.name.StartsWith("Outline2D_"))
                        continue;

                    Mesh srcMesh = mf.sharedMesh;
                    int subCount = Mathf.Max(1, srcMesh.subMeshCount);
                    Material[] goldMats = new Material[subCount];
                    for (int s = 0; s < subCount; s++)
                    {
                        goldMats[s] = _goldOutlineMaterial;
                    }

                    for (int d = 0; d < directions; d++)
                    {
                        float ang = (d / (float)directions) * Mathf.PI * 2.0f;
                        Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

                        GameObject outlineObj = new GameObject($"Outline2D_Gold_{d}");
                        outlineObj.transform.SetParent(mf.transform, false);
                        outlineObj.transform.localPosition = Vector3.zero;
                        outlineObj.transform.localRotation = Quaternion.identity;
                        outlineObj.transform.localScale = Vector3.one * 1.035f;

                        MeshFilter outMf = outlineObj.AddComponent<MeshFilter>();
                        outMf.sharedMesh = srcMesh;

                        MeshRenderer outMr = outlineObj.AddComponent<MeshRenderer>();
                        outMr.sharedMaterials = goldMats;
                        outMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        outMr.receiveShadows = false;

                        _outlineRingTransforms.Add(outlineObj.transform);
                        _outlineSourceTransforms.Add(mf.transform);
                        _outlineScreenDirs.Add(dir);
                    }
                }
            }

            AttachThickGoldenTrail();
        }

        private void AttachThickGoldenTrail()
        {
            // 1. Thick glowing golden ribbon TrailRenderer
            if (_goldenRibbonTrail == null)
            {
                GameObject ribbonObj = new GameObject("DragonfruitGoldenRibbonTrail");
                ribbonObj.transform.SetParent(transform, false);
                ribbonObj.transform.localPosition = new Vector3(0f, 0f, 0.35f);

                TrailRenderer tr = ribbonObj.AddComponent<TrailRenderer>();
                tr.time = 0.42f;
                tr.minVertexDistance = 0.06f;
                tr.startWidth = 0.56f;
                tr.endWidth = 0.05f;
                tr.numCapVertices = 5;
                tr.numCornerVertices = 4;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;

                Shader trailShader = Shader.Find("Sprites/Default");
                if (trailShader == null) trailShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (trailShader != null)
                {
                    Material mat = new Material(trailShader);
                    Color gold = new Color(1.00f, 0.86f, 0.16f, 0.92f);
                    mat.color = gold;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", gold);
                    tr.material = mat;
                }

                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1.00f, 0.94f, 0.36f), 0.0f),
                        new GradientColorKey(new Color(1.00f, 0.72f, 0.08f), 0.55f),
                        new GradientColorKey(new Color(0.95f, 0.45f, 0.04f), 1.0f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.88f, 0.0f),
                        new GradientAlphaKey(0.55f, 0.55f),
                        new GradientAlphaKey(0.0f, 1.0f)
                    }
                );
                tr.colorGradient = grad;
                _goldenRibbonTrail = tr;
            }

            // 2. Dense golden sparkle particle trail
            if (_bonusTrailPs == null)
            {
                GameObject trailObj = new GameObject("DragonfruitGoldenParticleTrail");
                trailObj.transform.SetParent(transform, false);
                trailObj.transform.localPosition = new Vector3(0f, 0f, 0.28f);

                ParticleSystem ps = trailObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.65f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.00f, 0.94f, 0.32f, 0.95f),
                    new Color(1.00f, 0.68f, 0.08f, 0.80f)
                );
                main.gravityModifier = 0.05f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 140;

                var emission = ps.emission;
                emission.rateOverTime = 26f;
                emission.rateOverDistance = 7.5f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.34f;

                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

                AssignParticleMaterial(trailObj, new Color(1.00f, 0.86f, 0.16f, 0.95f));
                ps.Play();
                _bonusTrailPs = ps;
            }
        }
    }
}
