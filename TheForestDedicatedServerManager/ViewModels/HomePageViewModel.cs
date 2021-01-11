﻿using AppConfiguration;
using Prism.Commands;
using Prism.Events;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Windows.Media;
using TheForestDedicatedServerManager.Base;
using TheForestDedicatedServerManager.Events;

namespace TheForestDedicatedServerManager.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        #region Fields and properties
        // Private only fields
        private string mServerProcessName;
        // Fields with public properties
        private string mServerOutputText;
        private string mShutdownTime;
        private bool mIsMachineShutdown;
        private Brush mServerStatusColour;
        // Commands
        private DelegateCommand mStartServerCommand;
        private DelegateCommand mShutdownServerCommand;
        private DelegateCommand mScheduleShutdownCommand;
        private DelegateCommand mCancelShutdownCommand;
        private DelegateCommand mQuitCommand;

        // Public properties
        public string ServerOutputText
        {
            get => mServerOutputText;
            set => SetProperty(ref mServerOutputText, value);
        }
        public string ShutdownTime
        {
            get => mShutdownTime;
            set
            {
                SetProperty(ref mShutdownTime, value);
                ScheduleShutdownCommand.RaiseCanExecuteChanged();
                CancelShutdownCommand.RaiseCanExecuteChanged();
            }
        }
        public bool IsMachineShutdown
        {
            get => mIsMachineShutdown;
            set
            {
                SetProperty(ref mIsMachineShutdown, value);
                AppConfig config = AppConfigurationManager.GetSettings();
                config.IsMachineShutdownScheduled = value;
                AppConfigurationManager.Save();
            }
        }
        public Brush ServerStatusColour
        {
            get => mServerStatusColour;
            set => SetProperty(ref mServerStatusColour, value);
        }
        // Commands
        public DelegateCommand StartServerCommand
        {
            get => mStartServerCommand ?? (mStartServerCommand = new DelegateCommand(StartServerExecute, StartServerCanExecute));
        }
        public DelegateCommand ShutdownServerCommand
        {
            get => mShutdownServerCommand ?? (mShutdownServerCommand = new DelegateCommand(ShutdownServerExecute, ShutdownServerCanExecute));
        }
        public DelegateCommand ScheduleShutdownCommand
        {
            get => mScheduleShutdownCommand ?? (mScheduleShutdownCommand = new DelegateCommand(ScheduleShutdownExecute, ScheduleShutdownCanExecute));
        }
        public DelegateCommand CancelShutdownCommand
        {
            get => mCancelShutdownCommand ?? (mCancelShutdownCommand = new DelegateCommand(CancelShutdownExecute, CancelShutdownCanExecute));
        }
        public DelegateCommand QuitCommand
        {
            get => mQuitCommand ?? (mQuitCommand = new DelegateCommand(QuitExecute, QuitCanExecute));
            private set => mQuitCommand = value;
        }
        #endregion

        #region Events
        public event EventHandler ServerStatusChange;
        protected virtual void RaiseServerStatusChange()
        {
            ServerStatusChange?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Constructors
        /// <inheritdoc cref="DataErrorBindableBase(Dictionary&lt;string, Func&lt;object, string&lt;&lt; _validators)"/>
        public HomePageViewModel(IEventAggregator eventAggregator) : this(eventAggregator, null)
        {
        }

        /// <inheritdoc cref="DataErrorBindableBase(Dictionary&lt;string, Func&lt;object, string&lt;&lt; _validators)"/>
        /// <param name="eventAggregator"><inheritdoc cref="ViewModelBase()"/></param>
        /// <param name="validators"><inheritdoc cref="DataErrorBindableBase(Dictionary&lt;string, Func&lt;object, string&lt;&lt; _validators)"/></param>
        public HomePageViewModel(IEventAggregator eventAggregator, Dictionary<string, Func<object, string>> validators) : base(eventAggregator, validators)
        {
            AppConfig config = AppConfigurationManager.GetSettings();
            mServerProcessName = config.ServerProcessName;
            ServerOutputText = "";
            ShutdownTime = null;
            AddValidator(nameof(ShutdownTime), ValidateShutdownTime);

            ServerStatusChange += HomePageViewModel_OnServerStatusChange;

            StartServerCommand.RaiseCanExecuteChanged();
            ShutdownServerCommand.RaiseCanExecuteChanged();
            ScheduleShutdownCommand.RaiseCanExecuteChanged();
            CancelShutdownCommand.RaiseCanExecuteChanged();
            RaiseServerStatusChange();
        }
        #endregion

        #region Command methods
        private void StartServerExecute()
        {
            AppConfig appConfig = AppConfigurationManager.GetSettings();
            Process process = new Process()
            {
                StartInfo = new ProcessStartInfo(appConfig.TheForestServerManagerExecutablePath, appConfig.ServerArguments)
                {
                    Verb = "runas"
                }
            };

            try
            {
                // Start the dedicated server
                if (process.Start())
                {
                    AppendServerOutputText("Dedicated server has been started.");
                    ShutdownServerCommand.RaiseCanExecuteChanged();
                    StartServerCommand.RaiseCanExecuteChanged();
                    ScheduleShutdownCommand.RaiseCanExecuteChanged();
                    CancelShutdownCommand.RaiseCanExecuteChanged();
                    RaiseServerStatusChange();
                }
                else
                {
                    AppendServerOutputText("Dedicated server did not start successfully.");
                }
            }
            catch (Win32Exception)
            {
                AppendServerOutputText("Server could not be started because administrative privileges were not granted.");
            }
        }
        private bool StartServerCanExecute()
        {
            if (!CheckServerStatus())
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private void ShutdownServerExecute()
        {
            Process[] processes = Process.GetProcessesByName(mServerProcessName);
            if (processes.Length == 1)
            {
                processes[0].Kill();
                Thread.Sleep(100);
                if (processes[0].HasExited)
                {
                    AppendServerOutputText("Dedicated server has been shutdown.");
                    RaiseServerStatusChange();
                }
                else
                {
                    AppendServerOutputText("Dedicated server failed to shutdown.");
                }
            }
            else if (processes.Length == 0)
            {
                AppendServerOutputText("Dedicated server is not running.");
            }
            else if (processes.Length > 1)
            {
                throw new Exception("Multiple processes with the same name found.");
            }

            ShutdownServerCommand.RaiseCanExecuteChanged();
            StartServerCommand.RaiseCanExecuteChanged();
            ScheduleShutdownCommand.RaiseCanExecuteChanged();
            CancelShutdownCommand.RaiseCanExecuteChanged();
        }
        private bool ShutdownServerCanExecute()
        {
            return CheckServerStatus();
        }

        private void ScheduleShutdownExecute()
        {
            AppConfig config = AppConfigurationManager.GetSettings();
            ServiceController controller = new ServiceController(config.ServiceName);
            if (controller.Status != ServiceControllerStatus.Running)
            {
                // Save the shutdown time to the shared file
                config.ShutdownTime = DateTime.Parse(ShutdownTime);
                AppConfigurationManager.Save();

                // Start the service
                controller.Start();
                Thread.Sleep(100);
                CancelShutdownCommand.RaiseCanExecuteChanged();
                if (config.IsMachineShutdownScheduled)
                {
                    AppendServerOutputText($"Shutdown scheduled for {config.ShutdownTime}. A machine shutdown is also scheduled.");
                }
                else
                {
                    AppendServerOutputText($"Shutdown scheduled for {config.ShutdownTime}.");
                }
            }
            else
            {
                string errorMsg = "Application attemped to schedule a shutdown when the shutdown scheduler service was already started.";
                AppendServerOutputText(errorMsg);
            }
        }
        private bool ScheduleShutdownCanExecute()
        {
            return CheckServerStatus() && ValidateShutdownTime(ShutdownTime) == "";
        }

        private void CancelShutdownExecute()
        {
            AppConfig config = AppConfigurationManager.GetSettings();
            ServiceController controller = new ServiceController(config.ServiceName);
            if (controller.Status == ServiceControllerStatus.Running)
            {
                controller.Stop();
                Thread.Sleep(100);
                AppendServerOutputText("Scheduled shutdown has been cancelled.");
                ShutdownServerCommand.RaiseCanExecuteChanged();
                StartServerCommand.RaiseCanExecuteChanged();
                ScheduleShutdownCommand.RaiseCanExecuteChanged();
                CancelShutdownCommand.RaiseCanExecuteChanged();
            }
        }
        private bool CancelShutdownCanExecute()
        {
            AppConfig config = AppConfigurationManager.GetSettings();
            ServiceController controller = new ServiceController(config.ServiceName);
            return controller.Status == ServiceControllerStatus.Running;
        }

        private void QuitExecute()
        {
            EventAggregator.GetEvent<QuitEvent>().Publish();
        }
        private bool QuitCanExecute()
        {
            return true;
        }
        #endregion

        #region Validators
        private string ValidateShutdownTime(object arg)
        {
            string errorMessage = "";
            if (arg is string shutdownTime && !string.IsNullOrWhiteSpace(shutdownTime))
            {
                if (!DateTime.TryParse(shutdownTime, out DateTime tmpShutdownTime))
                {
                    errorMessage = "Shutdown time is not valid.";
                }
                else if (tmpShutdownTime < DateTime.Now)
                {
                    errorMessage = "Shutdown time cannot be in the past.";
                }
            }
            else
            {
                errorMessage = "Shutdown time cannot be empty.";
            }
            return errorMessage;
        }
        #endregion

        #region Event handlers
        private void HomePageViewModel_OnServerStatusChange(object sender, EventArgs e)
        {
            // Check if the server is running
            if (CheckServerStatus())
            {
                ServerStatusColour = new SolidColorBrush(Colors.Lime);
            }
            else
            {
                ServerStatusColour = new SolidColorBrush(Colors.Red);
            }
        }
        #endregion

        #region Helper methods
        private bool CheckServerStatus()
        {
            // Throw an exception if process name field is blank
            if (string.IsNullOrWhiteSpace(mServerProcessName))
            {
                throw new Exception($"{nameof(mServerProcessName)} cannot be null, empty, or only whitespace.");
            }
            else
            {
                // Return true if process is found, and false if it wasn't
                Process[] processes = Process.GetProcessesByName(mServerProcessName);
                if (processes.Length == 1)
                {
                    return true;
                }
                else if (processes.Length < 1)
                {
                    return false;
                }
                else if (processes.Length > 1)
                {
                    throw new Exception($"Multiple processes with the name '{mServerProcessName}' were found.");
                }
                else
                {
                    throw new Exception($"Error occurred while resolving process '{mServerProcessName}'.");
                }
            }
        }
        private void AppendServerOutputText(string text)
        {
            ServerOutputText += $"{text}\n";
        }
        #endregion
    }
}
