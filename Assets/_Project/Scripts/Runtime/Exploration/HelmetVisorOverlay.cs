using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The inside of the helmet in first person: the visor rim darkens the edges of the view (drawn behind the HUD). When the
    /// sprint reserve runs low the rim closes in — the "tunnel vision" of a hard effort. Visual only.
    /// </summary>
    public sealed class HelmetVisorOverlay : MonoBehaviour
    {
        Texture2D rim;

        public bool Visible { get; set; }
        /// <summary>0 = rested, 1 = exhausted.</summary>
        public float Strain { get; set; }

        void OnDestroy()
        {
            if (rim != null) Destroy(rim);
        }

        void OnGUI()
        {
            if (!Visible || FlightInput.PauseMenuOpen) return;
            if (rim == null) rim = BuildRim(192, 128);
            GUI.depth = 10; // behind the instrument panels (lower depth is drawn on top)
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(screen, rim);
            if (Strain > 0.01f)
            {
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(Strain));
                GUI.DrawTexture(screen, rim);
                GUI.DrawTexture(screen, rim);
                GUI.color = c;
            }
            GUI.matrix = old;
        }

        /// <summary>Elliptical rim: clear in the middle, a soft dark edge in the corners.</summary>
        static Texture2D BuildRim(int width, int height)
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height * 2f - 1f;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width * 2f - 1f;
                    float r = Mathf.Sqrt(u * u * 0.85f + v * v * 1.15f);
                    float edge = Mathf.Clamp01((r - 0.82f) / 0.5f);
                    edge = edge * edge * (3f - 2f * edge);
                    t.SetPixel(x, y, new Color(0.01f, 0.012f, 0.016f, 0.8f * edge));
                }
            }
            t.Apply();
            return t;
        }
    }
}
