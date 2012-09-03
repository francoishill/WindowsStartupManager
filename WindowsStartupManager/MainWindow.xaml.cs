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
		ObservableCollection<ApplicationDetails> Applications = new ObservableCollection<ApplicationDetails>();

		public MainWindow()
		{
			InitializeComponent();
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
		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
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
						PopulateApplicationsList();

					if (DateTime.Now.Subtract(systemStartupTime).TotalSeconds > 30)//Check system already running a while
						if (GetMaxCpuUsage() < cCpuUsageTolerancePercentage)//Check that CPU usage is low enough
						{
							//foreach (var app in Applications)
							for (int i = 0; i < Applications.Count; i++)
							{
								Applications[i].StartNow_NotAllowMultipleInstances(false);
								if (GetMaxCpuUsage() >= cCpuUsageTolerancePercentage)//If CPU usage is too high
									break;
							}
						}
				},
				null,
				TimeSpan.FromSeconds(20),
				TimeSpan.FromSeconds(30));

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
							if (UserMessages.Confirm("Cpu usages was logged, open the log file?"))
								Process.Start("notepad", logfile);
						}
					}
				},
				null,
				TimeSpan.FromSeconds(0),
				TimeSpan.FromSeconds(2));
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

		private void PopulateApplicationsList()
		{
			listAlreadyPopulatedAtLeastOnce = true;
			Applications.Clear();
			if (OnlineSettings.ApplicationManagerSettings.Instance.RunCommands != null)
				foreach (var comm in OnlineSettings.ApplicationManagerSettings.Instance.RunCommands)
					Applications.Add(new ApplicationDetails(comm));
			listBox1.ItemsSource = Applications;
			labelPleaseWait.Visibility = System.Windows.Visibility.Collapsed;
			this.BringIntoView();
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

		private void Button_Click_1(object sender, RoutedEventArgs e)
		{
			var commands = OnlineSettings.ApplicationManagerSettings.Instance.RunCommands;
			if (commands == null)
				commands = new List<OnlineSettings.ApplicationManagerSettings.RunCommand>();
			else
				commands = commands.Clone();

			List<string> currentFullpathsWithArgs = commands
				.Select(
					com => (com.PathType == OnlineSettings.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
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

						var runcom = OnlineSettings.ApplicationManagerSettings.RunCommand.CreateFromFullCommandline(
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

							string tmpFullpathWithArgs = (runcom.PathType == OnlineSettings.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
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
			OnlineSettings.ApplicationManagerSettings.Instance.RunCommands = commands;
			PopulateApplicationsList();
		}

		private void Window_Closing(object sender, CancelEventArgs e)
		{
			try
			{
				startAppsTimer.Dispose();
			}
			catch { }
		}
	}

	public class ApplicationDetails : INotifyPropertyChanged
	{
		public OnlineSettings.ApplicationManagerSettings.RunCommand Command;
		public enum ApplicationStatusses { Running, NotResponding, NotRunning, RanSuccessfullyButClosedAgain, InvalidPath };
		public string ApplicationName { get; private set; }
		public string DisplayName { get; private set; }
		public ApplicationStatusses ApplicationStatus { get; private set; }
		public string ApplicationStatusString { get { return ApplicationStatus.ToString().InsertSpacesBeforeCamelCase(); } }
		public string ApplicationFullPath { get; private set; }
		public string ApplicationArguments { get; private set; }

		public ApplicationDetails(OnlineSettings.ApplicationManagerSettings.RunCommand command)//string ApplicationName, string ApplicationFullPath = null, string ApplicationArguments = null, ApplicationStatusses ApplicationStatus = ApplicationStatusses.NotRunning)
		{
			this.Command = command;
			string path = command.AppPath;
			string appname = 
				command.PathType == OnlineSettings.ApplicationManagerSettings.RunCommand.PathTypes.FullPath
				? Path.GetFileNameWithoutExtension(path)
				: command.AppPath;//It is RunCommand.PathTypes.OwnApp name

			this.ApplicationName = appname;
			this.ApplicationStatus = ApplicationStatusses.NotRunning;
			if (command.PathType == OnlineSettings.ApplicationManagerSettings.RunCommand.PathTypes.FullPath)
				this.ApplicationFullPath = path;
			else
				this.ApplicationFullPath = GetApplicationFullPathFromOwnAppname(this.ApplicationName);
			UpdateApplicationRunningStatus();
			this.ApplicationArguments = command.CommandlineArguments;//string.Join(" ", command.CommandlineArguments.Select(c => "\"" + c.Trim('\"') + "\""));

			this.DisplayName = command.DisplayName;
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

		private void UpdateApplicationRunningStatus()
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
				this.ApplicationStatus = ApplicationStatusses.Running;
				//TODO: Skip updating fullpath for now
				//this.ApplicationFullPath = proc.MainModule.FileName;
			}
			OnPropertyChanged("ApplicationStatus");
			OnPropertyChanged("ApplicationStatusString");
		}

		private Process GetProcessForApplication()
		{
			var matchingProcs = Process.GetProcessesByName(this.ApplicationName);
			if (matchingProcs.Length == 0)
				matchingProcs = Process.GetProcessesByName(this.ApplicationName.InsertSpacesBeforeCamelCase());
			if (matchingProcs.Length >= 1)
			{
				if (matchingProcs.Length > 1)
				{
					if (!this.ApplicationName.Equals("chrome", StringComparison.InvariantCultureIgnoreCase))
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
		public void StartNow_NotAllowMultipleInstances(bool showMessages = true, bool startAgainIfAlreadyRanAndClosed = false)
		{
			if (this.ApplicationStatus != ApplicationDetails.ApplicationStatusses.Running)
			{
				if (!string.IsNullOrWhiteSpace(this.ApplicationFullPath))
				{
					try
					{
						if (successfullyRanOnce && !startAgainIfAlreadyRanAndClosed)
							return;
						Process proc = null;
						if (!string.IsNullOrWhiteSpace(this.ApplicationArguments))
							proc = Process.Start(this.ApplicationFullPath, this.ApplicationArguments);
						else
							proc = Process.Start(this.ApplicationFullPath);

						if (proc != null)
							successfullyRanOnce = true;
						this.UpdateApplicationRunningStatus();

						ThreadingInterop.PerformOneArgFunctionSeperateThread((arg) =>
						{
							object[] ProcAndAppDet = arg as object[];
							if (ProcAndAppDet != null && ProcAndAppDet.Length == 2 && ProcAndAppDet[0] is Process && ProcAndAppDet[1] is ApplicationDetails)
							{
								Process thisProc = ProcAndAppDet[0] as Process;
								ApplicationDetails thisAppdet = ProcAndAppDet[1] as ApplicationDetails;
								thisProc.WaitForExit();
								thisAppdet.UpdateApplicationRunningStatus();
							}
						},
						new object[] { proc, this },
						false);
					}
					catch (Exception exc)
					{
						UserMessages.ShowErrorMessage(exc.Message + Environment.NewLine + exc.StackTrace);
					}
				}
				else
				{
					if (showMessages)
						UserMessages.ShowWarningMessage("No application full path defined for application " + this.ApplicationName);
				}
			}
			else
			{
				if (showMessages)
					UserMessages.ShowInfoMessage("Application already running");
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
