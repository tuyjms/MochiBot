using catgirlwindow.Src.Models;

namespace catgirlwindow.Src.Core.Events
{
    /// <summary>
    /// 事件调度器接口
    /// 统一管理所有事件的发布和订阅
    /// </summary>
    public interface IEventDispatcher
    {
        /// <summary>发布事件</summary>
        void Publish(EventData eventData);

        /// <summary>订阅指定分类的事件</summary>
        /// <param name="category">事件分类</param>
        /// <param name="handler">事件处理回调</param>
        /// <returns>订阅ID（用于取消订阅）</returns>
        string Subscribe(EventCategory category, Action<EventData> handler);

        /// <summary>订阅所有事件</summary>
        /// <param name="handler">事件处理回调</param>
        /// <returns>订阅ID（用于取消订阅）</returns>
        string SubscribeAll(Action<EventData> handler);

        /// <summary>取消订阅</summary>
        void Unsubscribe(string subscriptionId);

        /// <summary>获取指定分类的订阅者数量</summary>
        int GetSubscriberCount(EventCategory category);
    }
}
