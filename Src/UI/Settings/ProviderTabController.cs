using System.Windows;
using System.Windows.Controls;
using MochiBot.Src.Core.Config;
using MochiBot.Src.Core.Config.Models;
using MochiBot.Src.Services;

namespace MochiBot.Src.UI.Settings
{
    /// <summary>
    /// Tab 2: LLM 提供商 — 加载、收集、保存 Provider 配置（含模型列表）
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

            // 顶部"添加提供商"按钮
            var addProviderBtn = new Button
            {
                Content = "＋ 添加提供商",
                Width = 120, Height = 28,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            addProviderBtn.Click += AddProvider_Click;
            _providersPanel.Children.Add(addProviderBtn);

            var providers = _configReader.GetAllProviders();

            foreach (var (name, config) in providers)
            {
                _providersPanel.Children.Add(CreateProviderSection(name, config));
            }
        }

        /// <summary>创建单个提供商的 Expander UI 区块</summary>
        private Expander CreateProviderSection(string name, ProviderConfig config)
        {
            // 标题栏：左侧提供商名 + 右侧删除按钮
            var headerPanel = new Grid { Margin = new Thickness(0, 0, 4, 0) };
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameText = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, FontSize = 14, FontWeight = FontWeights.SemiBold };
            Grid.SetColumn(nameText, 0);
            headerPanel.Children.Add(nameText);
            var deleteBtn = new Button { Content = "✕", Width = 24, Height = 24, FontSize = 12, Tag = name, ToolTip = "删除此提供商" };
            deleteBtn.Click += RemoveProvider_Click;
            Grid.SetColumn(deleteBtn, 1);
            headerPanel.Children.Add(deleteBtn);

            var expander = new Expander
            {
                Header = headerPanel,
                Tag = name,
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

            // ===== 模型列表 =====
            var modelsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                MinHeight = 60, MaxHeight = 160,
                FontSize = 13, Margin = new Thickness(0, 0, 0, 6),
                SelectionMode = DataGridSelectionMode.Single,
                Tag = $"{name}:Models"
            };

