using System;

namespace SpaceSim.Mathematics
{
    /// <summary>Engine-independent serializable colour (components 0..1, sRGB-encoded).</summary>
    [Serializable]
    public struct ColorRGB
    {
        public float r;
        public float g;
        public float b;

        public ColorRGB(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public static ColorRGB Lerp(ColorRGB a, ColorRGB c, double t)
        {
            float f = (float)MathD.Clamp01(t);
            return new ColorRGB(a.r + (c.r - a.r) * f, a.g + (c.g - a.g) * f, a.b + (c.b - a.b) * f);
        }

        public static ColorRGB operator *(ColorRGB a, float s) => new ColorRGB(a.r * s, a.g * s, a.b * s);
    }
}
