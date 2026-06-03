using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;

namespace MochiBot.Src.UI.Settings
{
    /// <summary>
    /// Tab 2: LLM 提供商 — 加载、收集、保存 Provider 配置
    /// </summary>
    public class ProviderTabController
    {
        private readonly IConfigReader _configReader;
        private readonly StackPanel _providersPanel;

        public ProviderTabController(IConfigReader configReader, StackPanel providersPanel)
        {
            _configReader = configReader;
            _providersPanel = providersPanel;
        }

        /// <summary>加载 Provider 配置到动态 UI</summary>
        public void Load()
        {
            _providersPanel.Children.Clear();
            var providers = _configReader.GetAllProviders();

            foreach (var (name, config) in providers)
            {
                var expander = new Expander
                {
                    Header = name,
                    Margin = new Thickness(0, 0, 0, 8),
                    IsExpanded = true
                };

                var panel = new StackPanel { Margin = new Thickness(16, 4, 0, 4) };

                // ApiKey
                panel.Children.Add(new TextBlock { Text = "API Key", FontSize = 13, Margin = new Thickness(0, 2, 0, 2) });
                var apiKeyBox = new PasswordBox
                {
                    Height = 26, FontSize = 13, Padding = new Thickness(4, 0, 4, 0),
                    Tag = $"{name}:ApiKey", Password = config.ApiKey
                };
                panel.Children.Add(apiKeyBox);

                // BaseUrl
                panel.Children.Add(new TextBlock { Text = "Base URL", FontSize = 13, Margin = new Thickness(0, 6, 0, 2) });
                var baseUrlBox = new TextBox
                {
                    Height = 26, FontSize = 13, Padding = new Thickness(4, 0, 4, 0),
                    Tag = $"{name}:BaseUrl", Text = config.BaseUrl
                };
                panel.Children.Add(baseUrlBox);

                // ContextLimit
                panel.Children.Add(new TextBlock { Text = "上下文限制 (tokens)", FontSize = 13, Margin = new Thickness(0, 6, 0, 2) });
                var ctxLimitBox = new TextBox
                {
                    Height = 26, FontSize = 13, Padding = new Thickness(4, 0, 4, 0),
                    TextAlignment = TextAlignment.Center, Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Tag = $"{name}:ContextLimit", Text = config.ContextLimit.ToString()
                };
                panel.Children.Add(ctxLimitBox);

                expander.Content = panel;
                _providersPanel.Children.Add(expander);
            }
        }

        /// <summary>从 UI 收集 Provider 配置</summary>
        public Dictionary<string, ProviderConfig> Collect()
        {
            var result = new Dictionary<string, ProviderConfig>();
            foreach (var child in _providersPanel.Children)
            {
                if (child is not Expander expander || expander.Content is not StackPanel panel)
                    continue;

                var providerName = expander.Header?.ToString() ?? "";
                if (string.IsNullOrEmpty(providerName)) continue;

                var pc = new ProviderConfig();
                foreach (UIElement ctrl in panel.Children)
                {
                    if (ctrl is not FrameworkElement fe) continue;
                    var tag = ParseTag(fe);
                    if (tag == null) continue;
                    var (_, field) = tag.Value;
                    if (field == "ApiKey" && ctrl is PasswordBox pb)
                        pc.ApiKey = pb.Password;
                    else if (field == "BaseUrl" && ctrl is TextBox tb)
                        pc.BaseUrl = tb.Text;
                    else if (field == "ContextLimit" && ctrl is TextBox tbc && int.TryParse(tbc.Text, out var ctx))
                        pc.ContextLimit = ctx;
                }
                result[providerName] = pc;
            }
            return result;
        }

        private static (string provider, string field)? ParseTag(FrameworkElement ctrl)
        {
            var tag = ctrl.Tag?.ToString();
            if (string.IsNullOrEmpty(tag)) return null;
            var parts = tag.Split(':');
            if (parts.Length != 2) return null;
            return (parts[0], parts[1]);
        }
    }
}