            var modelsHeader = new Grid { Margin = new Thickness(0, 10, 0, 2) };
            modelsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            modelsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            modelsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            modelsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            modelsHeader.Children.Add(new TextBlock
            {
                Text = "模型列表", FontSize = 13, FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            var fetchBtn = new Button
            {
                Content = "从 API 获取", Width = 80, Height = 24, FontSize = 11,
                Tag = name, Margin = new Thickness(0, 0, 4, 0)
            };
            fetchBtn.Click += FetchModels_Click;
            Grid.SetColumn(fetchBtn, 1);
            modelsHeader.Children.Add(fetchBtn);

            var addBtn = new Button
            {
                Content = "+", Width = 26, Height = 24, FontSize = 13, FontWeight = FontWeights.Bold,
                Tag = name, Margin = new Thickness(0, 0, 4, 0)
            };
            addBtn.Click += (s, e) => AddModel(name, modelsGrid);
            Grid.SetColumn(addBtn, 2);
            modelsHeader.Children.Add(addBtn);

            var removeBtn = new Button
            {
                Content = "-", Width = 26, Height = 24, FontSize = 13, FontWeight = FontWeights.Bold,
                Tag = name
            };
            removeBtn.Click += (s, e) => RemoveModel(modelsGrid);
            Grid.SetColumn(removeBtn, 3);
            modelsHeader.Children.Add(removeBtn);

            panel.Children.Add(modelsHeader);
            modelsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "模型名称",
                Binding = new System.Windows.Data.Binding("Name") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            modelsGrid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "视觉模型",
                Binding = new System.Windows.Data.Binding("SupportsVision") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged },
                Width = 70
            });

            // 加载已有模型数据
            var modelVmList = new System.Collections.ObjectModel.ObservableCollection<ModelViewModel>(
                (config.Models ?? new List<ModelConfig>()).Select(m => new ModelViewModel
                {
                    Name = m.Name,
                    SupportsVision = m.SupportsVision
                }));
            modelsGrid.ItemsSource = modelVmList;

            panel.Children.Add(modelsGrid);

            expander.Content = panel;
            return expander;
        }

        /// <summary>从 UI 收集 Provider 配置</summary>
        public Dictionary<string, ProviderConfig> Collect()
        {
            var result = new Dictionary<string, ProviderConfig>();
            foreach (var child in _providersPanel.Children)
            {
                if (child is not Expander expander || expander.Content is not StackPanel panel)
                    continue;

                var providerName = expander.Tag?.ToString() ?? "";
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
                    else if (field == "Models" && ctrl is DataGrid dg && dg.ItemsSource is System.Collections.ObjectModel.ObservableCollection<ModelViewModel> models)
                    {
                        pc.Models = models.Select(vm => new ModelConfig
                        {
                            Name = vm.Name,
                            SupportsVision = vm.SupportsVision
                        }).ToList();
                    }
                }
                result[providerName] = pc;
            }
            return result;
        }

        private void AddModel(string providerName, DataGrid modelsGrid)
        {
            if (modelsGrid.ItemsSource is not System.Collections.ObjectModel.ObservableCollection<ModelViewModel> models)
                return;

            var vm = new ModelViewModel { Name = "new-model", SupportsVision = false };
            models.Add(vm);
            modelsGrid.SelectedItem = vm;
            modelsGrid.ScrollIntoView(vm);
        }

        private void RemoveModel(DataGrid modelsGrid)
        {
            if (modelsGrid.SelectedItem is ModelViewModel selected &&
                modelsGrid.ItemsSource is System.Collections.ObjectModel.ObservableCollection<ModelViewModel> models)
            {
                models.Remove(selected);
            }
        }

        private void AddProvider_Click(object sender, RoutedEventArgs e)
        {
            // 找到不冲突的默认名称
            var existing = new HashSet<string>();
            foreach (var child in _providersPanel.Children)
            {
                if (child is Expander exp && exp.Tag is string tag)
                    existing.Add(tag);
            }
            var baseName = "NewProvider";
            var name = baseName;
            var i = 1;
            while (existing.Contains(name)) name = $"{baseName}{i++}";

            // 创建新的 Expander（复用 Load 的结构）
            var config = new ProviderConfig();
            var expander = CreateProviderSection(name, config);
            expander.IsExpanded = true;
            _providersPanel.Children.Add(expander);
        }

        private void RemoveProvider_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var providerName = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(providerName)) return;

            var result = MessageBox.Show(
                $"确定要删除提供商 \"{providerName}\" 吗？此操作不可撤销。",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            // 从 providersPanel 中移除对应 Expander
            for (int i = _providersPanel.Children.Count - 1; i >= 0; i--)
            {
                if (_providersPanel.Children[i] is Expander exp && exp.Tag?.ToString() == providerName)
                {
                    _providersPanel.Children.RemoveAt(i);
                    break;
                }
            }
        }

        private async void FetchModels_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var providerName = btn.Tag?.ToString();
            if (string.IsNullOrEmpty(providerName)) return;

            // 找到对应的 Models Grid
            DataGrid? modelsGrid = null;
            if (btn.Parent is Grid grid && grid.Parent is StackPanel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is DataGrid dg && dg.Tag?.ToString() == $"{providerName}:Models")
                    {
                        modelsGrid = dg;
                        break;
                    }
                }
            }
            if (modelsGrid == null) return;

            btn.IsEnabled = false;
            btn.Content = "获取中...";
            try
            {
                var fetchService = new ModelFetchService(_configReader);
                var fetchedModels = await fetchService.FetchModelsAsync(providerName);

                if (fetchedModels.Count == 0)
                {
                    MessageBox.Show("该提供商未返回任何模型", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (modelsGrid.ItemsSource is System.Collections.ObjectModel.ObservableCollection<ModelViewModel> models)
                {
                    var existingNames = new HashSet<string>(models.Select(m => m.Name));
                    var newModels = fetchedModels.Where(n => !existingNames.Contains(n))
                        .Select(n => new ModelViewModel { Name = n, SupportsVision = false });
                    foreach (var m in newModels)
                        models.Add(m);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取模型列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = "从 API 获取";
            }
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

    /// <summary>模型列表 ViewModel（支持 DataGrid 双向绑定）</summary>
    public class ModelViewModel
    {
        public string Name { get; set; } = string.Empty;
        public bool SupportsVision { get; set; } = false;
    }
}
