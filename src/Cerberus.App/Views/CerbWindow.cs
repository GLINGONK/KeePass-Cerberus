using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using Cerberus.App.Services;

namespace Cerberus.App.Views
{
	/// <summary>Base for every window: theme, capture protection, dark title bar.</summary>
	public class CerbWindow : Window
	{
		public CerbWindow()
		{
			SetResourceReference(StyleProperty, "CerbWindow");
			try { this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/app.ico")); }
			catch(Exception) { }
			this.SourceInitialized += delegate
			{
				Native.SetCaptureProtection(this, Settings.Current.PreventScreenCapture);
				Native.SetDarkTitleBar(this, App.IsDark);
			};
			this.KeyDown += (s, e) => { if(e.Key == Key.Escape && IsDialog) { DialogResult = false; } };
		}

		protected bool IsDialog { get; set; }

		public static Path MakeIcon(string strKey, double size = 16)
		{
			Path p = new Path();
			p.SetResourceReference(StyleProperty, "Icon");
			p.SetResourceReference(Path.DataProperty, strKey);
			p.Width = size; p.Height = size;
			return p;
		}

		public static UIElement IconText(string strIcon, string strText)
		{
			StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
			sp.Children.Add(MakeIcon(strIcon));
			if(!string.IsNullOrEmpty(strText))
				sp.Children.Add(new TextBlock { Text = strText, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
			return sp;
		}

		/// <summary>"Anti-capture d'écran" check box, synced everywhere.</summary>
		public static CheckBox CaptureCheckBox()
		{
			CheckBox cb = new CheckBox { Content = L.T("Anti-capture d'écran"), VerticalAlignment = VerticalAlignment.Center,
				ToolTip = L.T("Coché : cette application apparaît en noir dans les captures et partages d'écran.") };
			cb.IsChecked = Settings.Current.PreventScreenCapture;
			bool bSync = false;
			cb.Checked += delegate { if(!bSync) App.SetCaptureProtection(true); };
			cb.Unchecked += delegate { if(!bSync) App.SetCaptureProtection(false); };
			Action h = delegate { bSync = true; cb.IsChecked = Settings.Current.PreventScreenCapture; bSync = false; };
			App.CaptureChanged += h;
			cb.Unloaded += delegate { App.CaptureChanged -= h; };
			cb.Loaded += delegate { App.CaptureChanged -= h; App.CaptureChanged += h; h(); };
			return cb;
		}
	}

	/// <summary>Themed message box.</summary>
	public static class Dialog
	{
		public static bool Show(Window owner, string strTitle, string strText, string strOk, string strCancel, bool bDanger = false)
		{
			// Translation is idempotent: already translated text passes through unchanged.
			strTitle = L.T(strTitle); strText = L.T(strText); strOk = L.T(strOk);
			if(strCancel != null) strCancel = L.T(strCancel);
			CerbWindow w = new CerbWindow { Title = strTitle, SizeToContent = SizeToContent.WidthAndHeight,
				ResizeMode = ResizeMode.NoResize, WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
				ShowInTaskbar = owner == null, MaxWidth = 520 };
			if(owner != null && owner.IsVisible) w.Owner = owner;

			StackPanel sp = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
			sp.Children.Add(new TextBlock { Text = strTitle, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
			TextBlock tb = new TextBlock { Text = strText, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None, MaxWidth = 460 };
			tb.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			sp.Children.Add(tb);

			StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
			bool bResult = false;
			if(strCancel != null)
			{
				Button c = new Button { Content = strCancel, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
				c.Click += delegate { w.Close(); };
				btns.Children.Add(c);
			}
			Button ok = new Button { Content = strOk, MinWidth = 96, IsDefault = true };
			ok.SetResourceReference(FrameworkElement.StyleProperty, bDanger ? "DangerButton" : "AccentButton");
			ok.Click += delegate { bResult = true; w.Close(); };
			if(strCancel == null) ok.IsCancel = true;
			btns.Children.Add(ok);
			sp.Children.Add(btns);
			w.Content = sp;
			w.Loaded += delegate { ok.Focus(); };
			w.ShowDialog();
			return bResult;
		}

		public static void Info(Window owner, string strTitle, string strText)
		{
			Show(owner, strTitle, strText, "OK", null);
		}

		public static void Error(Window owner, string strTitle, string strText)
		{
			Show(owner, strTitle, strText, "OK", null);
		}

		public static bool Confirm(Window owner, string strTitle, string strText, string strOk = "Continuer", bool bDanger = false)
		{
			return Show(owner, strTitle, strText, strOk, L.T("Annuler"), bDanger);
		}
	}
}
