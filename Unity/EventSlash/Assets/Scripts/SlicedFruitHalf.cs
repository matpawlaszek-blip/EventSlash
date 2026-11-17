using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Controls the physical motion and rotation of a sliced fruit half (Half_A or Half_B)
    /// or an exploded pomegranate piece shot outward in all directions.
    /// </summary>
    public class SlicedFruitHalf : MonoBehaviour
    {
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float gravity = -18.5f;
        public float destroyBelowY = -9.5f;
        public float maxLifetime = 4.0f;

        private float _age;

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;

            velocity.y += gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(angularVelocity * dt, Space.World);

            if (transform.position.y < destroyBelowY ||
                Mathf.Abs(transform.position.x) > 18.0f ||
                transform.position.y > 14.0f ||
                _age >= maxLifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}
