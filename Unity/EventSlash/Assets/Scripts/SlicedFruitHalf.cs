using UnityEngine;

namespace EventSlash
{
    /// <summary>
    /// Controls the physical motion and rotation of a sliced fruit half (Half_A or Half_B)
    /// after a fruit is cut by the player's hand.
    /// </summary>
    public class SlicedFruitHalf : MonoBehaviour
    {
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public float gravity = -18.5f;
        public float destroyBelowY = -9.5f;

        private void Update()
        {
            float dt = Time.deltaTime;
            velocity.y += gravity * dt;
            transform.position += velocity * dt;
            transform.Rotate(angularVelocity * dt, Space.World);

            if (transform.position.y < destroyBelowY)
            {
                Destroy(gameObject);
            }
        }
    }
}
