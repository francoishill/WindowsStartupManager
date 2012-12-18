using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		private void Application_Startup(object sender, StartupEventArgs e)
		{
			Application.Current.DispatcherUnhandledException += (s, ev) =>
			{
				UserMessages.ShowErrorMessage("Unhandled: " + Environment.NewLine + ev.Exception.Message);
			};

			SharedClasses.AutoUpdating.CheckForUpdates_ExceptionHandler();
			//SharedClasses.AutoUpdating.CheckForUpdates(null, null);
		}
	}
}
