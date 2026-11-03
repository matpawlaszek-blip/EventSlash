using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Represents a whole 3D fruit (or bomb) launched upward from the bottom of the screen.
    /// Detects hand slashes from the EventCameraManager, spawns the two sliced half models
    /// (Half_A and Half_B) + juice/explosion splash when cut.
    /// If isBomb is true: slicing immediately ends the game, while letting it fall does NOT deduct a life.
    /// </summary>
    public class FruitItem : MonoBehaviour
    {
        [Header("Fruit / Bomb Identity & Sliced Models")]
        public string fruitName = "Fruit";
        public bool isBomb;
        public GameObject halfAPrefab;
        public GameObject halfBPrefab;
        public Color juiceColor = new Color(0.95f, 0.15f, 0.2f);
        public Vector3 localCutSeparationAxis = Vector3.right;
        public float viewportHitRadius = 0.085f;
        public int scorePoints = 1;

        [Header("Ballistics")]
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float gravity = -13.5f;
        public float missThresholdY = -6.0f;

        private bool _hasEnteredScreen;
        private bool _isSliced;
        private Camera _mainCam;

        private void Start()
        {
            _mainCam = Camera.main;
        }

        private void Update()
        {
            if (_isSliced) return;

            float dt = Time.deltaTime;
            velocity.y += gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(angularVelocity * dt, Space.Self);

            if (_mainCam == null) _mainCam = Camera.main;
            if (_mainCam == null) return;

            Vector3 vp = _mainCam.WorldToViewportPoint(transform.position);
            if (vp.y > 0.06f)
            {
                _hasEnteredScreen = true;
            }

            // Check hand slash detection while item is visible on screen
            if (_hasEnteredScreen && vp.y > 0.02f && vp.y < 0.98f && vp.x > 0.02f && vp.x < 0.98f)
            {
                if (EventCameraManager.Instance != null &&
                    EventCameraManager.Instance.CheckFruitSliced(new Vector2(vp.x, vp.y), viewportHitRadius, out Vector2 slashDir))
                {
                    SliceFruit(slashDir);
                    return;
                }
            }

            // Check if item fell off the bottom of the screen without being sliced
            if (_hasEnteredScreen && velocity.y < 0f && transform.position.y < missThresholdY)
            {
                // Bombs are meant to be avoided — only fruits deduct a life when missed!
                if (!isBomb && EventSlashGameManager.Instance != null)
                {
                    EventSlashGameManager.Instance.OnFruitMissed(this);
                }
                Destroy(gameObject);
            }
            else if (!_hasEnteredScreen && transform.position.y < missThresholdY - 3.0f)
            {
                Destroy(gameObject);
            }
        }

        public void SliceFruit(Vector2 slashDirection)
        {
            if (_isSliced) return;
            _isSliced = true;

            if (slashDirection.sqrMagnitude < 0.001f)
            {
                slashDirection = Vector2.right;
            }
            slashDirection.Normalize();

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

            Destroy(gameObject);
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

            ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
            if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                Material pMat = new Material(particleShader);
                Color blastCol = new Color(1.0f, 0.45f, 0.08f);
                pMat.color = blastCol;
                if (pMat.HasProperty("_BaseColor")) pMat.SetColor("_BaseColor", blastCol);
                psr.material = pMat;
            }

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

            ParticleSystemRenderer psr = fxObj.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader == null) particleShader = Shader.Find("Particles/Standard Unlit");
            if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
            if (particleShader != null)
            {
                Material pMat = new Material(particleShader);
                pMat.color = juiceColor;
                if (pMat.HasProperty("_BaseColor")) pMat.SetColor("_BaseColor", juiceColor);
                psr.material = pMat;
            }

            ps.Play();
            Destroy(fxObj, 1.2f);
        }
    }
}
