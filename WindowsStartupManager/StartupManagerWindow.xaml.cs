﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Management;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SharedClasses;
using Notifs = SharedClasses.ShowNoCallbackNotificationInterop;

#if PlatformX86
WindowsStartupManager needs to access external processes, including 64bit ones, so it cannot run in x86 mode, only in 'Any CPU' mode
#endif

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class StartupManagerWindow : Window
	{
		public const string cThisAppName = "WindowsStartupManager";
		//private readonly TimeSpan DelaySecondsBetweenApps = TimeSpan.FromSeconds(2);//TimeSpan.FromMilliseconds(500);
		private string originalPauseButtonContent;
		ObservableCollection<ApplicationDetails> Applications = new ObservableCollection<ApplicationDetails>();
		private bool silentWaitUntilMorningMode = false;

		public StartupManagerWindow()
		{
			InitializeComponent();
			originalPauseButtonContent = buttonPauseStarting.Content.ToString();
			wmicpus = new WqlObjectQuery("SELECT * FROM Win32_Processor");
			cpus = new ManagementObjectSearcher(wmicpus);
		}

		//Timer startAppsTimer;
		System.Windows.Forms.Timer startAppsTimer;
		System.Windows.Forms.Timer timerToPopulateList;
		//Timer timerToLogCpuUsage;
		bool listAlreadyPopulatedAtLeastOnce = false;
		//float cCpuUsageTolerancePercentage = 30;
		ObjectQuery wmicpus;
		ManagementObjectSearcher cpus;
		DateTime? _systemStartupTime = null;
		DateTime systemStartupTime
		{
			get
			{
				//return new DateTime(2013, 03, 19, 07, 32, 30);
				if (!_systemStartupTime.HasValue)
				{
					DateTime tmpStartup;
					TimeSpan idleTime;
					if (Win32Api.GetLastInputInfo(out tmpStartup, out idleTime))
						_systemStartupTime = tmpStartup;
					else
					{
						_systemStartupTime = DateTime.Now;
						UserMessages.ShowWarningMessage("Unable to determine System Startup Time, using DateTime.Now");
					}
				}
				return _systemStartupTime.Value;
			}
		}
		//bool skipLoggingCpuUsage = false;
		int cMinimumCPUrunningSeconds = 30;
		bool alreadySetTimerTo5secs = false;
		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			this.MaxHeight = System.Windows.SystemParameters.WorkArea.Height - 300;

			//labelDelayBetweenApps.Content = "Delay after each app = "
			//    + DelaySecondsBetweenApps.TotalMilliseconds + "ms";

			if (IsSystemRunningMinimumDuration())
				labelStatus.Content = "CPU already running more than "
					+ cMinimumCPUrunningSeconds + " seconds, applications to be started soon";
			else
				labelStatus.Content = "Waiting for CPU to be running more than "
					+ cMinimumCPUrunningSeconds + " seconds (currently " + GetCPUrunningDuration().TotalSeconds + " seconds)";

			//Timer to populate the list with applications
			timerToPopulateList =
				new System.Windows.Forms.Timer();
			timerToPopulateList.Interval = 200;
			timerToPopulateList.Tick +=
				delegate
				{
					PopulateApplicationsList();
				};
			timerToPopulateList.Start();

			//Timer to check if must start apps keep this window on top
			startAppsTimer = new System.Windows.Forms.Timer();
			startAppsTimer.Interval = (int)TimeSpan.FromMilliseconds(100).TotalMilliseconds;//We set this later to 5 secs
			startAppsTimer.Tick +=
				delegate
				{
					ThreadingInterop.DoAction(
						delegate
						{
							if (!alreadySetTimerTo5secs)
							{
								alreadySetTimerTo5secs = true;
								this.Dispatcher.Invoke((Action)delegate
								{
									startAppsTimer.Interval = (int)TimeSpan.FromSeconds(5).TotalMilliseconds;
								});
							}

							//Dispatcher.Invoke((Action)delegate { OwnBringIntoView(); });
							//if (!listAlreadyPopulatedAtLeastOnce)
							//    Dispatcher.Invoke((Action)delegate
							//    {
							//        PopulateApplicationsList();
							//    });

							if (IsSystemRunningMinimumDuration())//Check system already running a while
							{
								if (!listAlreadyPopulatedAtLeastOnce)
									PopulateApplicationsList();
								StartAllApplications();
							}

							/*if (GetMaxCpuUsage() < cCpuUsageTolerancePercentage)//Check that CPU usage is low enough
								StartAllApplications();*/
						},
						false,
						apartmentState: ApartmentState.STA);
				};
			startAppsTimer.Start();

			//timerToLogCpuUsage = new Timer(delegate
			//    {
			//        //Log for first while
			//        if (!skipLoggingCpuUsage)
			//        {
			//            string logfile = Logging.LogInfoToFile(
			//                "[" + DateTime.Now.ToString("HH:mm:ss")
			//                    + "] (system startup at " + systemStartupTime.ToString("HH:mm:ss")
			//                    + ") cpu usage = " + (int)GetMaxCpuUsage() + "%",
			//                Logging.ReportingFrequencies.Daily,
			//                cThisAppName,
			//                "CpuUsageLogs");
			//            if (DateTime.Now.Subtract(systemStartupTime).TotalMinutes > 20)
			//            {
			//                skipLoggingCpuUsage = true;
			//                //if (UserMessages.Confirm("Cpu usages was logged, open the log file?"))
			//                //    Process.Start("notepad", logfile);
			//            }
			//        }
			//    },
			//    null,
			//    TimeSpan.FromSeconds(0),
			//    TimeSpan.FromSeconds(2));
		}

		private TimeSpan GetCPUrunningDuration()
		{
			return DateTime.Now.Subtract(systemStartupTime);
		}

		private bool IsSystemRunningMinimumDuration()
		{
			return GetCPUrunningDuration().TotalSeconds > cMinimumCPUrunningSeconds;
		}

		bool isbusyStarting = false;
		private void StartAllApplications()
		{
			if (isbusyStarting || silentWaitUntilMorningMode)
				return;

			isbusyStarting = true;

			//Dispatcher.Invoke((Action)delegate
			//{
			try
			{
				//foreach (var app in Applications)
				for (int i = 0; i < Applications.Count; i++)
				{
					if (silentWaitUntilMorningMode)
						break;

					var app = Applications[i];

					if (isPaused)
					{
						app.UpdateApplicationRunningStatus(true);
						continue;
					}

					int delayInSeconds = app.DelayAfterStartSeconds;
					//if (delayInSeconds <= 0)
					//    //Why still have to do this????
					//    delayInSeconds = SettingsSimple.ApplicationManagerSettings.RunCommand.cDefaultDelayInSeconds;//delayInSeconds = 1;//Minimum 1 second??

					//Gui updates
					Dispatcher.Invoke((Action)delegate
					{
						labelStatus.Content = "Starting \"" + app.DisplayName + "\", waiting " + delayInSeconds + " seconds";
					});
					UpdateLayoutThreadsafe(labelStatus);

					bool? startupSuccess
						= app.StartNow_NotAllowMultipleInstances(false);
					//while (isPaused)
					//    WPFHelper.DoEvents();

					UpdateLayoutThreadsafe(this);
					UpdateLayoutThreadsafe(dataGrid1);

					/*if (startupSuccess == true)//Was started now (not started previously, did not fail)
						Thread.Sleep(delayInSeconds * 1000);*/
					//if (GetMaxCpuUsage() >= cCpuUsageTolerancePercentage)//If CPU usage is too high
					//    break;
				}
			}
			finally
			{
				isbusyStarting = false;

				if (silentWaitUntilMorningMode)//We hidden now, waiting for morning in order to restart the pc
				{
					//Maybe put in some way to determine the earliest we ever got into work, and the maximum duration of executing all the apps

					//Rather not clearing all Applications, still used for later in "Quick close apps"
					/*Dispatcher.Invoke((Action)delegate { Applications.Clear(); });
					Applications = null;
					GC.Collect();
					GC.WaitForPendingFinalizers();*/
				}
			}
			//});
		}

		private void UpdateLayoutThreadsafe(Control control)
		{
			control.Dispatcher.Invoke((Action)delegate
			{
				control.UpdateLayout();
			});
		}

		private float GetMaxCpuUsage()
		{
			List<float> usages = new List<float>();
			try
			{
				foreach (ManagementObject cpu in cpus.Get())
				{
					float flt;
					if (float.TryParse(cpu["LoadPercentage"].ToString(), out flt))
						usages.Add(flt);
				}
				float max = usages.Max();
				Dispatcher.Invoke((Action)delegate { cpuUsage.Content = string.Format("Last CPU usage = {0:00}% at {1}", max, DateTime.Now.ToString("HH:mm:ss")); });
				return max;
			}
			catch//Might want to look at handling these exceptions?
			{
				return 100;
			}
		}

		bool busyPopulating = false;
		private void PopulateApplicationsList(bool populateIfAlreadyPopulated = false)
		{
			//UserMessages.ShowErrorMessage("7," + busyPopulating + "," + populateIfAlreadyPopulated + "," + listAlreadyPopulatedAtLeastOnce);
			if (busyPopulating)
				return;
			if (!populateIfAlreadyPopulated && listAlreadyPopulatedAtLeastOnce)
				return;

			busyPopulating = true;
			//listAlreadyPopulatedAtLeastOnce = true;

			//Dispatcher.Invoke((Action)delegate
			//{

			//Why the heck does this not work if we call getfromregistry when the application starts 

			Applications.Clear();
			if (SettingsSimple.ApplicationManagerSettings.Instance.RunCommands != null)
			{
				var runcomms = SettingsSimple.ApplicationManagerSettings.Instance.RunCommands;
				for (int i = 0; i < runcomms.Count; i++)
				{
					if (runcomms[i].DelayAfterStartSeconds == 0)
						runcomms[i].DelayAfterStartSeconds = SettingsSimple.ApplicationManagerSettings.RunCommand.cDefaultDelayInSeconds;
					//if (!runcomms[i].IsEnabled)
					//    runcomms[i].IsEnabled = true;
				}
				SettingsSimple.ApplicationManagerSettings.Instance.RunCommands = runcomms;
				foreach (var comm in runcomms)
				{//SettingsSimple.ApplicationManagerSettings.Instance.RunCommands)
					var tmpapp = new ApplicationDetails(comm);
					tmpapp.PropertyChanged += (sn, prop) =>
					{
						var wantedProperties = new List<Expression<Func<ApplicationDetails, object>>>();
						wantedProperties.Add(ms => ms.ApplicationStatus);
						ReflectionInterop.DoForeachPropertOrField<ApplicationDetails>(
							sn as ApplicationDetails,
							wantedProperties,
							(appdetails, memberInfo, memberValue) =>
							{
								if (prop.PropertyName.Equals(memberInfo.Name, StringComparison.InvariantCultureIgnoreCase))
								{
								}
							});

					};
					Applications.Add(tmpapp);
				}
			}

			//this.Dispatcher.Invoke((Action)delegate
			this.Dispatcher.Invoke((Action)delegate
			{
				listBox1.ItemsSource = Applications;
				dataGrid1.ItemsSource = Applications;

				labelPleaseWait.Visibility = System.Windows.Visibility.Collapsed;
				this.BringIntoView();
			});

			listAlreadyPopulatedAtLeastOnce = true;
			if (timerToPopulateList != null)
			{
				timerToPopulateList.Stop();
				timerToPopulateList.Dispose();
			}
			timerToPopulateList = null;
			busyPopulating = false;
			//});
		}

		private void OwnBringIntoView()
		{
			this.Topmost = false;
			this.Topmost = true;
			this.BringIntoView();
		}

		private void PositionWindowBottomRight()
		{
			if (this.WindowState != System.Windows.WindowState.Minimized)
			{
				this.Left = System.Windows.SystemParameters.WorkArea.Right - this.ActualWidth;
				this.Top = System.Windows.SystemParameters.WorkArea.Bottom - this.ActualHeight;
			}
		}

		private void listBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			ListBox lb = sender as ListBox;
			lb.SelectedIndex = -1;
		}

		private void ApplicationButton_Click(object sender, RoutedEventArgs e)
		{
			Button but = sender as Button;
			if (but == null) return;
			ApplicationDetails appdet = but.DataContext as ApplicationDetails;
			if (appdet == null) return;
			appdet.StartNow_NotAllowMultipleInstances(true, true);
		}

		private Timer tmpTimerCheckToRestartInMorning;
		/*private void Button_Click(object sender, RoutedEventArgs e)
		{
			ExitOrHide();
		}*/

		private void ExitOrHide()
		{
			bool isWorkPc = Directory.Exists(@"C:\Programming\GLSCore6");

			if (isWorkPc)
			{
				this.Hide();
				if (startAppsTimer != null)
				{
					startAppsTimer.Dispose(); startAppsTimer = null;
				}
				menuitemQuickCloseApps.Visible = true;
			}
			else
				this.IsEnabled = false;
			silentWaitUntilMorningMode = true;

			//At this stage only for work PC
			if (isWorkPc)
			{
				tmpTimerCheckToRestartInMorning = new Timer(
					delegate
					{
						DateTime now = DateTime.Now;
						var timeofday = now.TimeOfDay;
						if (
							now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday//Only weekdays
							&& timeofday.Hours == 6 && timeofday.Minutes >= 0 && timeofday.Minutes < 15)//Between 06h00 and 06h15
						//if (timeofday.Hours == 22 && timeofday.Minutes > 30)
						{
							DateTime tmpStartup;
							TimeSpan idleTime;
							if (Win32Api.GetLastInputInfo(out tmpStartup, out idleTime)
								&& idleTime.TotalHours > 3)//We assume it is in the morning when there was no user input for more than 3 hours
							{
								int waitBeforeShutdownSeconds = 30;
								Process.Start("shutdown", "-r -f -t " + waitBeforeShutdownSeconds + " -c \"Restart in the morning via CSharp WindowsStartupManager\"");
							}
						}
					},
					null,
					TimeSpan.Zero,//TimeSpan.FromSeconds(0),
					TimeSpan.FromMinutes(1));
			}
			else
			{
				this.trayIcon.Visibility = System.Windows.Visibility.Collapsed;
				this.trayIcon.UpdateLayout();
				this.Close();//This app is intended for on windows startup only (unless its the work pc)
				//Environment.Exit(0);//Forces exit
			}
		}

		private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			PositionWindowBottomRight();
		}

		private ScaleTransform originalScale = new ScaleTransform(1, 1);
		private ScaleTransform smallScale = new ScaleTransform(0.03, 0.03);//0.05, 0.05);
		private void Window_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			//Removed the double-click scaling for now, just use middle-mouse
			//ToggleScaling();
		}

		private void ToggleScaling()
		{
			var previousSizeToContent = this.SizeToContent;
			this.SizeToContent = System.Windows.SizeToContent.WidthAndHeight;

			if (mainDockPanel.LayoutTransform != smallScale)
				mainDockPanel.LayoutTransform = smallScale;
			else
				mainDockPanel.LayoutTransform = originalScale;

			this.UpdateLayout();
			this.SizeToContent = previousSizeToContent;
		}

		private void buttonGetFromRegistry_Click(object sender, RoutedEventArgs e)
		{
			GetCommandsFromRegistry();
		}

		private void GetCommandsFromRegistry()
		{
			var commands = SettingsSimple.ApplicationManagerSettings.Instance.RunCommands;
			if (commands == null)
				commands = new List<SettingsSimple.ApplicationManagerSettings.RunCommand>();
			else
				commands = commands.Clone();

			List<string> currentFullpathsWithArgs = commands
				.Select(
					com => (com.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
					? com.AppPath
					: ApplicationDetails.GetApplicationFullPathFromOwnAppname(com.AppPath)) + (com.CommandlineArguments ?? ""))
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToList();

			string thisAppExeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

			RegistryHive[] LMandCUhives = new RegistryHive[] 
			{
				RegistryHive.LocalMachine,
				RegistryHive.CurrentUser 
			};
			foreach (var hive in LMandCUhives)
			{
				string subpathInHive = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
				using (RegistryKey regRunKey = RegistryKey.OpenBaseKey(hive, RegistryInterop.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32)
					.OpenSubKey(subpathInHive, true))
				{
					foreach (var valname in regRunKey.GetValueNames())
					{
						string value = regRunKey.GetValue(valname).ToString();
						if (value == null || value.IndexOf(thisAppExeName, StringComparison.InvariantCultureIgnoreCase) != -1)
							continue;//Skip if it is this application

						var runcom = SettingsSimple.ApplicationManagerSettings.RunCommand.CreateFromFullCommandline(
							value, valname);
						if (runcom != null)
						{
							//Always delete registry value
							try
							{
								regRunKey.DeleteValue(valname);
								Logging.LogWarningToFile(
								string.Format("Deleted RegistryKey: key = '{0}', valuename = '{1}', valuestring = '{2}'",
									hive.ToString() + "\\" + subpathInHive, valname, value),
								Logging.ReportingFrequencies.Daily,
								cThisAppName);
							}
							catch (Exception exc)
							{
								Logging.LogErrorToFile(
								string.Format("Unable to delete RegistryKey: key = '{0}', valuename = '{1}', valuestring = '{2}', error: {3}",
									hive.ToString() + "\\" + subpathInHive, valname, value, exc.Message),
								Logging.ReportingFrequencies.Daily,
								cThisAppName);
								UserMessages.ShowWarningMessage("Exception trying to delete registry key: " + exc.Message);
							}

							string tmpFullpathWithArgs = (runcom.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
								? runcom.AppPath
								: ApplicationDetails.GetApplicationFullPathFromOwnAppname(runcom.AppPath)) + (runcom.CommandlineArguments ?? "");
							if (!currentFullpathsWithArgs.Contains(tmpFullpathWithArgs, StringComparer.InvariantCultureIgnoreCase))
							{
								currentFullpathsWithArgs.Add(tmpFullpathWithArgs);
								commands.Add(runcom);
							}
							else
								UserMessages.ShowWarningMessage("Cannot add startup item from Registry, item already in list:"
									+ Environment.NewLine + runcom.AppPath);
						}
					}
				}
			}
			SettingsSimple.ApplicationManagerSettings.Instance.RunCommands = commands;
			PopulateApplicationsList(true);
		}

		private void buttonGetFromStartupFolder_Click(object sender, RoutedEventArgs e)
		{
			GetCommandsFromStartupFolder();
		}

		private void GetCommandsFromStartupFolder()
		{
			var commands = SettingsSimple.ApplicationManagerSettings.Instance.RunCommands;
			if (commands == null)
				commands = new List<SettingsSimple.ApplicationManagerSettings.RunCommand>();
			else
				commands = commands.Clone();

			List<string> currentFullpathsWithArgs = commands
				.Select(
					com => (com.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
					? com.AppPath
					: ApplicationDetails.GetApplicationFullPathFromOwnAppname(com.AppPath)) + (com.CommandlineArguments ?? ""))
				.Where(s => !string.IsNullOrWhiteSpace(s))
				.ToList();

			string thisAppExeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);

			var files = Directory.GetFiles(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
			var folders = Directory.GetDirectories(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
			foreach (var actualShortcutFile in files.Concat(folders))
			{
				if (actualShortcutFile.EndsWith("desktop.ini", StringComparison.InvariantCultureIgnoreCase))
					continue;
				string shortcutPath, shortcutArguments, displayName, iconPath;
				if (WindowsInterop.GetShortcutTargetFile(actualShortcutFile, out shortcutPath, out shortcutArguments, out iconPath))
				{
					SettingsSimple.ApplicationManagerSettings.RunCommand runcom;

					shortcutPath = shortcutPath.Trim('\"');
					displayName = Path.GetFileNameWithoutExtension(actualShortcutFile);
					if (string.IsNullOrWhiteSpace(shortcutArguments)
						&& File.Exists(shortcutPath)
						&& !Path.GetExtension(shortcutPath).Equals(".exe", StringComparison.InvariantCultureIgnoreCase))
					{
						string fileExtension = Path.GetExtension(shortcutPath);
						bool isBatchFile = fileExtension.Equals(".bat", StringComparison.InvariantCultureIgnoreCase);
						shortcutArguments = (isBatchFile ? "/C " : "") + "\"" + shortcutPath.Trim('\"') + "\"";
						shortcutPath = isBatchFile ? "cmd.exe" : "explorer.exe";
					}
					else if (string.IsNullOrWhiteSpace(shortcutArguments)
						&& Directory.Exists(shortcutPath))
					{
						shortcutArguments = "\"" + shortcutPath.Trim('\"') + "\"";
						shortcutPath = "explorer.exe";
					}
					else
						shortcutPath = "\"" + shortcutPath + "\"";
					runcom = SettingsSimple.ApplicationManagerSettings.RunCommand.CreateFromFullCommandline(
						shortcutPath + (!string.IsNullOrWhiteSpace(shortcutArguments) ? " " + shortcutArguments : ""),
						displayName);
					if (runcom != null)
					{
						//Delete file
						try
						{
							bool isFile = File.Exists(actualShortcutFile);
							bool isDir = Directory.Exists(actualShortcutFile);
							if (isFile)
								File.Delete(actualShortcutFile);
							else if (isDir)
								Directory.Delete(actualShortcutFile);
							Logging.LogWarningToFile(
								"Deleted shortcut file: " + actualShortcutFile,
								Logging.ReportingFrequencies.Daily,
								cThisAppName);
						}
						catch (Exception exc)
						{
							Logging.LogErrorToFile(
								"Unable to delete file: " + actualShortcutFile + ", error: " + exc.Message,
								Logging.ReportingFrequencies.Daily,
								cThisAppName);
							UserMessages.ShowErrorMessage(exc.Message);
						}

						string tmpFullpathWithArgs = (runcom.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
								? runcom.AppPath
								: ApplicationDetails.GetApplicationFullPathFromOwnAppname(runcom.AppPath)) + (runcom.CommandlineArguments ?? "");
						if (!currentFullpathsWithArgs.Contains(tmpFullpathWithArgs, StringComparer.InvariantCultureIgnoreCase))
						{
							currentFullpathsWithArgs.Add(tmpFullpathWithArgs);
							commands.Add(runcom);
						}
						else
							UserMessages.ShowWarningMessage("Cannot add startup item from Startup folder, item already in list:"
								+ Environment.NewLine + runcom.AppPath);
					}
				}
			}
			SettingsSimple.ApplicationManagerSettings.Instance.RunCommands = commands;
			PopulateApplicationsList(true);
		}

		private bool isPaused = false;
		private void buttonPauseStarting_Click(object sender, RoutedEventArgs e)
		{
			if (buttonPauseStarting.Content.ToString() == originalPauseButtonContent)
			{
				isPaused = true;
				labelStatus.Content = "Paused, click resume to continue";
				buttonPauseStarting.Content = "Resume starting apps";
				this.UpdateLayout();
			}
			else
			{
				isPaused = false;
				labelStatus.Content = "Resumed starting apps";
				buttonPauseStarting.Content = originalPauseButtonContent;
				this.UpdateLayout();
			}
		}

		private void Window_Closing(object sender, CancelEventArgs e)
		{
			try
			{
				startAppsTimer.Dispose();
			}
			catch { }
		}

		ScaleTransform scaleTransform = new ScaleTransform(1, 1);
		private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (Keyboard.Modifiers == ModifierKeys.Control && mainDockPanel.LayoutTransform != smallScale)
			{
				e.Handled = true;
				mainDockPanel.LayoutTransform = scaleTransform;
				if (e.Delta > 0)//Mouse wheel up
				{
					scaleTransform.ScaleX += 0.1;
					scaleTransform.ScaleY += 0.1;
				}
				else if (e.Delta < 0)//Mouse wheel down
				{
					scaleTransform.ScaleX -= 0.1;
					scaleTransform.ScaleY -= 0.1;
				}
				this.UpdateLayout();
			}
		}

		private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.MiddleButton == MouseButtonState.Pressed)
				ToggleScaling();
		}

		private void OnMenuItemExitOrHideClick(object sender, EventArgs e)
		{
			ExitOrHide();
		}

		private void OnMenuItemQuickCloseAppsClick(object sender, EventArgs e)
		{
			foreach (var app in Applications)
				if (app.IncludeInQuickClose)
					app.KillProcess();
		}

		private void labelAbout_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			bool origTopmost = this.Topmost;
			this.Topmost = false;
			try
			{
				AboutWindow2.ShowAboutWindow(new System.Collections.ObjectModel.ObservableCollection<DisplayItem>()
				{
					new DisplayItem("Author", "Francois Hill"),
					new DisplayItem("Icon(s) obtained from", "http://www.iconfinder.com",
						"http://www.iconfinder.com/icondetails/1270/128/devices_hardware_input_settings_icon",
						"http://www.iconfinder.com/icondetails/34218/128/add_cross_delete_exit_remove_icon")
				});
			}
			finally
			{
				this.Topmost = origTopmost;
			}
		}

		private void menuitemGetFromRegistry_Click(object sender, RoutedEventArgs e)
		{
			GetCommandsFromRegistry();
		}

		private void menuitemGetFromStartupFolder_Click(object sender, RoutedEventArgs e)
		{
			GetCommandsFromStartupFolder();
		}

		private void menuitemExit_Click(object sender, RoutedEventArgs e)
		{
			ExitOrHide();
			//this.Close();
		}

		private void dataGrid1_AutoGeneratingColumn_1(object sender, DataGridAutoGeneratingColumnEventArgs e)
		{
			DataGrid dg = sender as DataGrid;
			if (dg == null) return;

			if (e.PropertyName.Equals("ApplicationStatus", StringComparison.InvariantCultureIgnoreCase))//We cannot simply make 'ApplicationStatus' protected, otherwise the WPF 
			{
				e.Cancel = true;
				return;
			}

			if (e.PropertyType == typeof(Boolean) || e.PropertyType == typeof(Nullable<Boolean>))
			{
				//if (e.PropertyType == typeof(Nullable<bool>))
				//{
				DataGridCheckBoxColumn column = new DataGridCheckBoxColumn();
				column.Header = e.PropertyName;
				column.IsReadOnly = false;
				column.IsThreeState = e.PropertyType == typeof(Nullable<Boolean>);
				column.Binding = new Binding() { Mode = BindingMode.OneWay, Path = new PropertyPath(e.PropertyName) };
				column.ElementStyle = dg.FindResource("DiscreteCheckBoxStyle_Readonly") as Style;
				e.Column = column;
				//}
			}
			else if (e.PropertyType == typeof(ImageSource))
			{
				DataGridTemplateColumn column = new DataGridTemplateColumn();
				column.Header = e.PropertyName;
				column.CellTemplate = dg.FindResource("ImageTemplate") as DataTemplate;
				e.Column = column;
			}
			else if (e.PropertyType == typeof(DateTime))
			{
				DataGridTextColumn column = new DataGridTextColumn();
				column.Header = e.PropertyName;
				column.Binding = new Binding() { Mode = BindingMode.OneWay, Path = new PropertyPath(e.PropertyName), StringFormat = @"HH:mm" };
				e.Column = column;
			}

			string displayName = ReflectionInterop.GetPropertyDisplayName(e.PropertyDescriptor);
			if (!string.IsNullOrEmpty(displayName))
				e.Column.Header = displayName;
			string descriptionForTooltips = ReflectionInterop.GetPropertyDescription(e.PropertyDescriptor);
			if (!string.IsNullOrWhiteSpace(descriptionForTooltips))
				ToolTipService.SetToolTip(e.Column, descriptionForTooltips);
		}

		private void menuitemStartNow_Click(object sender, RoutedEventArgs e)
		{
			var fe = sender as FrameworkElement;
			if (fe == null) return;
			ApplicationDetails appdet = fe.DataContext as ApplicationDetails;
			if (appdet == null) return;

			ThreadingInterop.PerformOneArgFunctionSeperateThread<ApplicationDetails>(
				(applicationDetails) => applicationDetails.StartNow_NotAllowMultipleInstances(true, true),
				appdet, false);
		}

		private void dataGrid1_SelectedCellsChanged_1(object sender, SelectedCellsChangedEventArgs e)
		{
			dataGrid1.UnselectAll();
		}
	}

	public class ApplicationDetails : INotifyPropertyChanged
	{
		public enum ApplicationStatusses { Running, NotResponding, NotRunning, RanSuccessfullyButClosedAgain, InvalidPath };

		private static List<ApplicationDetails> ListOfCreatedItems = new List<ApplicationDetails>();

		public SettingsSimple.ApplicationManagerSettings.RunCommand Command;
		protected string ApplicationName { get; private set; }
		public string DisplayName { get; private set; }

		private ApplicationStatusses _applicationstatus;
		public ApplicationStatusses ApplicationStatus { get { return _applicationstatus; } set { _applicationstatus = value; OnPropertyChanged("ApplicationStatus", "ApplicationStatusString"); } }
		//protected ApplicationStatusses ApplicationStatus { get; private set; }
		[DisplayName("Status")]
		[Description("The current status of the application.")]
		public string ApplicationStatusString
		{
			get
			{
				if (ApplicationStatus != ApplicationStatusses.RanSuccessfullyButClosedAgain)
					return ApplicationStatus.ToString().InsertSpacesBeforeCamelCase();
				else
					return "Closed";
			}
		}
		protected string ApplicationFullPath { get; private set; }
		protected string FullpathToProcessExe { get; private set; }//Usually required with Portable apps
		private bool ProcessExeDefersFromStartExe { get; set; }
		protected string ApplicationArguments { get; private set; }
		[DisplayName("Input")]
		[Description("Wait for user input (mouse or keyboard).")]
		public bool WaitForUserInput { get; private set; }
		[DisplayName("Delay")]
		[Description("Number of seconds to wait after application started.")]
		public int DelayAfterStartSeconds { get; private set; }
		[DisplayName("Enabled")]
		[Description("Whether the application should be started.")]
		public bool IsEnabled { get; private set; }
		[DisplayName("Close")]
		[Description("Wheter the application should be closed when we click 'Quick close...' in the tray icon right-click menu.")]
		public bool IncludeInQuickClose { get; private set; }

		[DisplayName("Time")]
		[Description("The time this application was started.")]
		public DateTime StartupTimeIfRunning { get; private set; }
		[DisplayName("Running")]
		[Description("Wheter this application is currently running.")]
		public bool IsRunning { get { return this.ApplicationStatus == ApplicationStatusses.Running; } }

		private ApplicationDetails()
		{
			ListOfCreatedItems.Add(this);
		}

		public ApplicationDetails(SettingsSimple.ApplicationManagerSettings.RunCommand command)
			: this()//string ApplicationName, string ApplicationFullPath = null, string ApplicationArguments = null, ApplicationStatusses ApplicationStatus = ApplicationStatusses.NotRunning)
		{
			this.Command = command;
			string path = command.AppPath;
			string appname = 
				command.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
				? Path.GetFileNameWithoutExtension(path)
				: command.AppPath;//It is RunCommand.PathTypes.OwnApp name

			this.ApplicationName = appname;
			this.ApplicationStatus = ApplicationStatusses.NotRunning;

			this.ProcessExeDefersFromStartExe = false;
			if (command.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath)
			{
				this.ApplicationFullPath = path;
				bool exeToKillDefersFromOriginal = false;
				this.FullpathToProcessExe = !string.IsNullOrWhiteSpace(FullpathToProcessExe)
					? FullpathToProcessExe : GetFullpathToProcessExe_FromFullpath(path, out exeToKillDefersFromOriginal);
				this.ProcessExeDefersFromStartExe = exeToKillDefersFromOriginal;
			}
			else
			{
				this.ApplicationFullPath = GetApplicationFullPathFromOwnAppname(this.ApplicationName);
				this.FullpathToProcessExe = this.ApplicationFullPath;
			}
			this.ApplicationArguments = command.CommandlineArguments;//string.Join(" ", command.CommandlineArguments.Select(c => "\"" + c.Trim('\"') + "\""));

			this.DisplayName = command.DisplayName;
			this.WaitForUserInput = command.WaitForUserInput;
			this.DelayAfterStartSeconds = command.DelayAfterStartSeconds;
			this.IsEnabled = command.IsEnabled;
			this.IncludeInQuickClose = command.IncludeInQuickClose;

			UpdateApplicationRunningStatus(false);
		}

		private static string GetFullpathToProcessExe_FromFullpath(string originallyStartedExepath, out bool exeToKillDefersFromOriginal)
		{
			/*If we run a Portable app the Exe that we run starts up another EXE,
			 * maybe build support in for this. I.e. check if the Foldername ends with "Portable"
			 * then get the piece before "Portable" (like take Skype if it was SkypePortable) then got
			 * into the folder "App\Skype\" and search for exe named "SKype.exe" in nested folders.
			 * Warn if we found multiple or no matches.*/
			string folderPath = Path.GetDirectoryName(originallyStartedExepath);
			string folderName = Path.GetFileName(folderPath);
			if (!folderName.EndsWith("Portable", StringComparison.InvariantCultureIgnoreCase))
			{
				exeToKillDefersFromOriginal = false;
				return originallyStartedExepath;//Not a portable app?
			}

			string appname = folderName.Substring(0, folderName.Length - "Portable".Length);
			string appFolderpath = Path.Combine(folderPath, "App", appname);//should end up being like ...\PortableApps\SkypePortable\App\Skype
			string exeFilenameToFind = "\\" + appname + ".exe";

			if (!Directory.Exists(appFolderpath))
			{
				exeToKillDefersFromOriginal = false;
				return originallyStartedExepath;
			}

			var matchedExeFiles = Directory.GetFiles(appFolderpath, "*.exe", SearchOption.AllDirectories)
				.Where(path => path.EndsWith(exeFilenameToFind, StringComparison.InvariantCultureIgnoreCase))
				.ToList();
			if (matchedExeFiles.Count == 0)
			{
				Notifs.Notify(err => UserMessages.ShowErrorMessage(err),
					"Possible portable app found but could not find '" + exeFilenameToFind + "' in nested folders from portable exe '" + originallyStartedExepath + "'");
				exeToKillDefersFromOriginal = false;
				return originallyStartedExepath;
			}
			if (matchedExeFiles.Count > 1)
				Notifs.Notify(err => UserMessages.ShowErrorMessage(err),
					"Multiple portable app EXEs found (using first) with name '" + exeFilenameToFind + "' in nested folders from portable exe '" + originallyStartedExepath + "'");
			exeToKillDefersFromOriginal = true;
			return matchedExeFiles[0];
		}

		~ApplicationDetails()
		{
			if (ListOfCreatedItems.Contains(this))
				ListOfCreatedItems.Remove(this);
		}

		//A timer to make sure our HumanFriendly dates are correct (Category.DueDate and Items[].Created)
		private static System.Threading.Timer _timerToEnsureHumanFriendlyDatesAlwaysCorrect = new System.Threading.Timer(
			delegate
			{
				for (int i = 0; i < ListOfCreatedItems.Count; i++)
				{
					try
					{
						ListOfCreatedItems[i].OnPropertyChanged("StartupTimeIfRunning");
					}
					catch { }
				}
			},
			null,
			TimeSpan.FromSeconds(0),
			TimeSpan.FromSeconds(0.9));//Just under a second, bit more certain

		public static string GetApplicationFullPathFromOwnAppname(string ownApplicationName)
		{
			string fullpathOrOwnAppname = null;
			string progfilesDir86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86).TrimEnd('\\');
			string progfilesDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd('\\');
			if (Directory.Exists(progfilesDir86 + "\\" + ownApplicationName))
				fullpathOrOwnAppname = progfilesDir86 + "\\" + ownApplicationName;
			else if (Directory.Exists(progfilesDir86 + "\\" + ownApplicationName.InsertSpacesBeforeCamelCase()))
				fullpathOrOwnAppname = progfilesDir86 + "\\" + ownApplicationName.InsertSpacesBeforeCamelCase();
			else if (Directory.Exists(progfilesDir + "\\" + ownApplicationName))
				fullpathOrOwnAppname = progfilesDir + "\\" + ownApplicationName;
			else if (Directory.Exists(progfilesDir + "\\" + ownApplicationName.InsertSpacesBeforeCamelCase()))
				fullpathOrOwnAppname = progfilesDir + "\\" + ownApplicationName.InsertSpacesBeforeCamelCase();

			if (fullpathOrOwnAppname != null)
			{
				fullpathOrOwnAppname = fullpathOrOwnAppname.TrimEnd('\\');
				if (File.Exists(fullpathOrOwnAppname + "\\" + ownApplicationName + ".exe"))
					fullpathOrOwnAppname = fullpathOrOwnAppname + "\\" + ownApplicationName + ".exe";
				else if (File.Exists(fullpathOrOwnAppname + "\\" + ownApplicationName.InsertSpacesBeforeCamelCase() + ".exe"))
					fullpathOrOwnAppname = fullpathOrOwnAppname + "\\" + ownApplicationName.InsertSpacesBeforeCamelCase() + ".exe";
			}
			else if (File.Exists(ownApplicationName))
				fullpathOrOwnAppname = ownApplicationName;
			return fullpathOrOwnAppname;
		}

		public void UpdateApplicationRunningStatus(bool markExplorerAsRunning)
		{
			//Maybe do not change it back to DateTime.MinValue also when status is 'ApplicationStatusses.RanSuccessfullyButClosedAgain'
			this.StartupTimeIfRunning = DateTime.MinValue;

			Process proc = GetProcessForApplication();
			if (proc == null)
			{
				if (this.ApplicationFullPath == null)
					this.ApplicationStatus = ApplicationStatusses.InvalidPath;
				else if (!successfullyRanOnce)
					this.ApplicationStatus = ApplicationStatusses.NotRunning;
				else//if (successfullyRanOnce)
					this.ApplicationStatus = ApplicationStatusses.RanSuccessfullyButClosedAgain;
			}
			else
			{
				if (markExplorerAsRunning || !IsExplorer && !IsCmd)
				{
					this.ApplicationStatus = ApplicationStatusses.Running;
					this.StartupTimeIfRunning = proc.StartTime;
					this.successfullyRanOnce = true;
				}

				ThreadingInterop.PerformOneArgFunctionSeperateThread((arg) =>
				{
					object[] ProcAndAppDet = arg as object[];
					if (ProcAndAppDet != null && ProcAndAppDet.Length == 2 && ProcAndAppDet[0] is Process && ProcAndAppDet[1] is ApplicationDetails)
					{
						Process thisProc = ProcAndAppDet[0] as Process;
						ApplicationDetails thisAppdet = ProcAndAppDet[1] as ApplicationDetails;
						thisProc.WaitForExit();
						thisAppdet.UpdateApplicationRunningStatus(true);
					}
				},
				new object[] { proc, this },
				false);

				//Skip updating fullpath for now
				//this.ApplicationFullPath = proc.MainModule.FileName;
			}
			OnPropertyChanged(
				"ApplicationStatus",
				"ApplicationStatusString",
				"StartupTimeIfRunning",
				"IsRunning");
		}

		private bool IsChrome { get { return this.ApplicationName.Equals("chrome", StringComparison.InvariantCultureIgnoreCase); } }
		private bool IsExplorer { get { return this.ApplicationName.Equals("explorer", StringComparison.InvariantCultureIgnoreCase); } }
		private bool IsCmd { get { return this.ApplicationName.Equals("cmd", StringComparison.InvariantCultureIgnoreCase); } }
		private bool IsPortableAppsPlatform
		{
			get
			{
				return this.ApplicationFullPath != null
					&& this.ApplicationFullPath.IndexOf("Portable Apps Platform", StringComparison.InvariantCultureIgnoreCase) != -1
					&& this.ApplicationFullPath.EndsWith("\\Start.exe", StringComparison.InvariantCultureIgnoreCase);
			}
		}

		private Process GetProcessForApplication()
		{
			int retryCount = 0;
		retryOnException:
			try
			{
				var matchingProcs =
					Process.GetProcessesByName(
						this.ProcessExeDefersFromStartExe
						? Path.GetFileNameWithoutExtension(this.FullpathToProcessExe)
						: this.ApplicationName);
				if (matchingProcs.Length == 0)
					matchingProcs = Process.GetProcessesByName(this.ApplicationName.InsertSpacesBeforeCamelCase());
				if (IsPortableAppsPlatform)
					matchingProcs = Process.GetProcessesByName("PortableAppsPlatform");
				if (matchingProcs.Length >= 1)
				{
					//if (matchingProcs.Length > 1)
					//{
					if (File.Exists(this.FullpathToProcessExe))
					{
						var itemsMatchingExactPath = matchingProcs.Where(
							p => p.MainModule != null
							&& p.MainModule.FileName != null
							&& p.MainModule.FileName.Trim('\\').ToLower() == this.FullpathToProcessExe.Trim('\\').ToLower()).ToArray();
						if (itemsMatchingExactPath.Length == 0 && IsPortableAppsPlatform)
							itemsMatchingExactPath = matchingProcs;
						if (itemsMatchingExactPath.Length > 1)
						{
							//We have the exact same processes
							if (!IsChrome
								&& !IsExplorer
								&& !IsCmd)
								ShowNoCallbackNotificationInterop.Notify(
									err => UserMessages.ShowErrorMessage(err),
									"Multiple processes found with name = '" + this.ApplicationName
									+ "', using first one with full path = " + matchingProcs[0].MainModule.FileName,
									"Multiple processes",
									ShowNoCallbackNotificationInterop.NotificationTypes.Warning);
						}
						else if (itemsMatchingExactPath.Length == 1)
							return itemsMatchingExactPath.First();
						else
						{
							//UserMessages.ShowWarningMessage(
							//    "Unable to find process (with exact same file path) for app '" + this.ApplicationName
							//    + "', full path = " + this.FullpathToProcessExe);
							return null;//No valid match found
						}
					}
					//}
					return matchingProcs[0];
				}
				else// if (matchingProcs.Length == 0)
					return null;
			}
			catch (Exception exc)
			{
				if (++retryCount <= 5)//Only retry 5 times
				{
					Thread.Sleep(2000);
					goto retryOnException;
				}
				else
				{
					try
					{
						ShowNoCallbackNotificationInterop.Notify(
							err => UserMessages.ShowErrorMessage(err),
							"Unable to find process for application '" + this.ApplicationName + "', exception: " + exc.Message,
							"Cannot find process",
							ShowNoCallbackNotificationInterop.NotificationTypes.Error,
							-1);
					}
					catch (Exception exc2)
					{
						UserMessages.ShowErrorMessage("Cannot show notification, unable to find process for application '"
							+ this.ApplicationName + "', exception: " + exc.Message + ". Error message for notification: " + exc2.Message);
					}
					return null;
				}
			}
		}

		public event PropertyChangedEventHandler PropertyChanged = new PropertyChangedEventHandler(delegate { });
		public void OnPropertyChanged(params string[] propertyNames) { foreach (var pn in propertyNames) PropertyChanged(this, new PropertyChangedEventArgs(pn)); }

		private bool successfullyRanOnce = false;
		//true=started now,false=could not start,null=already started
		public bool? StartNow_NotAllowMultipleInstances(bool showMessages = true, bool startAgainIfAlreadyRanAndClosed = false)
		{
			if (!this.IsEnabled)
				return null;//Application is not enabled

			if (this.ApplicationStatus != ApplicationDetails.ApplicationStatusses.Running)
			{
				if (!string.IsNullOrWhiteSpace(this.ApplicationFullPath))
				{
					DateTime systemStartupTime;
					TimeSpan idleTime;
					Win32Api.GetLastInputInfo(out systemStartupTime, out idleTime);
					if (this.WaitForUserInput && idleTime.TotalSeconds > 5)
						return null;//We should wait for userinput

					try
					{
						if (successfullyRanOnce && !startAgainIfAlreadyRanAndClosed)
							return null;

						if (File.Exists(this.ApplicationFullPath) || Directory.Exists(this.ApplicationFullPath))
							Environment.CurrentDirectory = Path.GetDirectoryName(this.ApplicationFullPath);
						Process proc = null;
						if (!string.IsNullOrWhiteSpace(this.ApplicationArguments))
							proc = Process.Start(this.ApplicationFullPath, this.ApplicationArguments);
						else
						{
							if (Directory.Exists(this.ApplicationFullPath))
								proc = Process.Start("explorer", this.ApplicationFullPath);
							else
								proc = Process.Start(this.ApplicationFullPath);
						}

						if (proc != null)
							successfullyRanOnce = true;

						bool alreadySlept = false;

						Process proc2 = null;
						if (!this.ProcessExeDefersFromStartExe)
						{
							if (proc == null)
							{
								alreadySlept = true;
								Thread.Sleep(this.DelayAfterStartSeconds * 1000);
							}
						}
						else
						{
							proc2 = GetProcessForApplication();
							if (proc2 != null)
								successfullyRanOnce = true;
							if (proc2 == null)
							{//Try again after delay
								alreadySlept = true;
								Thread.Sleep(this.DelayAfterStartSeconds * 1000);
								proc2 = GetProcessForApplication();
								if (proc2 != null)
									successfullyRanOnce = true;
							}
						}

						this.UpdateApplicationRunningStatus(true);
						if (!alreadySlept)
							Thread.Sleep(this.DelayAfterStartSeconds * 1000);

						ThreadingInterop.PerformOneArgFunctionSeperateThread((arg) =>
						{
							object[] ProcAndAppDet_AndProcToKill = arg as object[];
							if (ProcAndAppDet_AndProcToKill != null
								&& ProcAndAppDet_AndProcToKill.Length == 3
								&& ProcAndAppDet_AndProcToKill[0] is Process
								&& ProcAndAppDet_AndProcToKill[1] is ApplicationDetails
								&& (ProcAndAppDet_AndProcToKill[2] == null || ProcAndAppDet_AndProcToKill[2] is Process))
							{
								Process thisProc = ProcAndAppDet_AndProcToKill[0] as Process;
								ApplicationDetails thisAppdet = ProcAndAppDet_AndProcToKill[1] as ApplicationDetails;
								Process processToKill = ProcAndAppDet_AndProcToKill[2] as Process;
								thisProc.WaitForExit();
								if (processToKill != null)
									processToKill.WaitForExit();
								thisAppdet.UpdateApplicationRunningStatus(true);
							}
						},
						new object[] { proc, this, proc2 },
						false);
						return true;
					}
					catch (Exception exc)
					{
						ShowNoCallbackNotificationInterop.Notify(
							err => UserMessages.ShowErrorMessage(err),
							exc.Message + Environment.NewLine + exc.StackTrace,
							"Error starting application",
							ShowNoCallbackNotificationInterop.NotificationTypes.Error,
							-1);
						return false;//Did not start app
					}
				}
				else
				{
					if (showMessages)
						ShowNoCallbackNotificationInterop.Notify(
							err => UserMessages.ShowErrorMessage(err),
							"No application full path defined for application " + this.ApplicationName,
							"No full path",
							ShowNoCallbackNotificationInterop.NotificationTypes.Error,
							-1);
					return false;//Did not start app
				}
			}
			else
			{
				if (showMessages)
					ShowNoCallbackNotificationInterop.Notify(
						err => UserMessages.ShowErrorMessage(err),
						"Application already running: " + this.ApplicationName,
						"Already running",
						ShowNoCallbackNotificationInterop.NotificationTypes.Warning,
						5);
				return false;//Did not start app
			}
		}

		public void KillProcess()
		{
			Process proc = GetProcessForApplication();
			if (proc == null)
				return;

			proc.Kill();
		}
	}

	public class ApplicationStatusToColorConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (!(value is ApplicationDetails.ApplicationStatusses))
				return Colors.Transparent;

			switch ((ApplicationDetails.ApplicationStatusses)value)
			{
				case ApplicationDetails.ApplicationStatusses.Running:
					return Colors.Green;
				case ApplicationDetails.ApplicationStatusses.NotResponding:
					return Colors.Orange;
				case ApplicationDetails.ApplicationStatusses.NotRunning:
					return Colors.Transparent;
				case ApplicationDetails.ApplicationStatusses.RanSuccessfullyButClosedAgain:
					return Colors.Magenta;
				case ApplicationDetails.ApplicationStatusses.InvalidPath:
					return Colors.Red;
				default:
					return Colors.Transparent;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}

	public class CheckboxTrueAndIsRunningToVisibilityConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (values.Length != 2)
				return Visibility.Visible;
			if (!(values[0] is bool)
				|| !(values[1] is bool))
				return Visibility.Visible;

			if ((bool)values[0] == true
				|| (bool)values[1] == true)
				return Visibility.Visible;
			else
				return Visibility.Collapsed;
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
