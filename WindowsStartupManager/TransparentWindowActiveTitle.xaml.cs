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
using System.Threading;

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for TransparentWindowActiveTitle.xaml
	/// </summary>
	public partial class TransparentWindowActiveTitle : Window
	{
		private static TransparentWindowActiveTitle instanceWindow;
		private static Thread thread;

		public TransparentWindowActiveTitle()
		{
			InitializeComponent();
			this.MaxWidth = SystemParameters.WorkArea.Width - 200;
			this.MaxHeight = 20;
		}

		public static void ShowWindow()
		{
			thread = new Thread(() =>
			{
				if (instanceWindow == null)
					instanceWindow = new TransparentWindowActiveTitle();
				if (!instanceWindow.IsVisible)
					instanceWindow.Dispatcher.Invoke((Action)delegate { instanceWindow.ShowDialog(); });
				instanceWindow.Dispatcher.Invoke((Action)delegate
				{
					instanceWindow.BringIntoView();
					instanceWindow.Topmost = !instanceWindow.Topmost;
					instanceWindow.Topmost = !instanceWindow.Topmost;
				});
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
		}
		public static void UpdateText(string text)
		{
			/*if (instanceWindow == null)
				ShowWindow();
			while (instanceWindow == null) { }
			instanceWindow.Dispatcher.Invoke((Action)delegate
			{
				instanceWindow.textblock1.Text = text;
			});*/
		}
		public static void ForceClose()
		{
			try
			{
				if (thread != null && thread.IsAlive)
					thread.Abort();
			}
			catch { }
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			this.Top = 20;
			RepositionWindow();
		}

		private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			RepositionWindow();
		}

		private void RepositionWindow()
		{
			this.Left = (SystemParameters.WorkArea.Width - this.ActualWidth) / 2;
		}

		double? opacity1 = null;
		double opacity2 = 0.7;
		private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (!opacity1.HasValue)
				opacity1 = this.Opacity;
			if (this.Opacity != opacity1.Value)
				this.Opacity = opacity1.Value;
			else
				this.Opacity = opacity2;
		}
	}
}
