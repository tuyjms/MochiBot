namespace MochiBot.Src.Agent
{
    /// <summary>
    /// Prompt格式化器接口
    /// 通用模板格式化工具，接收模板字符串和变量字典，将占位符替换为实际值
    /// </summary>
    public interface IPromptFormatter
    {
        /// <summary>使用变量字典填充模板，返回格式化后的字符串</summary>
        /// <param name="variables">变量名到值的映射</param>
        string Format(Dictionary<string, string> variables);
    }
}
