using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

using Cerberus.App.Services;
using Cerberus.App.Views;

namespace Cerberus.App
{
	public partial class App : Application
	{
		private static System.Threading.Mutex g_mutex;

		public static event Action CaptureChanged;
		public static event Action ThemeChanged;

		protected override void OnStartup(StartupEventArgs e)
		{
			base.OnStartup(e);
			this.DispatcherUnhandledException += OnUnhandled;

			Settings.Load();
			L.Init(Settings.Current.Language);
			ApplyTheme(Settings.Current.Theme);

			// Single instance per Windows session: two instances editing the same
			// database would overwrite each other's changes.
			bool bNew;
			g_mutex = new System.Threading.Mutex(true, "Local\\KeePassCerberus.Wpf.SingleInstance", out bNew);
			if(!bNew)
			{
				Dialog.Info(null, "KeePass Cerberus", L.T("KeePass Cerberus est déjà ouvert (voir la barre des tâches)."));
				Shutdown();
				return;
			}
			Session.Init();

			MainWindow w = new MainWindow();
			this.MainWindow = w;
			w.Show();

			string strArg = e.Args.FirstOrDefault(a => !a.StartsWith("-"));
			w.StartWith(strArg);
		}

		protected override void OnExit(ExitEventArgs e)
		{
			SafeClipboard.ClearIfOurs();
			Settings.Save();
			base.OnExit(e);
		}

		private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
		{
			e.Handled = true;
			try { Dialog.Error(Current.MainWindow, L.T("Erreur inattendue"), e.Exception.Message); }
			catch(Exception) { }
		}

		public static bool IsDark { get { return Settings.Current.Theme != "Light"; } }

		public static void ApplyTheme(string strTheme)
		{
			Settings.Current.Theme = (strTheme == "Light") ? "Light" : "Dark";
			var md = Current.Resources.MergedDictionaries;
			md[0] = new ResourceDictionary { Source = new Uri("Themes/" + Settings.Current.Theme + ".xaml", UriKind.Relative) };
			foreach(Window w in Current.Windows) Native.SetDarkTitleBar(w, IsDark);
			ThemeChanged?.Invoke();
		}

		public static void SetCaptureProtection(bool b)
		{
			Settings.Current.PreventScreenCapture = b;
			Settings.Save();
			foreach(Window w in Current.Windows) Native.SetCaptureProtection(w, b);
			CaptureChanged?.Invoke();
		}
	}
}
