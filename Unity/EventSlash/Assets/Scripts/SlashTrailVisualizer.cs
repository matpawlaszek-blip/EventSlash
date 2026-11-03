using System.Collections.Generic;
using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Renders visual blade/hand markers and trails over the event camera view
    /// corresponding to the detected active hand motion clusters.
    /// </summary>
    public class SlashTrailVisualizer : MonoBehaviour
    {
        public Camera targetCamera;
        public float planeZ = -1.2f;
        public int maxTrails = 4;
        public Color trailColor = new Color(0.15f, 0.95f, 1.0f, 0.9f);

        private readonly List<TrailRenderer> _trails = new List<TrailRenderer>();
        private readonly List<SlashCluster> _clusters = new List<SlashCluster>();

        private void Start()
        {
            if (targetCamera == null) targetCamera = Camera.main;

            Shader trailShader = Shader.Find("Sprites/Default");
            if (trailShader == null) trailShader = Shader.Find("Universal Render Pipeline/Unlit");

            for (int i = 0; i < maxTrails; i++)
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

            for (int i = 0; i < _trails.Count; i++)
            {
                TrailRenderer tr = _trails[i];
                if (i < _clusters.Count)
                {
                    SlashCluster c = _clusters[i];
                    Vector3 worldPos = targetCamera.ViewportToWorldPoint(new Vector3(c.viewportPos.x, c.viewportPos.y, distFromCam));
                    worldPos.z = planeZ;

                    if (!tr.emitting || Vector3.Distance(tr.transform.position, worldPos) > 2.5f)
                    {
                        tr.emitting = false;
                        tr.transform.position = worldPos;
                        tr.Clear();
                        tr.emitting = true;
                    }
                    else
                    {
                        tr.transform.position = Vector3.Lerp(tr.transform.position, worldPos, 0.65f);
                        tr.emitting = true;
                    }
                }
                else
                {
                    tr.emitting = false;
                }
            }
        }
    }
}
