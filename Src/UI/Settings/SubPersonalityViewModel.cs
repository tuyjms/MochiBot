using System.ComponentModel;

namespace MochiBot.Src.UI.Settings
{
    /// <summary>
    /// 子人格视图模型，支持 DataGrid 双向绑定
    /// </summary>
    public class SubPersonalityViewModel : INotifyPropertyChanged
    {
        private string _name = "";
        private string _description = "";
        private int _weight;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        public int Weight
        {
            get => _weight;
            set { _weight = value; OnPropertyChanged(nameof(Weight)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
