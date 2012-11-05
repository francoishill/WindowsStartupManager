using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Collections.ObjectModel;
using SharedClasses;
using System.IO;
using System.Diagnostics;
using System.Threading;
using ApplicationManagerSettings = SharedClasses.GlobalSettings.ApplicationManagerSettings;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Management;

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public const string cThisAppName = "WindowsStartupManager";
		private readonly TimeSpan DelaySecondsBetweenApps = TimeSpan.FromSeconds(2);//TimeSpan.FromMilliseconds(500);
		private string originalPauseButtonContent;
		ObservableCollection<ApplicationDetails> Applications = new ObservableCollection<ApplicationDetails>();

		public MainWindow()
		{
			InitializeComponent();
			originalPauseButtonContent = buttonPauseStarting.Content.ToString();
			wmicpus = new WqlObjectQuery("SELECT * FROM Win32_Processor");
			cpus = new ManagementObjectSearcher(wmicpus);
		}

		Timer startAppsTimer;
		Timer timerToPopulateList;
		Timer timerToLogCpuUsage;
		bool listAlreadyPopulatedAtLeastOnce = false;
		float cCpuUsageTolerancePercentage = 30;
		ObjectQuery wmicpus;
		ManagementObjectSearcher cpus;
		DateTime? _systemStartupTime = null;
		DateTime systemStartupTime
		{
			get
			{
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
		bool skipLoggingCpuUsage = false;
		int minimumCPUrunningSeconds = 30;
		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			labelDelayBetweenApps.Content = "Delay after each app = "
				+ DelaySecondsBetweenApps.TotalMilliseconds + "ms";

			if (IsSystemRunningMinimumDuration())
				labelStatus.Content = "CPU already running more than "
					+ minimumCPUrunningSeconds + " seconds, applications to be started soon";
			else
				labelStatus.Content = "Waiting for CPU to be running more than "
					+ minimumCPUrunningSeconds + " seconds (currently " + GetCPUrunningDuration().TotalSeconds + " seconds)";

			//Timer to populate the list with applications
			timerToPopulateList =
				new Timer(delegate
				{
					Dispatcher.Invoke((Action)delegate
					{
						PopulateApplicationsList();
					});
				},
				null,
				200,
				System.Threading.Timeout.Infinite);

			//Timer to check if must start apps keep this window on top
			startAppsTimer =
				new Timer(delegate
				{
					Dispatcher.Invoke((Action)delegate { OwnBringIntoView(); });
					if (!listAlreadyPopulatedAtLeastOnce)
						Dispatcher.Invoke((Action)delegate
						{
							PopulateApplicationsList();
						});

					if (IsSystemRunningMinimumDuration())//Check system already running a while
						Dispatcher.Invoke((Action)delegate
					 {
						 StartAllApplications();
					 });
					/*if (GetMaxCpuUsage() < cCpuUsageTolerancePercentage)//Check that CPU usage is low enough
						StartAllApplications();*/
				},
				null,
				TimeSpan.FromSeconds(0),
				TimeSpan.FromSeconds(5));

			timerToLogCpuUsage = new Timer(delegate
				{
					//Log for first while
					if (!skipLoggingCpuUsage)
					{
						string logfile = Logging.LogInfoToFile(
							"[" + DateTime.Now.ToString("HH:mm:ss")
								+ "] (system startup at " + systemStartupTime.ToString("HH:mm:ss")
								+ ") cpu usage = " + (int)GetMaxCpuUsage() + "%",
							Logging.ReportingFrequencies.Daily,
							cThisAppName,
							"CpuUsageLogs");
						if (DateTime.Now.Subtract(systemStartupTime).TotalMinutes > 20)
						{
							skipLoggingCpuUsage = true;
							//if (UserMessages.Confirm("Cpu usages was logged, open the log file?"))
							//    Process.Start("notepad", logfile);
						}
					}
				},
				null,
				TimeSpan.FromSeconds(0),
				TimeSpan.FromSeconds(2));
		}

		private TimeSpan GetCPUrunningDuration()
		{
			return DateTime.Now.Subtract(systemStartupTime);
		}

		private bool IsSystemRunningMinimumDuration()
		{
			return GetCPUrunningDuration().TotalSeconds > minimumCPUrunningSeconds;
		}

		bool isbusyStarting = false;
		private void StartAllApplications()
		{
			if (isbusyStarting)
				return;

			isbusyStarting = true;
			try
			{
				//foreach (var app in Applications)
				for (int i = 0; i < Applications.Count; i++)
				{
					var app = Applications[i];
					labelStatus.Content = "Starting \"" + app.DisplayName + "\", waiting " + DelaySecondsBetweenApps.TotalSeconds + " seconds";
					labelStatus.UpdateLayout();
					bool? startupSuccess
						= app.StartNow_NotAllowMultipleInstances(false);
					while (isPaused)
						WPFHelper.DoEvents();
					this.UpdateLayout();
					if (startupSuccess == true)//Was started now (not started previously, did not fail)
						Thread.Sleep(DelaySecondsBetweenApps);
					//if (GetMaxCpuUsage() >= cCpuUsageTolerancePercentage)//If CPU usage is too high
					//    break;
				}
			}
			finally
			{
				isbusyStarting = false;
			}
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
			catch//TODO: Might want to look at handling these exceptions?
			{
				return 100;
			}
		}

		bool busyPopulating = false;
		private void PopulateApplicationsList()
		{
			if (busyPopulating)
				return;

			busyPopulating = true;

			listAlreadyPopulatedAtLeastOnce = true;
			Applications.Clear();
			if (SettingsSimple.ApplicationManagerSettings.Instance.RunCommands != null)
				foreach (var comm in SettingsSimple.ApplicationManagerSettings.Instance.RunCommands)
					Applications.Add(new ApplicationDetails(comm));
			listBox1.ItemsSource = Applications;
			labelPleaseWait.Visibility = System.Windows.Visibility.Collapsed;
			this.BringIntoView();

			busyPopulating = false;
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

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			//this.Hide();
			this.Close();//This app is intended for on windows startup only
			Environment.Exit(0);//Forces exit
		}

		private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			PositionWindowBottomRight();
		}

		private ScaleTransform originalScale = new ScaleTransform(1, 1);
		private ScaleTransform smallScale = new ScaleTransform(0.05, 0.05);
		private void Window_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			if (mainDockPanel.LayoutTransform != smallScale)
				mainDockPanel.LayoutTransform = smallScale;
			else
				mainDockPanel.LayoutTransform = originalScale;
		}

		private void buttonGetFromRegistry_Click(object sender, RoutedEventArgs e)
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
			PopulateApplicationsList();
		}

		private void buttonGetFromStartupFolder_Click(object sender, RoutedEventArgs e)
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
			PopulateApplicationsList();
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
			if (mainDockPanel.LayoutTransform != smallScale)
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
	}

	public class ApplicationDetails : INotifyPropertyChanged
	{
		public SettingsSimple.ApplicationManagerSettings.RunCommand Command;
		public enum ApplicationStatusses { Running, NotResponding, NotRunning, RanSuccessfullyButClosedAgain, InvalidPath };
		public string ApplicationName { get; private set; }
		public string DisplayName { get; private set; }
		public ApplicationStatusses ApplicationStatus { get; private set; }
		public string ApplicationStatusString { get { return ApplicationStatus.ToString().InsertSpacesBeforeCamelCase(); } }
		public string ApplicationFullPath { get; private set; }
		public string ApplicationArguments { get; private set; }
		public bool WaitForUserInput { get; private set; }

		public ApplicationDetails(SettingsSimple.ApplicationManagerSettings.RunCommand command)//string ApplicationName, string ApplicationFullPath = null, string ApplicationArguments = null, ApplicationStatusses ApplicationStatus = ApplicationStatusses.NotRunning)
		{
			this.Command = command;
			string path = command.AppPath;
			string appname = 
				command.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
				? Path.GetFileNameWithoutExtension(path)
				: command.AppPath;//It is RunCommand.PathTypes.OwnApp name

			this.ApplicationName = appname;
			this.ApplicationStatus = ApplicationStatusses.NotRunning;
			if (command.PathType == SettingsSimple.ApplicationManagerSettings.RunCommand.PathTypes.FullPath)
				this.ApplicationFullPath = path;
			else
				this.ApplicationFullPath = GetApplicationFullPathFromOwnAppname(this.ApplicationName);
			this.ApplicationArguments = command.CommandlineArguments;//string.Join(" ", command.CommandlineArguments.Select(c => "\"" + c.Trim('\"') + "\""));

			this.DisplayName = command.DisplayName;
			this.WaitForUserInput = command.WaitForUserInput;

			UpdateApplicationRunningStatus(false);
		}

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

		private void UpdateApplicationRunningStatus(bool markExplorerAsRunning)
		{
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
					this.ApplicationStatus = ApplicationStatusses.Running;
				//TODO: Skip updating fullpath for now
				//this.ApplicationFullPath = proc.MainModule.FileName;
			}
			OnPropertyChanged("ApplicationStatus");
			OnPropertyChanged("ApplicationStatusString");
		}

		private bool IsChrome { get { return this.ApplicationName.Equals("chrome", StringComparison.InvariantCultureIgnoreCase); } }
		private bool IsExplorer { get { return this.ApplicationName.Equals("explorer", StringComparison.InvariantCultureIgnoreCase); } }
		private bool IsCmd { get { return this.ApplicationName.Equals("cmd", StringComparison.InvariantCultureIgnoreCase); } }
		private Process GetProcessForApplication()
		{
			var matchingProcs = Process.GetProcessesByName(this.ApplicationName);
			if (matchingProcs.Length == 0)
				matchingProcs = Process.GetProcessesByName(this.ApplicationName.InsertSpacesBeforeCamelCase());
			if (matchingProcs.Length >= 1)
			{
				if (matchingProcs.Length > 1)
				{
					if (!IsChrome
						&& !IsExplorer
						&& !IsCmd)
						UserMessages.ShowWarningMessage(
							"Multiple processes found with name = '" + this.ApplicationName
							+ "', using first one with full path = " + matchingProcs[0].MainModule.FileName);
				}
				return matchingProcs[0];
			}
			else// if (matchingProcs.Length == 0)
				return null;
		}

		public event PropertyChangedEventHandler PropertyChanged = new PropertyChangedEventHandler(delegate { });
		public void OnPropertyChanged(string propertyName) { PropertyChanged(this, new PropertyChangedEventArgs(propertyName)); }

		private bool successfullyRanOnce = false;
		//true=started now,false=could not start,null=already started
		public bool? StartNow_NotAllowMultipleInstances(bool showMessages = true, bool startAgainIfAlreadyRanAndClosed = false)
		{
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
						this.UpdateApplicationRunningStatus(true);

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
						return true;
					}
					catch (Exception exc)
					{
						UserMessages.ShowErrorMessage(exc.Message + Environment.NewLine + exc.StackTrace);
						return false;//Did not start app
					}
				}
				else
				{
					if (showMessages)
						UserMessages.ShowWarningMessage("No application full path defined for application " + this.ApplicationName);
					return false;//Did not start app
				}
			}
			else
			{
				if (showMessages)
					UserMessages.ShowInfoMessage("Application already running");
				return false;//Did not start app
			}
		}
	}

	public class ApplicationStatusToBrushConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			if (!(value is ApplicationDetails.ApplicationStatusses))
				return Brushes.Transparent;

			switch ((ApplicationDetails.ApplicationStatusses)value)
			{
				case ApplicationDetails.ApplicationStatusses.Running:
					return Brushes.Green;
				case ApplicationDetails.ApplicationStatusses.NotResponding:
					return Brushes.Orange;
				case ApplicationDetails.ApplicationStatusses.NotRunning:
					return Brushes.Gray;
				case ApplicationDetails.ApplicationStatusses.RanSuccessfullyButClosedAgain:
					return Brushes.Magenta;
				case ApplicationDetails.ApplicationStatusses.InvalidPath:
					return Brushes.Red;
				default:
					return Brushes.Transparent;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
