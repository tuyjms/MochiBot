namespace catgirlwindow.Src.Agent
{
    /// <summary>
    /// Prompt格式化器实现
    /// 通用模板格式化工具，接收模板字符串和变量字典，将占位符替换为实际值
    /// </summary>
    public class PromptFormatter : IPromptFormatter
    {
        private readonly string _template;

        public PromptFormatter(string template)
        {
            _template = template;
        }

        /// <summary>
        /// 使用变量字典填充模板，返回格式化后的字符串
        /// </summary>
        /// <param name="variables">变量名到值的映射</param>
        /// <returns>格式化后的字符串</returns>
        public string Format(Dictionary<string, string> variables)
        {
            var result = _template;
            foreach (var kv in variables)
            {
                result = result.Replace($"{{{kv.Key}}}", kv.Value);
            }
            return result;
        }
    }
}
