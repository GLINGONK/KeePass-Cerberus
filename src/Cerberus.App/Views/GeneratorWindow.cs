using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Cerberus.App.Services;

namespace Cerberus.App.Views
{
	public sealed class GeneratorWindow : CerbWindow
	{
		private readonly TextBox m_out;
		private readonly Slider m_len;
		private readonly TextBlock m_lenText, m_entropy;
		private readonly CheckBox m_u, m_l, m_d, m_s, m_amb;
		private readonly ProgressBar m_bar;
		private char[] m_cur = new char[0];

		public char[] Result { get; private set; }

		public GeneratorWindow(bool bSelectable)
		{
			IsDialog = true;
			Title = L.T("Générateur de mots de passe");
			Width = 520; SizeToContent = SizeToContent.Height;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;
			Settings st = Settings.Current;

			StackPanel root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };
			root.Children.Add(new TextBlock { Text = L.T("Générateur"), Style = (Style)FindResource("Title") });
			root.Children.Add(new TextBlock { Text = L.T("Tirage uniforme avec le générateur cryptographique de KeePass."), Style = (Style)FindResource("Subtitle"), Margin = new Thickness(0, 4, 0, 14) });

			Grid g = new Grid();
			g.ColumnDefinitions.Add(new ColumnDefinition());
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			m_out = new TextBox { FontFamily = new FontFamily("Consolas"), FontSize = 17, MinHeight = 46, IsReadOnly = true };
			Button again = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcDice", 18), ToolTip = L.T("Regénérer"), Margin = new Thickness(6, 0, 0, 0), Width = 44, Height = 44 };
			Button copy = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcCopy", 18), ToolTip = L.T("Copier"), Width = 44, Height = 44 };
			Grid.SetColumn(again, 1); Grid.SetColumn(copy, 2);
			g.Children.Add(m_out); g.Children.Add(again); g.Children.Add(copy);
			root.Children.Add(g);

			Grid e = new Grid { Margin = new Thickness(0, 10, 0, 0) };
			e.ColumnDefinitions.Add(new ColumnDefinition());
			e.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			m_bar = new ProgressBar { Maximum = 160, VerticalAlignment = VerticalAlignment.Center };
			m_entropy = new TextBlock { Margin = new Thickness(12, 0, 0, 0), Width = 150 };
			m_entropy.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			Grid.SetColumn(m_entropy, 1);
			e.Children.Add(m_bar); e.Children.Add(m_entropy);
			root.Children.Add(e);

			root.Children.Add(new TextBlock { Text = L.T("LONGUEUR"), Style = (Style)FindResource("Section") });
			Grid lg = new Grid();
			lg.ColumnDefinitions.Add(new ColumnDefinition());
			lg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
			m_len = new Slider { Minimum = 4, Maximum = 128, Value = Math.Max(4, Math.Min(128, st.GenLength)), IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
			m_lenText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
			Grid.SetColumn(m_lenText, 1);
			lg.Children.Add(m_len); lg.Children.Add(m_lenText);
			root.Children.Add(lg);

			root.Children.Add(new TextBlock { Text = L.T("CARACTÈRES"), Style = (Style)FindResource("Section") });
			WrapPanel wp = new WrapPanel();
			m_u = Chk(wp, "A–Z", st.GenUpper); m_l = Chk(wp, "a–z", st.GenLower); m_d = Chk(wp, "0–9", st.GenDigits); m_s = Chk(wp, "!#$%…", st.GenSymbols);
			root.Children.Add(wp);
			m_amb = new CheckBox { Content = L.T("Exclure les caractères ambigus (I l 1 O 0…)"), IsChecked = st.GenExcludeAmbiguous };
			root.Children.Add(m_amb);

			DockPanel bar = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
			if(bSelectable)
			{
				Button use = new Button { Content = L.T("Utiliser"), Style = (Style)FindResource("AccentButton"), MinWidth = 110, IsDefault = true };
				Button cancel = new Button { Content = L.T("Annuler"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
				DockPanel.SetDock(use, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
				bar.Children.Add(use); bar.Children.Add(cancel);
				use.Click += delegate { Result = (char[])m_cur.Clone(); Save(); DialogResult = true; };
			}
			else
			{
				Button close = new Button { Content = L.T("Fermer"), MinWidth = 96, IsCancel = true };
				DockPanel.SetDock(close, Dock.Right);
				bar.Children.Add(close);
				close.Click += delegate { Save(); Close(); };
			}
			root.Children.Add(bar);
			Content = root;

			RoutedEventHandler regen = delegate { Generate(); };
			m_len.ValueChanged += delegate { Generate(); };
			foreach(CheckBox c in new[] { m_u, m_l, m_d, m_s, m_amb }) { c.Checked += regen; c.Unchecked += regen; }
			again.Click += regen;
			copy.Click += delegate { SafeClipboard.Copy(new string(m_cur)); };
			Closed += delegate { Array.Clear(m_cur, 0, m_cur.Length); m_out.Text = ""; };
			Generate();
		}

		private static CheckBox Chk(Panel p, string s, bool b)
		{
			CheckBox c = new CheckBox { Content = s, IsChecked = b, Margin = new Thickness(0, 4, 22, 4) };
			p.Children.Add(c);
			return c;
		}

		private void Generate()
		{
			int n = (int)m_len.Value;
			m_lenText.Text = n.ToString();
			bool u = m_u.IsChecked == true, l = m_l.IsChecked == true, d = m_d.IsChecked == true, s = m_s.IsChecked == true, amb = m_amb.IsChecked == true;
			if(!u && !l && !d && !s) { m_l.IsChecked = true; return; }
			Array.Clear(m_cur, 0, m_cur.Length);
			m_cur = PasswordGen.Generate(n, u, l, d, s, amb);
			m_out.Text = new string(m_cur);
			double bits = PasswordGen.Entropy(n, PasswordGen.Alphabet(u, l, d, s, amb).Length);
			m_bar.Value = Math.Min(bits, 160);
			m_bar.SetResourceReference(ForegroundProperty, bits < 50 ? "Danger" : bits < 80 ? "Warning" : "Success");
			m_entropy.Text = ((int)bits) + L.T(" bits d'entropie");
		}

		private void Save()
		{
			Settings st = Settings.Current;
			st.GenLength = (int)m_len.Value; st.GenUpper = m_u.IsChecked == true; st.GenLower = m_l.IsChecked == true;
			st.GenDigits = m_d.IsChecked == true; st.GenSymbols = m_s.IsChecked == true; st.GenExcludeAmbiguous = m_amb.IsChecked == true;
			Settings.Save();
		}
	}
}
