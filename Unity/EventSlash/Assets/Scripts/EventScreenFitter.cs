using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Ensures the EventCameraScreen Quad in 3D space stays centered in front of the static Main Camera
    /// and scales to fill 100% of the camera viewport at any screen resolution / aspect ratio.
    /// </summary>
    [ExecuteAlways]
    public class EventScreenFitter : MonoBehaviour
    {
        public Camera targetCamera;
        public float distanceFromCamera = 15.0f;

        private void LateUpdate()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                if (targetCamera == null) return;
            }

            // Keep the quad directly in front of the static camera
            transform.position = targetCamera.transform.position + targetCamera.transform.forward * distanceFromCamera;
            transform.rotation = targetCamera.transform.rotation;

            float height;
            if (targetCamera.orthographic)
            {
                height = targetCamera.orthographicSize * 2.0f;
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
