using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using xLozerOptimizer.Core;
using xLozerOptimizer.Models;

namespace xLozerOptimizer
{
    public partial class MainWindow : Window
    {
        private readonly SystemScanner _scanner;
        private readonly OptimizerEngine _engine;
        private readonly HardwareMonitor _monitor;
        private CancellationTokenSource? _cancelToken;

        public ObservableCollection<OptimizeItem> ProcessList { get; set; }
        public ObservableCollection<string> IgnoredList { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            _scanner = new SystemScanner();
            _engine = new OptimizerEngine();
            _monitor = new HardwareMonitor();
            ProcessList = new ObservableCollection<OptimizeItem>();
            IgnoredList = new ObservableCollection<string>();

            DataContext = this;

            // Restaura o estado salvo do Auto-Clean
            ChkAutoClean.IsChecked = ConfigManager.Current.AutoCleanEnabled;
            _monitor.AutoCleanEnabled = ConfigManager.Current.AutoCleanEnabled;

            PopulateHardwareCards();
            RefreshIgnoredList();
            LoadProcesses();
            StartTelemetry();
        }

        private void PopulateHardwareCards()
        {
            TxtUserName.Text = _monitor.UserName;
            TxtCpuModel.Text = _monitor.CpuName;
            TxtCpuCores.Text = _monitor.CpuCoresText;

            TxtGpuModel.Text = _monitor.GpuName;
            TxtGpuType.Text = _monitor.GpuTypeText;

            TxtRamTotal.Text = _monitor.TotalRamText;
            TxtRamType.Text = _monitor.RamTypeText;

            TxtOsName.Text = _monitor.OsName;
            TxtOsVersion.Text = _monitor.OsVersionText;

            TxtDiskModel.Text = _monitor.DiskModel;
            TxtDiskSpace.Text = _monitor.DiskSpaceText;

            bool isAdmin = MemoryManager.IsRunningAsAdmin();
            if (!isAdmin)
            {
                BadgeAdmin.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D2111"));
                BadgeAdmin.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
                TxtAdminStatus.Text = "⚠️ Modo Padrão (Abra como Admin)";
                TxtAdminStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
            }
        }

