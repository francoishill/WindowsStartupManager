﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;
using SharedClasses;

namespace WindowsStartupManager
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		protected override void OnStartup(StartupEventArgs e)
		{
			//Application.Current.DispatcherUnhandledException += (s, ev) =>
			//{
			//	UserMessages.ShowErrorMessage("Unhandled: " + Environment.NewLine + ev.Exception.Message);
			//};

			SharedClasses.AutoUpdating.CheckForUpdates_ExceptionHandler();
			//SharedClasses.AutoUpdating.CheckForUpdates(null, null);

			base.OnStartup(e);
			var mw = new MainWindow();
			mw.ShowDialog();
		}
	}
}
