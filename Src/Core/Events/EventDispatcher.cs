using System.Collections.Concurrent;
using catgirlwindow.Src.Models;

namespace catgirlwindow.Src.Core.Events
{
    /// <summary>
    /// 事件调度器实现
    /// 统一管理所有事件的发布和订阅
    /// </summary>
    public class EventDispatcher : IEventDispatcher
    {
        // 按事件分类存储订阅者
        private readonly ConcurrentDictionary<EventCategory, List<Subscription>> _categorySubscriptions = new();
        // 所有事件的订阅者
        private readonly List<Subscription> _allSubscriptions = new();
        private readonly Lock _lock = new();

        public void Publish(EventData eventData)
        {
            // 通知指定分类的订阅者
            if (_categorySubscriptions.TryGetValue(eventData.Category, out var categorySubs))
            {
                List<Subscription> snapshot;
                lock (_lock)
                {
                    snapshot = new List<Subscription>(categorySubs);
                }
                foreach (var sub in snapshot)
                {
                    try { sub.Handler(eventData); }
                    catch { /* 防止单个订阅者异常影响其他订阅者 */ }
                }
            }

            // 通知所有事件的订阅者
            List<Subscription> allSnapshot;
            lock (_lock)
            {
                allSnapshot = new List<Subscription>(_allSubscriptions);
            }
            foreach (var sub in allSnapshot)
            {
                try { sub.Handler(eventData); }
                catch { /* 防止单个订阅者异常影响其他订阅者 */ }
            }
        }

        public string Subscribe(EventCategory category, Action<EventData> handler)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                Handler = handler
            };

            lock (_lock)
            {
                var subs = _categorySubscriptions.GetOrAdd(category, _ => new List<Subscription>());
                subs.Add(subscription);
            }

            return subscription.Id;
        }

        public string SubscribeAll(Action<EventData> handler)
        {
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString(),
                Handler = handler
            };

            lock (_lock)
            {
                _allSubscriptions.Add(subscription);
            }

            return subscription.Id;
        }

        public void Unsubscribe(string subscriptionId)
        {
            lock (_lock)
            {
                // 从分类订阅中移除
                foreach (var kvp in _categorySubscriptions)
                {
                    kvp.Value.RemoveAll(s => s.Id == subscriptionId);
                }

                // 从全部订阅中移除
                _allSubscriptions.RemoveAll(s => s.Id == subscriptionId);
            }
        }

        public int GetSubscriberCount(EventCategory category)
        {
            lock (_lock)
            {
                if (_categorySubscriptions.TryGetValue(category, out var subs))
                    return subs.Count;
                return 0;
            }
        }

        /// <summary>
        /// 订阅记录
        /// </summary>
        private class Subscription
        {
            public string Id { get; set; } = string.Empty;
            public Action<EventData> Handler { get; set; } = _ => { };
        }
    }
}
