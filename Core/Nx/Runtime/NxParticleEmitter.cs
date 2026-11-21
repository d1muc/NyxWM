using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace nyxWM.Nx
{
    /// <summary>
    /// Spawns and animates particles around an element. Each particle has a random
    /// starting position along the element's perimeter, a random direction, and a
    /// finite lifetime. Particles are rendered as small shapes or images.
    /// </summary>
    internal sealed class NxParticleEmitter
    {
        private readonly List<Particle> _particles = new();
        // Update runs on the frame-clock thread while Paint runs on the UI
        // thread, so every access to the particle list goes through this lock.
        private readonly object _particlesLock = new();
        private readonly Random _rng = new(Environment.TickCount);
        private readonly string _imagePath;
        private readonly string _sourceFile;
        private double _elapsedMs;
        private double _spawnAccumMs;
        private bool _initialSpawn;

        internal NxParticleEmitter(NxStyle style)
        {
            _sourceFile = ""; // set externally if needed
            _imagePath = style.ParticleImage;
            SpawnBatch(style, 0);
        }

        internal NxParticleEmitter(NxStyle style, string sourceFile) : this(style)
        {
            _sourceFile = sourceFile;
        }

        internal void Update(double deltaMs, RectangleF elementBounds, NxStyle style)
        {
            lock (_particlesLock)
            {
                Advance(deltaMs, elementBounds, style);
            }
        }

        private void Advance(double deltaMs, RectangleF elementBounds, NxStyle style)
        {
            double delta = Math.Clamp(deltaMs, 1.0, 100.0);
            _elapsedMs += delta;

            double spawnInterval = style.ParticleCount > 0
                ? style.ParticleLifetime / style.ParticleCount
                : style.ParticleLifetime;

            _spawnAccumMs += delta;

            while (_spawnAccumMs >= spawnInterval)
            {
                _spawnAccumMs -= spawnInterval;
                SpawnParticle(elementBounds, style);
            }

            // Initial spawn burst
            if (!_initialSpawn)
            {
                _initialSpawn = true;
                for (int i = 0; i < (int)style.ParticleCount / 3; i++)
                    SpawnParticle(elementBounds, style);
            }

            // Advance each particle
            float dtSec = (float)delta / 1000f;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                p.AgeMs += delta;
                p.X += p.Vx * dtSec;
                p.Y += p.Vy * dtSec;
                p.Rotation += p.RotSpeed * dtSec;

                float lifeFraction = (float)Math.Clamp(p.AgeMs / p.LifetimeMs, 0.0, 1.0);
                p.Scale = 0.2f + 0.8f * (1f - lifeFraction);
                p.Alpha = (byte)(255 * (1f - lifeFraction * lifeFraction));

                if (p.AgeMs >= p.LifetimeMs)
                    _particles.RemoveAt(i);
            }
        }

        private void SpawnParticle(RectangleF bounds, NxStyle style)
        {
            // Random position along the perimeter
            float perimeter = 2f * (bounds.Width + bounds.Height);
            float pos = (float)(_rng.NextDouble() * perimeter);

            float x, y;
            if (pos < bounds.Width)
            {
                x = bounds.Left + pos;
                y = bounds.Top;
            }
            else if (pos < bounds.Width + bounds.Height)
            {
                x = bounds.Right;
                y = bounds.Top + (pos - bounds.Width);
            }
            else if (pos < bounds.Width * 2 + bounds.Height)
            {
                x = bounds.Left + (pos - bounds.Width - bounds.Height);
                y = bounds.Bottom;
            }
            else
            {
                x = bounds.Left;
                y = bounds.Top + (pos - bounds.Width * 2 - bounds.Height);
            }

            // Random direction outward
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float speed = (float)(style.ParticleSpeed * (0.5 + _rng.NextDouble() * 1.0));

            _particles.Add(new Particle
            {
                X = x, Y = y,
                Vx = (float)Math.Cos(angle) * speed,
                Vy = (float)Math.Sin(angle) * speed,
                LifetimeMs = style.ParticleLifetime * (0.5 + _rng.NextDouble() * 1.0),
                AgeMs = 0,
                Size = (float)(style.ParticleSize * (0.5 + _rng.NextDouble() * 1.0)),
                Scale = 1f,
                Alpha = 255,
                Rotation = (float)(_rng.NextDouble() * 360f),
                RotSpeed = (float)((_rng.NextDouble() - 0.5) * 180f)
            });
        }

        private void SpawnBatch(NxStyle style, int count)
        {
            // called once initially to pre-populate some particles
        }

        internal void Paint(Graphics graphics, NxStyle style)
        {
            Particle[] snapshot;
            lock (_particlesLock)
            {
                if (_particles.Count == 0) return;
                snapshot = _particles.ToArray();
            }

            GraphicsState saved = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            NxColor baseColor = style.GlowColor.A > 0 ? style.GlowColor : style.Foreground;
            Image? image = null;
            if (_imagePath.Length > 0 && _sourceFile.Length > 0)
                image = NxImageCache.Get(_imagePath, _sourceFile);

            foreach (Particle p in snapshot)
            {
                if (p.Alpha == 0) continue;

                float s = p.Size * p.Scale;
                var particleBox = new RectangleF(p.X - s / 2f, p.Y - s / 2f, s, s);
                Color color = Color.FromArgb(p.Alpha, baseColor.R, baseColor.G, baseColor.B);

                if (image != null)
                {
                    graphics.DrawImage(image, particleBox);
                }
                else
                {
                    using GraphicsPath path = NxWidget.BuildShape(
                        style.ParticleShape, particleBox, s * 0.4f);
                    using var brush = new SolidBrush(color);
                    graphics.FillPath(brush, path);
                }
            }

            graphics.Restore(saved);
        }

        private sealed class Particle
        {
            internal float X, Y;
            internal float Vx, Vy;
            internal double LifetimeMs, AgeMs;
            internal float Size, Scale;
            internal byte Alpha;
            internal float Rotation, RotSpeed;
        }
    }
}