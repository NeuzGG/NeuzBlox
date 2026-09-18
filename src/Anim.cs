using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace NeuzBlox
{
    public enum Ease { Linear, OutCubic, InOutCubic, OutBack, OutQuint }

    /// <summary>
    /// One eased value moving towards a target, repainting its control as it goes.
    /// Everything visual in NeuzBlox animates through these rather than each control
    /// owning a timer - one shared clock drives them all and stops when nothing moves.
    /// </summary>
    public sealed class Anim
    {
        public static bool Enabled = true;

        readonly Control _target;
        float _from, _to, _value;
        int _durationMs, _elapsedMs, _delayMs;
        Ease _ease = Ease.OutCubic;
        bool _running;
        bool _loop;

        public Action Completed;

        public Anim(Control target) : this(target, 0f) { }

        public Anim(Control target, float initial)
        {
            _target = target;
            _value = initial;
            _from = initial;
            _to = initial;
        }

        public float Value { get { return _value; } }
        public bool Running { get { return _running; } }

        /// <summary>0-255 alpha from the current value, for fading custom-painted content.</summary>
        public int Alpha
        {
            get
            {
                int a = (int)(_value * 255f);
                return a < 0 ? 0 : (a > 255 ? 255 : a);
            }
        }

        public void Set(float v)
        {
            _value = v;
            _from = v;
            _to = v;
            _running = false;
        }

        public void To(float target, int ms) { To(target, ms, Ease.OutCubic, 0); }
        public void To(float target, int ms, Ease ease) { To(target, ms, ease, 0); }

        public void To(float target, int ms, Ease ease, int delayMs)
        {
            if (!Enabled || ms <= 0)
            {
                Set(target);
                Repaint();
                Fire();
                return;
            }
            if (_running && Math.Abs(_to - target) < 0.0005f) return;
            if (!_running && Math.Abs(_value - target) < 0.0005f) return;

            _from = _value;
            _to = target;
            _durationMs = ms;
            _elapsedMs = 0;
            _delayMs = delayMs;
            _ease = ease;
            _running = true;
            AnimClock.Register(this);
        }

        /// <summary>Runs 0 to 1 forever - used for shimmer and pulse effects.</summary>
        public void Loop(int ms)
        {
            if (!Enabled) return;
            _loop = true;
            _from = 0f;
            _to = 1f;
            _value = 0f;
            _durationMs = ms;
            _elapsedMs = 0;
            _delayMs = 0;
            _ease = Ease.Linear;
            _running = true;
            AnimClock.Register(this);
        }

        public void Stop()
        {
            _loop = false;
            _running = false;
        }

        internal bool Advance(int deltaMs)
        {
            if (!_running) return false;

            if (_delayMs > 0)
            {
                _delayMs -= deltaMs;
                if (_delayMs > 0) return true;
                deltaMs = -_delayMs;
                _delayMs = 0;
            }

            _elapsedMs += deltaMs;
            float t = _durationMs <= 0 ? 1f : (float)_elapsedMs / _durationMs;
            if (t > 1f) t = 1f;

            _value = _from + (_to - _from) * Apply(_ease, t);
            Repaint();

            if (t >= 1f)
            {
                if (_loop)
                {
                    _elapsedMs = 0;
                    _value = 0f;
                    return true;
                }
                _value = _to;
                _running = false;
                Fire();
                return false;
            }
            return true;
        }

        void Repaint()
        {
            try
            {
                if (_target != null && !_target.IsDisposed && _target.IsHandleCreated)
                    _target.Invalidate();
            }
            catch { }
        }

        void Fire()
        {
            Action c = Completed;
            if (c != null) { try { c(); } catch { } }
        }

        static float Apply(Ease e, float t)
        {
            switch (e)
            {
                case Ease.OutCubic:
                    {
                        float u = 1f - t;
                        return 1f - u * u * u;
                    }
                case Ease.OutQuint:
                    {
                        float u = 1f - t;
                        return 1f - u * u * u * u * u;
                    }
                case Ease.InOutCubic:
                    return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3) / 2f;
                case Ease.OutBack:
                    {
                        const float c1 = 1.70158f, c3 = c1 + 1f;
                        float u = t - 1f;
                        return 1f + c3 * u * u * u + c1 * u * u;
                    }
                default:
                    return t;
            }
        }
    }

    /// <summary>Single ~60fps timer shared by every animation; idles itself when nothing moves.</summary>
    internal static class AnimClock
    {
        static readonly List<Anim> Items = new List<Anim>();
        static Timer _timer;
        static DateTime _last;

        public static void Register(Anim a)
        {
            if (!Items.Contains(a)) Items.Add(a);
            if (_timer == null)
            {
                _timer = new Timer();
                _timer.Interval = 16;
                _timer.Tick += Tick;
            }
            if (!_timer.Enabled)
            {
                _last = DateTime.UtcNow;
                _timer.Start();
            }
        }

        static void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            int dt = (int)(now - _last).TotalMilliseconds;
            if (dt <= 0) dt = 16;
            if (dt > 120) dt = 120;
            _last = now;

            Anim[] snapshot = Items.ToArray();
            foreach (Anim a in snapshot)
                if (!a.Advance(dt)) Items.Remove(a);

            if (Items.Count == 0) _timer.Stop();
        }
    }
}
