using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SharedClasses;
using System.Timers;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		//public const string ThisAppName = "HoursWorkedCalculator";  REMOVED THIS because we are merging this app into WindowsStartupManager
		StartupManagerWindow managerWindow;

		public MainWindow()
		{
			InitializeComponent();
			//StartPipeClient();
			//WindowMessagesInterop.InitializeClientMessages();

			DateTime systemStartupTime;
			TimeSpan idleTime;
			if (Win32Api.GetLastInputInfo(out systemStartupTime, out idleTime))
			{
				this.Title += " (system startup at " + systemStartupTime.ToString(@"HH:mm:ss \o\n yyyy-MM-dd") + ")";
				labelSystemStartupTime.Content = "System startup time: " + systemStartupTime.ToString("yyyy-MM-dd HH:mm:ss");
			}
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);
			HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
			source.AddHook(WndProc);
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			WindowMessagesInterop.MessageTypes mt;
			WindowMessagesInterop.ClientHandleMessage(msg, wParam, lParam, out mt);
			if (mt == WindowMessagesInterop.MessageTypes.Show)
			{
				this.Show();
				if (this.WindowState == System.Windows.WindowState.Minimized)
					this.WindowState = System.Windows.WindowState.Normal;
				bool tmptopmost = this.Topmost;
				this.Topmost = true;
				this.Topmost = tmptopmost;
				this.Activate();
				this.UpdateLayout();
			}
			else if (mt == WindowMessagesInterop.MessageTypes.Close)
			{
				this.Close();
			}
			else if (mt == WindowMessagesInterop.MessageTypes.Hide)
			{
				this.Hide();
			}
			return IntPtr.Zero;
		}

		WindowsMonitor monitor = null;
		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			monitor = new WindowsMonitor(
				TransparentWindowActiveTitle.UpdateText,
				(startup, idleduration) =>
				{
					Dispatcher.Invoke((Action)delegate { labelIdleDuration.Content = "Idle seconds: " + idleduration.TotalSeconds; });
				});

			managerWindow = new StartupManagerWindow();
			managerWindow.Show();
		}

		private void Button_Click(object sender, RoutedEventArgs e)
		{
			Dictionary<string, WindowsMonitor.WindowTimes> windowsActivatedToday;
			this.WindowState = System.Windows.WindowState.Minimized;
			if (monitor.StopAndGetReport(out windowsActivatedToday))
				ReportWindow.ShowReport(windowsActivatedToday);
			this.WindowState = System.Windows.WindowState.Normal;
			monitor.Restart();
		}

		private void Label_MouseDoubleClick_1(object sender, MouseButtonEventArgs e)
		{
			//Process.Start("cmd");
			//Process.Start(@"D:\Francois\Dev\VSprojects\HoursWorkedCalculator\HoursWorkedCalculator\bin\Debug\Command Prompt.lnk");
			//string currentDir = System.IO.Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]).TrimEnd('\\');
			//string batfilepath = currentDir + "\\tmp.bat";
			//System.IO.File.WriteAllText(batfilepath, "quser & pause");
			//Process cmdProc = Process.Start(batfilepath);
			//cmdProc.WaitForExit();
			//System.IO.File.Delete(batfilepath);
		}

		private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			//monitor.Stop();

			string subfolder = "Reports\\" + DateTime.Now.ToString("yyyy_MM_dd HH_mm_ss");
			Dictionary<string, WindowsMonitor.WindowTimes> windowsActivatedToday;
			if (monitor.StopAndGetReport(out windowsActivatedToday))
				WindowsMonitor.SaveReportsToJsonAndHtmlAndRecordedWave(
					new ObservableCollection<WindowsMonitor.WindowTimes>(windowsActivatedToday.Values),
					ReportWindow.GetReportsJsonFilePath(subfolder),
					ReportWindow.GetHtmlFilePath(subfolder)/*,
					WindowsMonitor.MustRecord ? ReportWindow.GetRecordinsSaveToDirectory(subfolder) : ""*/);

			TransparentWindowActiveTitle.ForceClose();
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
					new DisplayItem("Icon(s) obtained from", null)
				});
			}
			finally
			{
				this.Topmost = origTopmost;
			}
		}

		/*private void StartPipeClient()
		{
			NamedPipesInterop.NamedPipeClient pipeclient = NamedPipesInterop.NamedPipeClient.StartNewPipeClient(
				ActionOnError: (e) => { Console.WriteLine("Error occured: " + e.GetException().Message); },
				ActionOnMessageReceived: (m) =>
				{
					if (m.MessageType == PipeMessageTypes.AcknowledgeClientRegistration)
						Console.WriteLine("Client successfully registered.");
					else
					{
						if (m.MessageType == PipeMessageTypes.Show)
							Dispatcher.BeginInvoke((Action)delegate
							{
								this.Show();
								if (this.WindowState == System.Windows.WindowState.Minimized)
									this.WindowState = System.Windows.WindowState.Normal;
								bool tmptopmost = this.Topmost;
								this.Topmost = true;
								this.Topmost = tmptopmost;
								this.Activate();
								this.UpdateLayout();
							});
						else if (m.MessageType == PipeMessageTypes.Hide)
							Dispatcher.BeginInvoke((Action)delegate { this.Hide(); });
						else if (m.MessageType == PipeMessageTypes.Close)
							Dispatcher.BeginInvoke((Action)delegate { this.Close(); });
					}
				});
			this.Closed += delegate { if (pipeclient != null) pipeclient.ForceCancelRetryLoop = true; };
		}*/
	}

	[ValueConversion(typeof(double), typeof(string))]
	public class MinutesToHoursConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			double minutes = (double)value;
			string hoursdivided = ((int)Math.Floor((double)((int)minutes / 60))).ToString();
			string minutesmodulus = (((int)minutes % 60)).ToString();
			while (hoursdivided.Length < 2) hoursdivided = "0" + hoursdivided;
			while (minutesmodulus.Length < 2) minutesmodulus = "0" + minutesmodulus;
			string hours =  hoursdivided + "h" + minutesmodulus + "m";
			return hours;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return null;
		}
	}

	[ValueConversion(typeof(double), typeof(int))]
	public class DoubleToIntConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return (int)((double)value);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			double returnDouble;
			if (double.TryParse((string)value, out returnDouble))
				return returnDouble;
			MessageBox.Show("Cannot convert to double value: " + value + ", resetting to 0");
			return 0;
		}
	}

	[ValueConversion(typeof(double), typeof(string))]
	public class HoursDecimalToHoursAndMinutesConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			string hoursdivided = ((int)Math.Floor((double)value)).ToString();
			string minutesmodulus = (((int)(((double)value - Math.Floor((double)value)) * 60))).ToString();
			while (hoursdivided.Length < 2) hoursdivided = "0" + hoursdivided;
			while (minutesmodulus.Length < 2) minutesmodulus = "0" + minutesmodulus;
			string hours =  hoursdivided + "h" + minutesmodulus + "m";
			return hours;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			double returnDouble;
			if (double.TryParse((string)value, out returnDouble))
				return returnDouble;
			MessageBox.Show("Cannot convert to double value: " + value + ", resetting to 0");
			return 0;
		}
	}

	public class OuttimeIntimeOffhoursTOhoursworkedConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			//foreach (object obj in values)
			//	if (obj != null)
			//		MessageBox.Show(obj.ToString());
			if (values != null && values.Length == 3 && values[0] != null && values[1] != null && values[2] != null
				&& values[0] is DateTime && values[1] is double && values[2] is DateTime)
			{
				DateTime inTime = (DateTime)values[0];
				double minutesOff = (double)values[1];
				DateTime outTime = (DateTime)values[2];
				TimeSpan timeSpan = new TimeSpan(outTime.Ticks - inTime.Ticks).Add(-TimeSpan.FromMinutes(minutesOff));
				return timeSpan.TotalHours;
			}
			return (double)0;
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			return null;
			//return new object[] 
			//{
			//	DateTime.Now,
			//	5D,
			//	5D
			//};
		}
	}

	//public class IntimeWorkedhoursOffhoursTOOutTimeConverter : IMultiValueConverter
	//{
	//	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	//	{
	//		//foreach (object obj in values)
	//		//	if (obj != null)
	//		//		MessageBox.Show(obj.ToString());
	//		if (values != null && values.Length == 3 && values[0] != null && values[1] != null && values[2] != null
	//			&& values[0] is DateTime && values[1] is double && values[2] is double)
	//		{
	//			DateTime inTime = (DateTime)values[0];
	//			double workedHours = (double)values[1];
	//			double offHours = (double)values[2];
	//			return inTime.AddHours(workedHours + offHours);
	//			//TimeSpan timeSpan = new TimeSpan(outTime.Ticks - inTime.Ticks).Add(-TimeSpan.FromMinutes(minutesOff));
	//			//return timeSpan.TotalHours;
	//		}
	//		return new DateTime(0, 1, 1);
	//	}

	//	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	//	{
	//		return null;
	//		//return new object[] 
	//		//{
	//		//	DateTime.Now,
	//		//	5D,
	//		//	5D
	//		//};
	//	}
	//}
}
