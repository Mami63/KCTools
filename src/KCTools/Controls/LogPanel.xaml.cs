using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using KCTools.Models;
using KCTools.Services;

namespace KCTools.Controls
{
    /// <summary>
    /// Alt konsol paneli: LogHub koleksiyonunu filtreli ve otomatik kaydırmalı gösterir.
    /// Kendi DataContext'i LogHub.Instance'dır.
    /// </summary>
    public partial class LogPanel : UserControl
    {
        private enum LogFilter
        {
            All,
            Info,
            Warn,
            Error
        }

        private readonly ICollectionView _view;
        private LogFilter _filter = LogFilter.All;

        public LogPanel()
        {
            InitializeComponent();

            DataContext = LogHub.Instance;

            _view = CollectionViewSource.GetDefaultView(LogHub.Instance.Entries);
            _view.Filter = FilterEntry;
            LogList.ItemsSource = _view;

            LogHub.Instance.EntryAdded += OnEntryAdded;
            LogHub.Instance.Cleared += OnLogCleared;

            Loaded += (_, __) => ScrollToEnd();
            Unloaded += OnUnloaded;
        }

        private bool FilterEntry(object item)
        {
            if (!(item is LogEntry entry))
            {
                return true;
            }

            switch (_filter)
            {
                case LogFilter.Info:
                    return entry.Severity == LogSeverity.Info
                           || entry.Severity == LogSeverity.Step
                           || entry.Severity == LogSeverity.Success
                           || entry.Severity == LogSeverity.Command;

                case LogFilter.Warn:
                    return entry.Severity == LogSeverity.Warning;

                case LogFilter.Error:
                    return entry.Severity == LogSeverity.Error;

                default:
                    return true;
            }
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton radioButton) || !(radioButton.Tag is string tag))
            {
                return;
            }

            if (!Enum.TryParse(tag, out LogFilter filter))
            {
                return;
            }

            _filter = filter;

            // İlk yüklemede IsChecked="True" bu olayı InitializeComponent sırasında
            // tetikler; görünüm henüz hazır olmadığı için korumalı ilerliyoruz.
            if (_view == null)
            {
                return;
            }

            _view.Refresh();
            ScrollToEnd();
        }

        private void OnEntryAdded(object sender, LogEntry entry)
        {
            if (!LogHub.Instance.AutoScroll)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ScrollToEnd), DispatcherPriority.Background);
        }

        private void OnLogCleared(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(ScrollToEnd), DispatcherPriority.Background);
        }

        private void ScrollToEnd()
        {
            if (LogList == null || LogList.Items.Count == 0)
            {
                return;
            }

            try
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
            catch (Exception)
            {
                // Sanallaştırma sırasında kısa süreli istisnalar yutulur.
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            LogHub.Instance.EntryAdded -= OnEntryAdded;
            LogHub.Instance.Cleared -= OnLogCleared;
            Unloaded -= OnUnloaded;
        }
    }
}
