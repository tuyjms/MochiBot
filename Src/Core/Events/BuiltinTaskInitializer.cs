using catgirlwindow.Src.Core.Config;

namespace catgirlwindow.Src.Core.Events
{
    /// <summary>
    /// 内置任务初始化器
    /// 从配置文件读取系统内置任务配置，注册到事件调度器
    /// 可用于 feature：重启后的触发器恢复
    /// </summary>
    public class BuiltinTaskInitializer
    {
        private readonly IEventDispatcher _eventDispatcher;
        private readonly IConfigReader _configReader;

        public BuiltinTaskInitializer(IEventDispatcher eventDispatcher, IConfigReader configReader)
        {
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _configReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
        }

        /// <summary>
        /// 初始化所有内置任务
        /// 从配置文件的 CronTasks 数组读取任务配置并注册到事件调度器
        /// </summary>
        public void Initialize()
        {
            var cronTasks = _configReader.GetCronTasks();

            foreach (var task in cronTasks)
            {
                if (task.Enabled)
                {
                    _eventDispatcher.RegisterTask(task);
                }
            }
        }

        /// <summary>
        /// 恢复已注册的任务（用于重启后恢复触发器）
        /// 从持久化存储中读取之前注册的任务并重新注册
        /// </summary>
        public void RestoreTasks(List<CronTask> savedTasks)
        {
            if (savedTasks == null) return;

            foreach (var task in savedTasks)
            {
                if (task.Id.StartsWith("builtin:")) continue;
                _eventDispatcher.RegisterTask(task);
            }
        }
    }
}
