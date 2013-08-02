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
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Timers;
using System.Diagnostics;
using System.Collections.ObjectModel;
using SharedClasses;
using System.IO;
using System.Text.RegularExpressions;
//using Microsoft.Xna.Framework.Audio;
//using VoiceRecorder.Audio;
//using NAudio.Wave;

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for ReportWindow.xaml
	/// </summary>
	public partial class ReportWindow : Window
	{
		public ObservableCollection<WindowsMonitor.WindowTimes> originalUngroupedList = null;
		private static List<string> GroupingWindowTitlesBySubstring = new List<string>();

		public ReportWindow()
		{
			InitializeComponent();
			GetGroupingOfWindowTitles();
			textboxGroupingOfWindowTitles.TextChanged += new TextChangedEventHandler(textboxGroupingOfWindowTitles_TextChanged);

			DateTime systemStartupTime;
			TimeSpan idleTime;
			if (Win32Api.GetLastInputInfo(out systemStartupTime, out idleTime))
				labelSystemStartupTime.Content = "System startup time: " + systemStartupTime.ToString("yyyy-MM-dd HH:mm:ss");
		}

		public static void ShowReport(Dictionary<string, WindowsMonitor.WindowTimes> activatedWindowsReported)
		{
			ReportWindow rw = new ReportWindow();
			//rw.listBox1.Items.Clear();
			var tmplist = new ObservableCollection<WindowsMonitor.WindowTimes>(activatedWindowsReported.Values);
			int minsecs;
			if (!int.TryParse(rw.textboxMinimumSecondsToShow.Text, out minsecs))
				minsecs = 0;
			WindowsMonitor.PopulateList(ref tmplist, minsecs, GroupingWindowTitlesBySubstring);
			rw.listBox1.ItemsSource = tmplist;
			rw.originalUngroupedList = new ObservableCollection<WindowsMonitor.WindowTimes>(activatedWindowsReported.Values);
			rw.WindowState = WindowState.Maximized;
			rw.labelAllWindowsTotalSeconds.Content = "All total seconds: " + activatedWindowsReported.Sum(kv => kv.Value.TotalTimes.Sum(dateDur => dateDur.Value != DateTime.MinValue ? (dateDur.Value.Subtract(dateDur.Key).TotalSeconds) : 0));
			rw.labelAllWindowsTotalIdleSeconds.Content = "All idle total seconds: " + activatedWindowsReported.Sum(kv => kv.Value.IdleTimes.Sum(dateDur => dateDur.Value != DateTime.MinValue ? (dateDur.Value.Subtract(dateDur.Key).TotalSeconds) : 0));
			rw.ShowDialog();
		}

		private void textboxGroupingOfWindowTitles_TextChanged(object sender, TextChangedEventArgs e)
		{
			SaveGroupingOfWindowTitles();
			GetGroupingOfWindowTitles();
			var tmplist = new ObservableCollection<WindowsMonitor.WindowTimes>(originalUngroupedList);
			int minsecs;
			if (!int.TryParse(textboxMinimumSecondsToShow.Text, out minsecs))
				minsecs = 0;
			WindowsMonitor.PopulateList(ref tmplist, minsecs, GroupingWindowTitlesBySubstring);
			listBox1.ItemsSource = null;
			listBox1.ItemsSource = tmplist;
		}

		private string WindowTitleGroupingFilePath { get { return SettingsInterop.GetFullFilePathInLocalAppdata("WindowTitleGroupings.fjset", StartupManagerWindow.cThisAppName); } }
		public static string SelectedRecordingDeviceFilepath { get { return SettingsInterop.GetFullFilePathInLocalAppdata("SelectedRecordingDevice.fjset", StartupManagerWindow.cThisAppName); } }

		private void GetGroupingOfWindowTitles()
		{
			string filepath = WindowTitleGroupingFilePath;
			if (!File.Exists(filepath))
				File.Create(filepath).Close();
			var filetext = File.ReadAllText(filepath);
			textboxGroupingOfWindowTitles.Text = filetext;
			GroupingWindowTitlesBySubstring.Clear();
			GroupingWindowTitlesBySubstring = filetext.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();
		}
		bool isBusySaving = false;
		bool isSavingQueud = false;
		private void SaveGroupingOfWindowTitles()
		{
			if (isBusySaving)
			{
				isSavingQueud = true;
				return;
			}

			isBusySaving = true;
			File.WriteAllText(WindowTitleGroupingFilePath, textboxGroupingOfWindowTitles.Text);
			isBusySaving = false;
			if (isSavingQueud)
			{
				isSavingQueud = false;
				SaveGroupingOfWindowTitles();
			}
		}

		private void listBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			listBox1.SelectedItem = null;
		}

		private void Border_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
		{
			Border itemBorder = sender as Border;
			if (itemBorder == null) return;
			var windowTimes = itemBorder.DataContext as WindowsMonitor.WindowTimes;
			if (windowTimes == null) return;

			string fullpath = windowTimes.ProcessPath;
			if (fullpath == WindowsMonitor.cNullFilePath)
			{
				//Had a NULL for the module path (Skype does this, what other apps?)
			}
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			this.BringIntoView();
			this.Topmost = !this.Topmost;
			this.Topmost = !this.Topmost;
		}

		private void buttonLoad_Click(object sender, RoutedEventArgs e)
		{
			var reportsDir = System.IO.Path.GetDirectoryName(SettingsInterop.GetFullFilePathInLocalAppdata("", StartupManagerWindow.cThisAppName, "Reports"));
			string filepath = FileSystemInterop.SelectFile("Please select a json file to import", reportsDir, "Json files (*.json)|*.json");
			if (filepath != null)
			{
				int tmpMinSecs;
				if (!int.TryParse(textboxMinimumSecondsToShow.Text, out tmpMinSecs))
					tmpMinSecs = 0;
				ObservableCollection<WindowsMonitor.WindowTimes> winTimes = WindowsMonitor.LoadReportsFromJson(filepath, tmpMinSecs, GroupingWindowTitlesBySubstring);//@"C:\Users\francois\AppData\Local\FJH\HoursWorkedCalculator\Reports\2012_08_02 16_07_12\reportList.json");
				originalUngroupedList = new ObservableCollection<WindowsMonitor.WindowTimes>(winTimes);
				listBox1.ItemsSource = winTimes;
			}
		}

		public static string GetReportsJsonFilePath(string subfolder)
		{
			return SettingsInterop.GetFullFilePathInLocalAppdata("reportList.json", StartupManagerWindow.cThisAppName, subfolder);
		}

		public static string GetHtmlFilePath(string subfolder)
		{
			return SettingsInterop.GetFullFilePathInLocalAppdata("reports.html", StartupManagerWindow.cThisAppName, subfolder);
		}

		/*public static string GetRecordinsSaveToDirectory(string subfolder)
		{
			return SettingsInterop.LocalAppdataPath(MainWindow.ThisAppName + "\\" + subfolder + "\\recordings");
		}*/

		private void buttonSave_Click(object sender, RoutedEventArgs e)
		{
			string subfolder = "Reports\\" + DateTime.Now.ToString("yyyy_MM_dd HH_mm_ss");
			WindowsMonitor.SaveReportsToJsonAndHtmlAndRecordedWave(
				listBox1.ItemsSource as ObservableCollection<WindowsMonitor.WindowTimes>,
				GetReportsJsonFilePath(subfolder),
				GetHtmlFilePath(subfolder)/*
				GetRecordinsSaveToDirectory(subfolder)*/);
		}

		private string mPrevText = "";
		private bool mValidating;
		private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (mValidating) return;
			mValidating = true;
			try
			{
				int value = -1;
				if (textboxMinimumSecondsToShow.Text.Length > 0 &&
					!int.TryParse(textboxMinimumSecondsToShow.Text, out value))
				{
					textboxMinimumSecondsToShow.Text = mPrevText;
					textboxMinimumSecondsToShow.SelectionStart = mPrevText.Length;
				}
				else
				{
					mPrevText = textboxMinimumSecondsToShow.Text;
					if (textboxMinimumSecondsToShow.Text.Length == 0 ||
						originalUngroupedList != null)
					{
						var tmplist = new ObservableCollection<WindowsMonitor.WindowTimes>(originalUngroupedList);
						WindowsMonitor.PopulateList(ref tmplist, value, GroupingWindowTitlesBySubstring);
						listBox1.ItemsSource = tmplist;
					}
				}
			}
			finally
			{
				mValidating = false;
			}
		}
	}
}
