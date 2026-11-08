using System.Collections.Generic;
using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Renders visual blade/hand markers and trails over the event camera view
    /// corresponding to the detected active hand motion clusters (up to 2 hands + mouse fallback).
    /// Matches each detected hand to its nearest active trail so single/dual hand transitions stay smooth.
    /// </summary>
    public class SlashTrailVisualizer : MonoBehaviour
    {
        public Camera targetCamera;
        public float planeZ = -1.2f;
        public int maxTrails = 3;
        public Color trailColor = new Color(0.15f, 0.95f, 1.0f, 0.9f);

        private readonly List<TrailRenderer> _trails = new List<TrailRenderer>();
        private readonly List<SlashCluster> _clusters = new List<SlashCluster>();

        private void Start()
        {
            if (targetCamera == null) targetCamera = Camera.main;

            Shader trailShader = Shader.Find("Sprites/Default");
            if (trailShader == null) trailShader = Shader.Find("Universal Render Pipeline/Unlit");

            int trailCount = Mathf.Max(2, maxTrails);
            for (int i = 0; i < trailCount; i++)
            {
                GameObject tObj = new GameObject($"HandBladeTrail_{i}");
                tObj.transform.SetParent(transform, false);

                TrailRenderer tr = tObj.AddComponent<TrailRenderer>();
                tr.time = 0.22f;
                tr.minVertexDistance = 0.08f;
                tr.startWidth = 0.24f;
                tr.endWidth = 0.02f;
                tr.numCapVertices = 4;
                tr.numCornerVertices = 4;
                tr.emitting = false;

                if (trailShader != null)
                {
                    Material mat = new Material(trailShader);
                    mat.color = trailColor;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", trailColor);
                    tr.material = mat;
                }

                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.3f, 1.0f, 1.0f), 0.0f),
                        new GradientColorKey(new Color(0.1f, 0.6f, 1.0f), 1.0f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.9f, 0.0f),
                        new GradientAlphaKey(0.0f, 1.0f)
                    }
                );
                tr.colorGradient = grad;
                _trails.Add(tr);
            }
        }

        private void LateUpdate()
        {
            if (EventCameraManager.Instance == null || targetCamera == null)
                return;

            EventCameraManager.Instance.GetActiveClusters(_clusters);
            float distFromCam = Mathf.Abs(planeZ - targetCamera.transform.position.z);

            int clusterCount = Mathf.Min(_clusters.Count, _trails.Count);
            if (clusterCount == 0)
            {
                for (int i = 0; i < _trails.Count; i++)
                {
                    _trails[i].emitting = false;
                }
                return;
            }

            Vector3[] targetPositions = new Vector3[clusterCount];
            for (int c = 0; c < clusterCount; c++)
            {
                SlashCluster sc = _clusters[c];
                Vector3 wp = targetCamera.ViewportToWorldPoint(new Vector3(sc.viewportPos.x, sc.viewportPos.y, distFromCam));
                wp.z = planeZ;
                targetPositions[c] = wp;
            }

            bool[] trailUsed = new bool[_trails.Count];
            bool[] clusterAssigned = new bool[clusterCount];

            // 1. Match each cluster to the nearest already-emitting trail within 2.5 world units
            for (int c = 0; c < clusterCount; c++)
            {
                int bestTrail = -1;
                float bestDist = 2.5f;
                for (int t = 0; t < _trails.Count; t++)
                {
                    if (trailUsed[t] || !_trails[t].emitting)
                        continue;
                    float d = Vector3.Distance(_trails[t].transform.position, targetPositions[c]);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestTrail = t;
                    }
                }

                if (bestTrail >= 0)
                {
                    trailUsed[bestTrail] = true;
                    clusterAssigned[c] = true;
                    TrailRenderer tr = _trails[bestTrail];
                    tr.transform.position = Vector3.Lerp(tr.transform.position, targetPositions[c], 0.65f);
                    tr.emitting = true;
                }
            }

            // 2. Assign any remaining clusters to unused trails
            for (int c = 0; c < clusterCount; c++)
            {
                if (clusterAssigned[c])
                    continue;

                for (int t = 0; t < _trails.Count; t++)
                {
                    if (trailUsed[t])
                        continue;

                    trailUsed[t] = true;
                    clusterAssigned[c] = true;
                    TrailRenderer tr = _trails[t];
                    tr.emitting = false;
                    tr.transform.position = targetPositions[c];
                    tr.Clear();
                    tr.emitting = true;
                    break;
                }
            }

            // 3. Disable trails that were not matched this frame
            for (int t = 0; t < _trails.Count; t++)
            {
                if (!trailUsed[t])
                {
                    _trails[t].emitting = false;
                }
            }
        }
    }
}
