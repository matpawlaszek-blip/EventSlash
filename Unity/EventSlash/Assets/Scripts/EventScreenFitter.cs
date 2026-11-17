using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Ensures the EventCameraScreen Quad in 3D space fills 100% of the base camera viewport
    /// at any screen resolution / aspect ratio, while remaining anchored in world space
    /// so camera zoom, tilt, and shake effects during Pomegranate Frenzy visibly affect the scene.
    /// </summary>
    [ExecuteAlways]
    public class EventScreenFitter : MonoBehaviour
    {
        public Camera targetCamera;
        public float distanceFromCamera = 15.0f;
        public float baseOrthographicSize = 5.0f;

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera == null) return;
            }

            // Anchor at world center in front of the base camera position (0, 0, -10 + 15 = +5)
            transform.position = new Vector3(0f, 0f, -10.0f + distanceFromCamera);
            transform.rotation = Quaternion.identity;

            float height;
            if (targetCamera.orthographic)
            {
                float ortho = Mathf.Max(baseOrthographicSize, targetCamera.orthographicSize);
                // Add slight overscan buffer only while camera is actively tilted or offset so edges never show
                bool isCameraDisplaced = (targetCamera.transform.position - new Vector3(0f, 0f, -10f)).sqrMagnitude > 0.001f
                                         || Quaternion.Angle(targetCamera.transform.rotation, Quaternion.identity) > 0.05f;
                float margin = (isCameraDisplaced && targetCamera.orthographicSize > baseOrthographicSize * 0.92f) ? 1.08f : 1.0f;
                height = ortho * 2.0f * margin;
            }
            else
            {
                height = 2.0f * distanceFromCamera * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            float width = height * targetCamera.aspect;
            transform.localScale = new Vector3(width, height, 1.0f);
        }
    }
}
