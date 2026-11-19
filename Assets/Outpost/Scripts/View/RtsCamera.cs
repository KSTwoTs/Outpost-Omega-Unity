using UnityEngine;
using UnityEngine.InputSystem;

namespace Outpost.View
{
    /// <summary>
    /// Halo Wars style camera: a pivot that slides over the battlefield with the camera on a tilted arm.
    /// Arrows / screen edges / middle-mouse drag pan, mouse wheel zooms (the arm also tilts as you zoom),
    /// Page Up / Page Down or Alt + middle drag rotate, Home resets the rotation.
    /// (Letter keys are left free for unit commands, as in the Python game.)
    /// </summary>
    public class RtsCamera : MonoBehaviour
    {
        public Camera cam;
        public float panSpeed = 60f;
        public float edgePixels = 12f;
        public bool edgePan = true;
        public float minDistance = 18f, maxDistance = 140f;
        public float minPitch = 38f, maxPitch = 62f;
        public float distance = 70f;
        public float yaw = 0f;
        public Vector2 boundsMin = new Vector2(0, -400), boundsMax = new Vector2(600, 0);

        float targetDistance, targetYaw;
        Vector3 targetPos;

        // ------------------------------------------------------------------ camera shake (Python effects.shake)
        static RtsCamera active;
        static float trauma;            // 0..1; the shake is trauma squared, so small hits stay small
        public static float Trauma => trauma;
        public static RtsCamera Active => active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            active = null;
            trauma = 0f;
        }

        /// <summary>Adds shake (0..1). Effects call ShakeAt so far-away blasts shake less.</summary>
        public static void Shake(float amount) => trauma = Mathf.Clamp01(trauma + Mathf.Max(0f, amount));

        /// <summary>Shake from something happening at a world point: full strength near the middle of the view,
        /// none beyond about 75 units. Does nothing without an active camera (edit-mode tests).</summary>
        public static void ShakeAt(Vector3 at, float amount)
        {
            if (active == null) return;
            var d = at - active.transform.position;
            d.y = 0f;
            float k = Mathf.Clamp01(1f - (d.magnitude - 15f) / 60f);
            if (k > 0f) Shake(amount * k);
        }

        void Start()
        {
            if (cam == null) cam = GetComponentInChildren<Camera>();
            active = this;
            targetDistance = distance;
            targetYaw = yaw;
            targetPos = transform.position;
            Apply(1f);
        }

        public void JumpTo(Vector3 worldPos)
        {
            targetPos = new Vector3(worldPos.x, worldPos.y, worldPos.z);
            transform.position = targetPos;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed) move.y += 1;
                if (kb.downArrowKey.isPressed) move.y -= 1;
                if (kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.pageUpKey.isPressed) targetYaw -= 90f * dt;
                if (kb.pageDownKey.isPressed) targetYaw += 90f * dt;
                if (kb.homeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) targetYaw = 0f;
            }
            if (mouse != null)
            {
                var mp = mouse.position.ReadValue();
                bool inWindow = mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height;
                if (edgePan && inWindow && Application.isFocused)
                {
                    if (mp.x < edgePixels) move.x -= 1;
                    if (mp.x > Screen.width - edgePixels) move.x += 1;
                    if (mp.y < edgePixels) move.y -= 1;
                    if (mp.y > Screen.height - edgePixels) move.y += 1;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    targetDistance = Mathf.Clamp(targetDistance * (scroll > 0 ? 0.88f : 1.0f / 0.88f), minDistance, maxDistance);
                if (mouse.middleButton.isPressed)
                {
                    var md = mouse.delta.ReadValue();
                    if (kb != null && kb.altKey.isPressed) targetYaw += md.x * 0.25f;
                    else
                    {
                        // grab-and-drag the ground: move opposite to the mouse, scaled by zoom
                        float k = distance * 0.0022f;
                        var r = Quaternion.Euler(0, targetYaw, 0);
                        targetPos -= r * new Vector3(md.x, 0, md.y) * k;
                    }
                }
            }
            if (move != Vector2.zero)
            {
                var rot = Quaternion.Euler(0, targetYaw, 0);
                var dir = rot * new Vector3(move.x, 0, move.y);
                float speed = panSpeed * Mathf.Lerp(0.5f, 1.6f, Mathf.InverseLerp(minDistance, maxDistance, targetDistance));
                targetPos += dir.normalized * speed * dt;
            }
            targetPos.x = Mathf.Clamp(targetPos.x, boundsMin.x, boundsMax.x);
            targetPos.z = Mathf.Clamp(targetPos.z, boundsMin.y, boundsMax.y);
            Apply(1f - Mathf.Exp(-12f * dt));
            if (trauma > 0f && cam != null)
            {
                float s = trauma * trauma * 1.3f, tm = Time.unscaledTime * 28f;
                cam.transform.localPosition += new Vector3(Mathf.PerlinNoise(tm, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, tm) - 0.5f, Mathf.PerlinNoise(tm, 5.1f) - 0.5f) * 2f * s;
                cam.transform.localRotation *= Quaternion.Euler((Mathf.PerlinNoise(tm, 9.2f) - 0.5f) * 1.6f * s, (Mathf.PerlinNoise(3.3f, tm) - 0.5f) * 1.6f * s, 0f);
                trauma = Mathf.Max(0f, trauma - dt * 1.4f);
            }
        }

        void OnDestroy()
        {
            if (active == this) active = null;
        }

        void Apply(float k)
        {
            distance = Mathf.Lerp(distance, targetDistance, k);
            yaw = Mathf.LerpAngle(yaw, targetYaw, k);
            var p = Vector3.Lerp(transform.position, targetPos, k);
            // keep the pivot on the ground so zoom stays centred on what you are looking at
            if (Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out var hit, 1000f))
                p.y = Mathf.Lerp(transform.position.y, hit.point.y, k);
            transform.position = p;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            float pitch = Mathf.Lerp(minPitch, maxPitch, Mathf.InverseLerp(minDistance, maxDistance, distance));
            if (cam != null)
            {
                var arm = Quaternion.Euler(pitch, 0, 0) * new Vector3(0, 0, -distance);
                cam.transform.localPosition = arm;
                cam.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
        }
    }
}
