using System.Collections.Concurrent;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Events;
using MochiBot.Src.EventModels;

namespace MochiBot.Src.Agent
{
    /// <summary>
    /// 事件处理队列 + 状态机
    /// 负责事件入队、队列溢出管理、串行处理循环、状态转换和状态上报
    /// 实际事件处理逻辑通过回调委托给调用方
    /// </summary>
    public class EventProcessingQueue : IDisposable
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;
        private readonly Func<EventData, Task> _processEventInternal;

        private readonly ConcurrentQueue<EventData> _eventQueue = new();
        private const int MaxQueueSize = 20;
        private volatile AgentState _state = AgentState.Idle;
        private readonly SemaphoreSlim _processLock = new(1, 1);

        /// <summary>当前 Agent 状态</summary>
        public AgentState State => _state;

        /// <summary>是否正在处理事件</summary>
        public bool IsProcessing { get; private set; }

        /// <summary>最近一次事件/动作标识（用于状态报告）</summary>
        public string LastEvent { get; set; } = string.Empty;

        /// <summary>队列中的事件数量</summary>
        public int QueueCount => _eventQueue.Count;

        public EventProcessingQueue(
            IEventDispatcher eventDispatcher,
            IConfigReader configReader,
            Func<EventData, Task> processEventInternal)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
            _processEventInternal = processEventInternal ?? throw new ArgumentNullException(nameof(processEventInternal));
        }

        /// <summary>事件入队 + 触发处理循环</summary>
        public Task EnqueueEventAsync(EventData eventData)
        {
            // 队列满时丢弃最旧事件
            while (_eventQueue.Count >= MaxQueueSize)
            {
                _eventQueue.TryDequeue(out _);
                _configReader.Logger.Warn("[Agent] 事件队列已满，丢弃最旧事件");
            }

            _eventQueue.Enqueue(eventData);
            _configReader.Logger.Debug($"[Agent] 事件已入队: {eventData.Category}, 队列长度: {_eventQueue.Count}");

            TryStartProcessing();
            return Task.CompletedTask;
        }

        /// <summary>尝试启动处理循环（仅 Idle 状态可启动）</summary>
        private void TryStartProcessing()
        {
            if (_state != AgentState.Idle) return;
            if (!_processLock.Wait(0)) return;

            _ = ProcessQueueAsync();
        }

        /// <summary>事件处理循环：从队列逐个取出事件串行处理</summary>
        private async Task ProcessQueueAsync()
        {
            try
            {
                while (_eventQueue.TryDequeue(out var eventData))
                {
                    SetState(AgentState.Thinking);
                    IsProcessing = true;
                    try
                    {
                        await _processEventInternal(eventData);
                    }
                    catch (Exception ex)
                    {
                        _configReader.Logger.Error($"[Agent] 处理事件异常: {eventData.Category}", ex);
                        SetState(AgentState.Error);
                        await Task.Delay(1000); // 错误冷却
                    }
                    finally
                    {
                        IsProcessing = false;
                        SetState(AgentState.Idle);
                    }
                }
            }
            finally
            {
                _processLock.Release();
            }
        }

        /// <summary>设置 Agent 状态并上报到 EventDispatcher</summary>
        private void SetState(AgentState newState)
        {
            if (_state == newState) return;
            _state = newState;
            _configReader.Logger.Debug($"[Agent] 状态: {newState}");
            _eventDispatcher.UpdateModuleState("agent", newState.ToString().ToLower());
        }

        public void Dispose()
        {
            _processLock.Dispose();
        }
    }
}
