using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using KCTools.Models;

namespace KCTools.Common
{
    /// <summary>Uygulama içi olay işaretleyicisi.</summary>
    public interface IAppEvent
    {
    }

    /// <summary>Cihaz takıldı/çıkarıldı.</summary>
    public sealed class DeviceStateChangedEvent : IAppEvent
    {
        public DetectedDevice Device { get; set; }

        public bool Connected { get; set; }
    }

    /// <summary>Bir modülde işlem başladı/bitti.</summary>
    public sealed class OperationStateChangedEvent : IAppEvent
    {
        public string Module { get; set; }

        public string Operation { get; set; }

        public bool Started { get; set; }

        public bool Success { get; set; }
    }

    /// <summary>Başka bir sekmeye geçiş isteği (ör. Cihaz → IMEI Onarımı).</summary>
    public sealed class NavigationRequestedEvent : IAppEvent
    {
        public string Key { get; set; }
    }

    /// <summary>ADB/Fastboot yoklamasında cihaz göründü veya kayboldu.</summary>
    public sealed class DevicePresenceEvent : IAppEvent
    {
        public MonitoredDevice Device { get; set; }

        public bool Connected { get; set; }
    }

    /// <summary>
    /// Global olay merkezi: servisler (DeviceWatcher, sayfa VM'leri, lisans)
    /// birbirini doğrudan tanımadan olay yayınlar. Aboneler UI iş parçacığına
    /// otomatik taşınır; hatalı bir abone diğerlerini etkilemez.
    /// </summary>
    public sealed class EventAggregator
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers =
            new ConcurrentDictionary<Type, List<Delegate>>();

        private readonly object _gate = new object();

        private EventAggregator()
        {
        }

        public static EventAggregator Instance { get; } = new EventAggregator();

        public IDisposable Subscribe<T>(Action<T> handler) where T : IAppEvent
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (_gate)
            {
                var list = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
                list.Add(handler);
            }

            return new Subscription(() => Unsubscribe(handler));
        }

        public void Publish<T>(T payload) where T : IAppEvent
        {
            List<Delegate> snapshot;

            lock (_gate)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                {
                    return;
                }

                snapshot = new List<Delegate>(list);
            }

            foreach (var handler in snapshot)
            {
                var action = handler as Action<T>;

                if (action == null)
                {
                    continue;
                }

                Dispatch(() =>
                {
                    try
                    {
                        action(payload);
                    }
                    catch (Exception ex)
                    {
                        Services.LogHub.Instance.Error(
                            "Olay abonesi hata verdi: " + ex.Message, "EVENT");
                    }
                });
            }
        }

        private void Unsubscribe<T>(Action<T> handler)
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    list.Remove(handler);
                }
            }
        }

        private static void Dispatch(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(action);
                return;
            }

            action();
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                var action = _dispose;
                _dispose = null;
                action?.Invoke();
            }
        }
    }
}
