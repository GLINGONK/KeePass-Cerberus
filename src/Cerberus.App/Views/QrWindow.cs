using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

using Cerberus.App.Services;

using KeePassLib;

using QRCoder;

namespace Cerberus.App.Views
{
	/// <summary>
	/// Shows username / password as a QR code so a phone can read it.
	/// Nothing is written to disk; the window closes itself after a delay.
	/// </summary>
	public sealed class QrWindow : CerbWindow
	{
		private const int AutoCloseSeconds = 30;

		private readonly PwEntry m_pe;
		private readonly Image m_img;
		private readonly TextBlock m_caption, m_timer;
		private readonly DispatcherTimer m_tick;
		private int m_left = AutoCloseSeconds;

		public QrWindow(PwEntry pe)
		{
			m_pe = pe;
			IsDialog = true;
			Title = L.T("QR code");
			SizeToContent = SizeToContent.WidthAndHeight;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;

			StackPanel root = new StackPanel { Margin = new Thickness(26, 20, 26, 20), Width = 380 };
			root.Children.Add(new TextBlock { Text = L.T("Scanner avec le téléphone"), Style = (Style)FindResource("Title"), FontSize = 19 });
			root.Children.Add(new TextBlock { Text = pe.Strings.ReadSafe(PwDefs.TitleField), Style = (Style)FindResource("Subtitle"), Margin = new Thickness(0, 2, 0, 12) });

			// segmented choice
			StackPanel seg = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
			RadioButton rbPw = Seg(L.T("Mot de passe"), true), rbUser = Seg(L.T("Identifiant"), false), rbBoth = Seg(L.T("Les deux"), false);
			seg.Children.Add(rbPw); seg.Children.Add(rbUser); seg.Children.Add(rbBoth);
			root.Children.Add(seg);

			Border frame = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(14),
				HorizontalAlignment = HorizontalAlignment.Center, Width = 300, Height = 300 };
			m_img = new Image { Stretch = Stretch.Uniform };
			RenderOptions.SetBitmapScalingMode(m_img, BitmapScalingMode.NearestNeighbor);
			frame.Child = m_img;
			root.Children.Add(frame);

			m_caption = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
			m_caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			root.Children.Add(m_caption);

			TextBlock warn = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 12, 0, 0),
				Text = L.T("Le secret est visible à l'écran tant que cette fenêtre est ouverte : assurez-vous que personne ne regarde. Rien n'est enregistré ni envoyé.") };
			root.Children.Add(warn);

			DockPanel bar = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
			m_timer = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
			m_timer.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
			bar.Children.Add(m_timer);
			Button close = new Button { Content = L.T("Fermer"), MinWidth = 96, IsCancel = true, IsDefault = true };
			DockPanel.SetDock(close, Dock.Right);
			bar.Children.Add(close);
			close.Click += delegate { Close(); };
			root.Children.Add(bar);
			Content = root;

			rbPw.Checked += delegate { Show(0); };
			rbUser.Checked += delegate { Show(1); };
			rbBoth.Checked += delegate { Show(2); };

			m_tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			m_tick.Tick += delegate
			{
				--m_left;
				m_timer.Text = L.T("Fermeture automatique dans ") + m_left + " s";
				if(m_left <= 0) Close();
			};
			m_timer.Text = L.T("Fermeture automatique dans ") + m_left + " s";
			m_tick.Start();
			// Leaving the window (or the app) closes it, like a clipboard timeout.
			Deactivated += delegate { if(IsLoaded) Close(); };
			Closed += delegate { m_tick.Stop(); m_img.Source = null; };
			Loaded += delegate { Show(0); };
		}

		private RadioButton Seg(string text, bool isChecked)
		{
			RadioButton rb = new RadioButton { Content = text, GroupName = "qr", IsChecked = isChecked, Margin = new Thickness(8, 0, 8, 0) };
			return rb;
		}

		private void Show(int mode)
		{
			string user = m_pe.Strings.ReadSafe(PwDefs.UserNameField);
			string pw = m_pe.Strings.ReadSafe(PwDefs.PasswordField);
			string text;
			if(mode == 0) { text = pw; m_caption.Text = L.T("Mot de passe"); }
			else if(mode == 1) { text = user; m_caption.Text = L.T("Nom d'utilisateur"); }
			else { text = L.T("Identifiant : ") + user + "\n" + L.T("Mot de passe : ") + pw; m_caption.Text = L.T("Identifiant + mot de passe"); }

			if(string.IsNullOrEmpty(text)) { m_img.Source = null; m_caption.Text += L.T(" (vide)"); return; }
			m_img.Source = Render(text);
			m_left = AutoCloseSeconds; // restart the countdown on interaction
		}

		/// <summary>QR matrix (QRCoder) drawn as a vector image: no bitmap file, no clipboard.</summary>
		private static ImageSource Render(string text)
		{
			using(QRCodeGenerator gen = new QRCodeGenerator())
			using(QRCodeData data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M, true))
			{
				var m = data.ModuleMatrix;
				int n = m.Count;
				GeometryGroup gg = new GeometryGroup { FillRule = FillRule.Nonzero };
				for(int y = 0; y < n; ++y)
					for(int x = 0; x < n; ++x)
						if(m[y][x]) gg.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
				gg.Freeze();
				DrawingGroup dg = new DrawingGroup();
				dg.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, n, n))));
				dg.Children.Add(new GeometryDrawing(Brushes.Black, null, gg));
				dg.Freeze();
				return new DrawingImage(dg);
			}
		}
	}
}
