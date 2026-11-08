using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace xLozerOptimizer.Models
{
    public class OptimizeItem : INotifyPropertyChanged
    {
        private bool _shouldOptimize;

        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // "Processo" ou "Serviço"
        public string MemoryUsageText { get; set; } = string.Empty;
        public long MemoryBytes { get; set; }
        public bool IsService { get; set; }
        public string Description { get; set; } = string.Empty;

        public bool ShouldOptimize
        {
            get => _shouldOptimize;
            set
            {
                if (_shouldOptimize != value)
                {
                    _shouldOptimize = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}