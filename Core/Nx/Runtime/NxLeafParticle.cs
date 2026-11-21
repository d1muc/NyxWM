using System.Drawing;
using System.IO;

namespace nyxWM.Nx
{
    /// <summary>
    /// Standalone leaf-particle renderer for WinForms windows (notifications,
    /// Rofi, settings, terminal). Wraps <see cref="NxParticleEmitter"/> so these
    /// windows can spawn leaves without depending on a full <c>NxWidget</c>.
    ///
    /// Usage: create once on window open, call <see cref="Update"/> each frame
    /// with the window bounds, call <see cref="Paint"/> at the end of the
    /// window's custom render pass.
    /// </summary>
    internal sealed class NxLeafParticle
    {
        private readonly NxParticleEmitter _emitter;
        private readonly NxStyle _style;

        private NxLeafParticle(NxStyle style, string sourceFile)
        {
            _style = style;
            _emitter = new NxParticleEmitter(style, sourceFile);
        }

        /// <summary>
        /// Creates a leaf-particle emitter configured from the theme's leaf tokens.
        /// Returns null when the leave icon cannot be resolved or the active theme
        /// does not support leaf particles.
        /// </summary>
        internal static NxLeafParticle? Create()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string themeFile = ConfigManager.Current?.ThemeFile ?? "leaf-storm.nx";

            // Only enable leaf particles for leaf-themed files.
            string name = Path.GetFileNameWithoutExtension(themeFile);
            if (!string.Equals(name, "leaf-storm", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(name, "nature-leaves", StringComparison.OrdinalIgnoreCase))
                return null;

            string sourceFile = Path.Combine(baseDir, "themes", themeFile);

            // Verify the icon exists so a missing file fails silently.
            string iconPath = Path.Combine(baseDir, "themes", "icons", "leave.png");
            if (!File.Exists(iconPath))
                return null;

            var style = new NxStyle
            {
                Particles = true,
                ParticleCount = 10,
                ParticleSpeed = 40,
                ParticleLifetime = 1800,
                ParticleSize = 24,
                ParticleImage = "icons/leave.png",
                ParticleShape = "circle",
                GlowColor = NxColor.Transparent,
                Foreground = NxColor.FromBytes(255, 119, 221, 68) // #77dd44
            };

            return new NxLeafParticle(style, sourceFile);
        }

        /// <summary>
        /// Creates a leaf-particle emitter with explicit config.
        /// </summary>
        internal static NxLeafParticle CreateWith(
            string imageRelativePath,
            string sourceFilePath,
            int count = 10,
            double speed = 40,
            double lifetime = 1800,
            double size = 24)
        {
            var style = new NxStyle
            {
                Particles = true,
                ParticleCount = count,
                ParticleSpeed = speed,
                ParticleLifetime = lifetime,
                ParticleSize = size,
                ParticleImage = imageRelativePath,
                ParticleShape = "circle",
                GlowColor = NxColor.Transparent,
                Foreground = NxColor.FromBytes(255, 119, 221, 68)
            };

            return new NxLeafParticle(style, sourceFilePath);
        }

        /// <summary>
        /// Advances particle positions and spawns new leaves.
        /// Call each frame from the window's tick with the frame delta.
        /// </summary>
        internal void Update(double deltaMs, RectangleF elementBounds)
        {
            _emitter.Update(deltaMs, elementBounds, _style);
        }

        /// <summary>
        /// Paints live particles onto the graphics surface.
        /// Call after the window's own content has been drawn.
        /// </summary>
        internal void Paint(Graphics graphics)
        {
            _emitter.Paint(graphics, _style);
        }
    }
}