using Rustun.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rustun.Services
{
    internal class LogService : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static LogService _instance = new LogService();
        public static LogService Instance => _instance;

        private LogService()
        {
            LogEntries = new ObservableCollection<LogEntry>();
        }

        public ObservableCollection<LogEntry> LogEntries { get; }

        public void AddLog(string message, string level = "INFO")
        {
            var logEntry = new LogEntry
            {
                Message = message,
                Timestamp = DateTime.Now,
                Level = level
            };

            // 确保在UI线程上更新
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                LogEntries.Add(logEntry);

                // 限制日志数量，避免内存泄漏
                if (LogEntries.Count > 1000)
                {
                    LogEntries.RemoveAt(0);
                }

                OnPropertyChanged(nameof(LogEntries));
            });
        }

        public void ClearLogs()
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                LogEntries.Clear();
                OnPropertyChanged(nameof(LogEntries));
            });
        }
    }
}
