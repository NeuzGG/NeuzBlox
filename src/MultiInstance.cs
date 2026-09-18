using System;
using System.Threading;

namespace NeuzBlox
{
    /// <summary>
    /// Holds the named kernel objects the Roblox client uses for its "only one copy of me"
    /// check. While NeuzBlox owns them, every client that launches afterwards keeps running
    /// instead of handing off to the first one, which is what lets several accounts be
    /// online at the same time. Nothing about the client is patched or modified.
    /// </summary>
    public static class MultiInstance
    {
        public const string MutexName = "ROBLOX_singletonMutex";
        public const string EventName = "ROBLOX_singletonEvent";

        static Thread _holder;
        static ManualResetEvent _release;
        static volatile bool _active;
        static volatile bool _owned;
        static volatile string _detail = "Locked";

        public static event EventHandler StateChanged;

        public static bool Active { get { return _active; } }

        /// <summary>True once NeuzBlox itself owns the lock, rather than riding on a client that already had it.</summary>
        public static bool Owned { get { return _owned; } }

        public static string Detail { get { return _detail; } }

        public static void Enable()
        {
            if (_active) return;
            _release = new ManualResetEvent(false);
            var ready = new ManualResetEvent(false);

            // Mutex ownership is thread-affine: the thread that takes it has to stay alive,
            // otherwise Windows marks the mutex abandoned and the lock is gone.
            _holder = new Thread(delegate()
            {
                Mutex mutex = null;
                EventWaitHandle evt = null;
                bool owned = false;
                try
                {
                    bool createdNew;
                    mutex = new Mutex(false, MutexName, out createdNew);

                    try { evt = new EventWaitHandle(false, EventResetMode.ManualReset, EventName); }
                    catch { }

                    _active = true;
                    _detail = "Unlocked";
                    ready.Set();
                    Raise();

                    // A client that was already open owns the lock. The lock existing is enough
                    // for more clients to start, but we keep asking for it so that NeuzBlox holds
                    // it the moment that client quits - otherwise the unlock would die with it.
                    while (!_release.WaitOne(0))
                    {
                        try { owned = mutex.WaitOne(400, false); }
                        catch (AbandonedMutexException) { owned = true; }
                        if (owned) break;
                        if (_detail != "Shared with a client")
                        {
                            _detail = "Shared with a client";
                            Raise();
                        }
                    }

                    if (owned)
                    {
                        _owned = true;
                        _detail = "Unlocked";
                        Raise();
                    }

                    _release.WaitOne();
                }
                catch (Exception ex)
                {
                    _detail = "Failed: " + ex.Message;
                    _active = false;
                    ready.Set();
                    Raise();
                }
                finally
                {
                    try { if (owned && mutex != null) mutex.ReleaseMutex(); }
                    catch { }
                    try { if (mutex != null) mutex.Close(); }
                    catch { }
                    try { if (evt != null) evt.Close(); }
                    catch { }
                }
            });
            _holder.IsBackground = true;
            _holder.Name = "NeuzBlox.MutexHolder";
            _holder.Start();
            ready.WaitOne(3000);
        }

        public static void Disable()
        {
            if (!_active) return;
            _active = false;
            _owned = false;
            _detail = "Locked";
            try { if (_release != null) _release.Set(); }
            catch { }
            try { if (_holder != null) _holder.Join(1500); }
            catch { }
            _holder = null;
            _release = null;
            Raise();
        }

        /// <summary>True when the mutex exists right now, whoever owns it.</summary>
        public static bool IsMutexPresent()
        {
            try
            {
                Mutex probe;
                if (Mutex.TryOpenExisting(MutexName, out probe))
                {
                    probe.Close();
                    return true;
                }
            }
            catch { }
            return false;
        }

        static void Raise()
        {
            var h = StateChanged;
            if (h != null) { try { h(null, EventArgs.Empty); } catch { } }
        }
    }
}
