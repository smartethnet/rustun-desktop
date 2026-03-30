using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Rustun.Services
{
    internal class ProcessService
    {
        private Process? _clientProcess;
        private bool _isRunning;

        // 事件定义
        public event EventHandler<ProcessEventArgs>? OutputReceived;
        public event EventHandler<ProcessEventArgs>? ProcessExited;

        public bool IsProcessRunning ()
        {
            return _isRunning && _clientProcess != null && !_clientProcess.HasExited;
        }

        // 单例模式
        private static ProcessService _instance = new ProcessService();
        public static ProcessService Instance => _instance;

        private ProcessService() { }

        public async Task<bool> StartClientProcess(string exePath, string arguments = "")
        {
            try
            {
                if (_isRunning && _clientProcess != null && !_clientProcess.HasExited)
                {
                    StopClientProcess();
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(exePath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = System.Text.Encoding.UTF8,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                };

                _clientProcess = new Process { StartInfo = startInfo };
                _clientProcess.EnableRaisingEvents = true;

                // 设置输出接收事件
                _clientProcess.OutputDataReceived += OnOutputDataReceived;
                _clientProcess.ErrorDataReceived += OnErrorDataReceived;
                _clientProcess.Exited += OnProcessExited;

                if (_clientProcess.Start())
                {
                    _isRunning = true;

                    _clientProcess.BeginOutputReadLine();
                    _clientProcess.BeginErrorReadLine();

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                dispatcherQueue?.TryEnqueue(() =>
                {
                    OutputReceived?.Invoke(this, new ProcessEventArgs
                    {
                        Message = ex.Message,
                        ExitCode = -1
                    });
                });
                return false;
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                OutputReceived?.Invoke(this, new ProcessEventArgs
                {
                    Message = e.Data
                });
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                dispatcherQueue?.TryEnqueue(() =>
                {
                    OutputReceived?.Invoke(this, new ProcessEventArgs
                    {
                        Message = $"[ERROR] {e.Data}"
                    });
                });
            }
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (sender is Process process)
            {
                _isRunning = false;
                var exitCode = process?.ExitCode ?? -1;

                var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
                dispatcherQueue?.TryEnqueue(() =>
                {
                    ProcessExited?.Invoke(this, new ProcessEventArgs
                    {
                        Message = $"进程已退出，退出代码: {exitCode}",
                        HasExited = true,
                        ExitCode = exitCode
                    });
                });
            }

            // 清理资源
            _clientProcess?.Dispose();
            _clientProcess = null;
        }

        public void StopClientProcess()
        {
            if (_isRunning && _clientProcess != null && !_clientProcess.HasExited)
            {
                try
                {
                    _clientProcess.Kill();
                }
                catch (Exception)
                {
                    // 忽略清理异常
                }
            }

            _isRunning = false;
        }
    }
}