        private void StartTelemetry()
        {
            _cancelToken = new CancellationTokenSource();
            _ = _monitor.StartMonitoringAsync(snapshot =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtCpuLivePercent.Text = $"{snapshot.CpuUsagePercent:F1}%";
                    BarCpu.Value = snapshot.CpuUsagePercent;

                    TxtRamAvailable.Text = $"Livres: {snapshot.RamAvailableGb:F2} GB";
                    TxtRamLivePercent.Text = $"{snapshot.RamUsagePercent:F0}%";
                    BarRam.Value = snapshot.RamUsagePercent;

                    if (snapshot.AutoCleanedNow)
                    {
                        TxtStatusBanner.Text = $"[Auto-Clean] Cache limpo automaticamente! +{snapshot.AutoFreedMb:F0} MB liberados.";
                        TxtBoosterFooter.Text = $"Auto-Clean liberou {snapshot.AutoFreedMb:F0} MB agora mesmo.";
                    }
                });
            }, _cancelToken.Token);
        }

        private void RefreshIgnoredList()
        {
            IgnoredList.Clear();
            foreach (var proc in ConfigManager.Current.IgnoredProcesses.OrderBy(x => x))
            {
                IgnoredList.Add(proc);
            }
        }

        private void LoadProcesses()
        {
            foreach (var oldItem in ProcessList)
                oldItem.PropertyChanged -= Item_PropertyChanged;

            ProcessList.Clear();
            var scannedItems = _scanner.ScanSystem();

            foreach (var item in scannedItems)
            {
                item.PropertyChanged += Item_PropertyChanged;
                ProcessList.Add(item);
            }

            UpdateBoosterCounters();
        }

        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OptimizeItem.ShouldOptimize) && sender is OptimizeItem changedItem)
            {
                // Salva imediatamente no JSON a escolha do usuário para este aplicativo/serviço!
                ConfigManager.SaveToggleState(changedItem.Name, changedItem.ShouldOptimize);
                UpdateBoosterCounters();
            }
        }

        private void UpdateBoosterCounters()
        {
            int total = ProcessList.Count;
            int selected = ProcessList.Count(x => x.ShouldOptimize);
            long selectedBytes = ProcessList.Where(x => x.ShouldOptimize && !x.IsService).Sum(x => x.MemoryBytes);
            double selectedMb = selectedBytes / (1024.0 * 1024.0);

            TxtTotalDetected.Text = $"{total} Alvos em 2º Plano";
            TxtSelectedCount.Text = $"{selected} Marcados (~{selectedMb:F0} MB)";
            TxtBoosterFooter.Text = $"{selected} itens selecionados (~{selectedMb:F0} MB) • Suas escolhas ficam salvas automaticamente.";
        }

        // --- GERENCIAMENTO DE WHITELIST (IGNORAR PROCESSOS) ---
        private void BtnIgnoreItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string procName && !string.IsNullOrWhiteSpace(procName))
            {
                ConfigManager.AddIgnoredProcess(procName);
                RefreshIgnoredList();
                LoadProcesses();
                TxtBoosterFooter.Text = $"'{procName}' foi adicionado à Whitelist e nunca mais será encerrado.";
            }
        }

        private void BtnAddCustomIgnore_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtCustomIgnore.Text.Trim();
            if (!string.IsNullOrWhiteSpace(input))
            {
                ConfigManager.AddIgnoredProcess(input);
                TxtCustomIgnore.Clear();
                RefreshIgnoredList();
                LoadProcesses();
            }
        }

        private void BtnRemoveIgnore_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string procName && !string.IsNullOrWhiteSpace(procName))
            {
                ConfigManager.RemoveIgnoredProcess(procName);
                RefreshIgnoredList();
                LoadProcesses();
            }
        }

        // --- NAVEGAÇÃO ENTRE ABAS ---
        private void SetActiveNavButton(Button activeBtn)
        {
            Button[] navButtons = { NavDashboard, NavBooster, NavWhitelist };
            foreach (var btn in navButtons)
            {
                if (btn == activeBtn)
                {
                    btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#161B22"));
                    btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));
                }
                else
                {
                    btn.Background = Brushes.Transparent;
                    btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E"));
                }
            }
        }

        private void NavDashboard_Click(object sender, RoutedEventArgs e)
        {
            ViewDashboard.Visibility = Visibility.Visible;
            ViewBooster.Visibility = Visibility.Collapsed;
            ViewWhitelist.Visibility = Visibility.Collapsed;
            SetActiveNavButton(NavDashboard);
        }

        private void NavBooster_Click(object sender, RoutedEventArgs e)
        {
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewBooster.Visibility = Visibility.Visible;
            ViewWhitelist.Visibility = Visibility.Collapsed;
            SetActiveNavButton(NavBooster);
        }

        private void NavWhitelist_Click(object sender, RoutedEventArgs e)
        {
            ViewDashboard.Visibility = Visibility.Collapsed;
            ViewBooster.Visibility = Visibility.Collapsed;
            ViewWhitelist.Visibility = Visibility.Visible;
            SetActiveNavButton(NavWhitelist);
        }

        private void BtnRescan_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void ChkAutoClean_Changed(object sender, RoutedEventArgs e)
        {
            if (_monitor != null && ChkAutoClean != null)
            {
                bool isEnabled = ChkAutoClean.IsChecked == true;
                _monitor.AutoCleanEnabled = isEnabled;
                ConfigManager.Current.AutoCleanEnabled = isEnabled;
                ConfigManager.Save();
            }
        }

        // --- BOTÕES DE AÇÃO ---
        private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
        {
            BtnOptimize.IsEnabled = false;
            BtnOptimize.Content = "⏳ Otimizando...";

            var items = new List<OptimizeItem>(ProcessList);
            var report = await _engine.ApplyOptimizationsAsync(items);

            BtnOptimize.Content = "⚡ Aplicar Otimização (Live Mode)";
            BtnOptimize.IsEnabled = true;

            TxtStatusBanner.Text = $"Última otimização: {report.ProcessesKilled} apps fechados, {report.ServicesStopped} serviços pausados, +{report.RamFreedMb:F0} MB livres.";

            MessageBox.Show(
                $"Otimização de Live Concluída!\n\n" +
                $"• Aplicativos encerrados: {report.ProcessesKilled}\n" +
                $"• Serviços pausados: {report.ServicesStopped}\n" +
                $"• Memória RAM liberada: {report.RamFreedMb:F0} MB\n" +
                (report.IsAdmin ? "• Privilégio: Administrador (Standby List limpa)" : "• Aviso: Execute como Administrador para pausar serviços do Windows."),
                "xLozer Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);

            LoadProcesses();
        }

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            int restoredCount = await _engine.RestoreSystemAsync();
            LoadProcesses();

            MessageBox.Show(
                $"Restauração concluída!\n{restoredCount} serviço(s) do Windows foram reiniciados.",
                "xLozer Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClearRam_Click(object sender, RoutedEventArgs e)
        {
            var result = MemoryManager.ClearStandbyList();
            TxtStatusBanner.Text = result.Message;
            TxtBoosterFooter.Text = result.Message;

            MessageBox.Show(
                $"{result.Message}\n\n" +
                $"• Disponível Antes: {result.BeforeAvailableMb / 1024.0:F2} GB\n" +
                $"• Disponível Agora: {result.AfterAvailableMb / 1024.0:F2} GB",
                "xLozer RAM Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // --- CONTROLES DA JANELA ---
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _cancelToken?.Cancel();
            base.OnClosed(e);
        }
    }
}