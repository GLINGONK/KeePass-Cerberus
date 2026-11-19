using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using Cerberus.App.Controls;
using Cerberus.App.Services;

using KeePass.Cerberus;
using KeePassLib.Cryptography;
using KeePassLib.Utility;

namespace Cerberus.App.Views
{
	/// <summary>PIN / pattern / Shamir factors (same derivation as the legacy UI).</summary>
	public sealed class FactorsWindow : CerbWindow
	{
		private readonly bool m_bCreate;
		private readonly string m_strDb;
		private CheckBox m_cbPin, m_cbPat, m_cbSh;
		private PasswordBox m_pin, m_pin2;
		private ComboBox m_cmbGrid;
		private PatternPad m_pad, m_pad2;
		private TextBox m_tbK, m_tbN, m_tbDir;
		private TextBlock m_lblShares;
		private readonly List<ShamirShare> m_shares = new List<ShamirShare>();

		public byte[] ResultKey { get; private set; }
		public string ResultFlags { get; private set; }
		public int ResultGrid { get; private set; }

		public FactorsWindow(bool bCreate, string strDbPath, KeyAssoc hint)
		{
			m_bCreate = bCreate;
			m_strDb = strDbPath;
			IsDialog = true;
			Title = "Facteurs Cerberus";
			SizeToContent = SizeToContent.WidthAndHeight;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;

			// Per-database memory first, then the last configuration used (if enabled).
			Settings st = Settings.Current;
			string flags = "";
			int grid = 3;
			if(st.RememberFactorConfig)
			{
				flags = (hint != null && !string.IsNullOrEmpty(hint.FactorFlags)) ? hint.FactorFlags : (st.LastFactorFlags ?? "");
				grid = (hint != null && hint.GridSize >= 3 && !string.IsNullOrEmpty(hint.FactorFlags)) ? hint.GridSize : st.LastGridSize;
				grid = Math.Max(3, Math.Min(8, grid));
			}

			StackPanel root = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Width = 520 };
			root.Children.Add(new TextBlock { Text = "Facteurs Cerberus", Style = (Style)FindResource("Title") });
			root.Children.Add(new TextBlock { Style = (Style)FindResource("Subtitle"), Margin = new Thickness(0, 4, 0, 6),
				Text = bCreate ? L.T("Cochez les facteurs à ajouter. Tous seront exigés à chaque ouverture, en plus des autres éléments de la clé.")
				               : L.T("Fournissez exactement les mêmes facteurs que lors de la création de la clé.") });

			// PIN
			Border cardPin = Card(out StackPanel spPin);
			m_cbPin = new CheckBox { Content = Bold(L.T("Code PIN")), IsChecked = flags.Contains("P") };
			spPin.Children.Add(m_cbPin);
			m_pin = new PasswordBox { Margin = new Thickness(0, 8, 0, 0), MaxLength = 64 };
			spPin.Children.Add(m_pin);
			if(bCreate)
			{
				spPin.Children.Add(new TextBlock { Text = L.T("Confirmation"), Style = (Style)FindResource("FieldLabel") });
				m_pin2 = new PasswordBox { MaxLength = 64 };
				spPin.Children.Add(m_pin2);
			}
			root.Children.Add(cardPin);

			// Pattern
			Border cardPat = Card(out StackPanel spPat);
			DockPanel head = new DockPanel();
			m_cbPat = new CheckBox { Content = Bold(L.T("Motif")), IsChecked = flags.Contains("M") };
			head.Children.Add(m_cbPat);
			StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
			m_cmbGrid = new ComboBox { Width = 84, Margin = new Thickness(0, 0, 6, 0) };
			for(int g = 3; g <= 8; ++g) m_cmbGrid.Items.Add(g + " × " + g);
			m_cmbGrid.SelectedIndex = grid - 3;
			ToggleButton tgHide = new ToggleButton { Style = (Style)FindResource("ToggleIcon"), Content = MakeIcon("IcEye"), ToolTip = L.T("Masquer le tracé") };
			Button btnClear = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcClose"), ToolTip = L.T("Effacer") };
			tools.Children.Add(m_cmbGrid); tools.Children.Add(tgHide); tools.Children.Add(btnClear);
			DockPanel.SetDock(tools, Dock.Right);
			head.Children.Insert(0, tools);
			spPat.Children.Add(head);

			StackPanel pads = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
			m_pad = new PatternPad { GridSize = grid };
			pads.Children.Add(m_pad);
			if(bCreate)
			{
				m_pad2 = new PatternPad { GridSize = grid, Margin = new Thickness(16, 0, 0, 0) };
				pads.Children.Add(m_pad2);
			}
			spPat.Children.Add(pads);
			if(bCreate) spPat.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 8, 0, 0),
				Text = L.T("Dessinez le motif à gauche, puis répétez-le à droite.") });
			m_cmbGrid.SelectionChanged += delegate
			{
				m_pad.GridSize = m_cmbGrid.SelectedIndex + 3;
				if(m_pad2 != null) m_pad2.GridSize = m_cmbGrid.SelectedIndex + 3;
			};
			tgHide.Checked += delegate { m_pad.HideTrace = true; if(m_pad2 != null) m_pad2.HideTrace = true; };
			tgHide.Unchecked += delegate { m_pad.HideTrace = false; if(m_pad2 != null) m_pad2.HideTrace = false; };
			btnClear.Click += delegate { m_pad.Clear(); m_pad2?.Clear(); };
			root.Children.Add(cardPat);

			// Shamir
			Border cardSh = Card(out StackPanel spSh);
			m_cbSh = new CheckBox { Content = Bold(L.T("Partage de secret Shamir (K parmi N)")), IsChecked = flags.Contains("S") };
			spSh.Children.Add(m_cbSh);
			if(bCreate)
			{
				StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
				row.Children.Add(new TextBlock { Text = "K", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
				m_tbK = new TextBox { Text = "2", Width = 48 };
				row.Children.Add(m_tbK);
				row.Children.Add(new TextBlock { Text = "N", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
				m_tbN = new TextBox { Text = "3", Width = 48 };
				row.Children.Add(m_tbN);
				spSh.Children.Add(row);
				DockPanel dir = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
				Button btnDir = new Button { Content = L.T("Dossier…"), Margin = new Thickness(8, 0, 0, 0) };
				DockPanel.SetDock(btnDir, Dock.Right);
				dir.Children.Add(btnDir);
				m_tbDir = new TextBox { IsReadOnly = true };
				dir.Children.Add(m_tbDir);
				btnDir.Click += delegate
				{
					using(var fbd = new System.Windows.Forms.FolderBrowserDialog())
					{
						fbd.Description = L.T("Dossier où écrire les parts (à déplacer ensuite vers des supports séparés)");
						if(fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK) { m_tbDir.Text = fbd.SelectedPath; }
					}
				};
				spSh.Children.Add(dir);
				spSh.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0),
					Text = L.T("N fichiers .kpcshare seront créés ; K d'entre eux seront exigés à l'ouverture. Moins de K parts = base irrécupérable.") });
			}
			else
			{
				StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
				Button btnLoad = new Button { Content = L.T("Charger les parts…") };
				m_lblShares = new TextBlock { Text = L.T("Aucune part chargée."), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
				m_lblShares.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
				btnLoad.Click += OnLoadShares;
				row.Children.Add(btnLoad); row.Children.Add(m_lblShares);
				spSh.Children.Add(row);
			}
			root.Children.Add(cardSh);

			if(bCreate)
				root.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 12, 0, 0),
					Text = L.T("Le PIN et le motif sont des protections simples : ils complètent le mot de passe maître sans le remplacer. Aucune longueur minimale n'est imposée.") });

			CheckBox remember = new CheckBox { Content = L.T("Mémoriser cette configuration (grille, facteurs cochés) comme valeur par défaut"),
				IsChecked = st.RememberFactorConfig, Margin = new Thickness(0, 14, 0, 0) };
			remember.Checked += delegate { st.RememberFactorConfig = true; Settings.Save(); };
			remember.Unchecked += delegate { st.RememberFactorConfig = false; st.LastFactorFlags = ""; st.LastGridSize = 3; Settings.Save(); };
			root.Children.Add(remember);

			// Buttons
			DockPanel bar = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
			CheckBox cap = CaptureCheckBox();
			bar.Children.Add(cap);
			Button ok = new Button { Content = L.T("Valider"), Style = (Style)FindResource("AccentButton"), MinWidth = 110, IsDefault = true };
			Button cancel = new Button { Content = L.T("Annuler"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
			DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
			bar.Children.Add(ok); bar.Children.Add(cancel);
			ok.Click += OnOK;
			root.Children.Add(bar);

			Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = SystemParameters.WorkArea.Height - 60 };
			Loaded += delegate { if(m_cbPin.IsChecked == true) m_pin.Focus(); };
		}

		private Border Card(out StackPanel sp)
		{
			sp = new StackPanel();
			return new Border { Style = (Style)FindResource("Card"), Child = sp, Margin = new Thickness(0, 12, 0, 0) };
		}

		private static TextBlock Bold(string s) { return new TextBlock { Text = s, FontWeight = FontWeights.SemiBold }; }

		private void OnLoadShares(object sender, RoutedEventArgs e)
		{
			var ofd = new Microsoft.Win32.OpenFileDialog { Title = L.T("Sélectionner au moins K parts Shamir"),
				Filter = L.T("Parts Cerberus (*.kpcshare)|*.kpcshare|Tous les fichiers (*.*)|*.*"), Multiselect = true };
			if(ofd.ShowDialog(this) != true) return;
			m_shares.Clear();
			try
			{
				foreach(string f in ofd.FileNames) m_shares.Add(ShamirShare.Parse(File.ReadAllText(f, Encoding.UTF8)));
				int k = m_shares.Count > 0 ? m_shares[0].K : 0;
				m_lblShares.Text = m_shares.Count + L.T(" part(s) chargée(s), ") + k + L.T(" requise(s).");

			}
			catch(Exception ex)
			{
				m_shares.Clear();
				m_lblShares.Text = L.T("Aucune part chargée.");
				Dialog.Error(this, L.T("Parts Shamir"), ex.Message);
			}
		}

		private void OnOK(object sender, RoutedEventArgs e)
		{
			bool bPin = m_cbPin.IsChecked == true, bPat = m_cbPat.IsChecked == true, bSh = m_cbSh.IsChecked == true;
			if(!bPin && !bPat && !bSh) { Dialog.Info(this, L.T("Facteurs"), L.T("Cochez au moins un facteur.")); return; }

			byte[] pbPin = null, pbSecret = null;
			try
			{
				if(bPin)
				{
					pbPin = Secure.Utf8(m_pin.SecurePassword);
					if(pbPin.Length == 0) { Dialog.Info(this, "PIN", L.T("Saisissez le PIN (ou décochez ce facteur).")); return; }
					if(m_bCreate)
					{
						byte[] pb2 = Secure.Utf8(m_pin2.SecurePassword);
						bool eq = MemUtil.ArraysEqual(pbPin, pb2);
						MemUtil.ZeroByteArray(pb2);
						if(!eq) { Dialog.Info(this, "PIN", L.T("Les deux PIN ne correspondent pas.")); return; }
					}
				}
				int[] seq = null;
				if(bPat)
				{
					seq = m_pad.Sequence;
					if(seq.Length == 0) { Dialog.Info(this, L.T("Motif"), L.T("Dessinez le motif (ou décochez ce facteur).")); return; }
					if(m_bCreate && !MemUtil.ArraysEqual(CerberusFactors.PatternBytes(m_pad.GridSize, seq),
						CerberusFactors.PatternBytes(m_pad2.GridSize, m_pad2.Sequence)))
					{ Dialog.Info(this, L.T("Motif"), L.T("Les deux motifs ne correspondent pas.")); return; }
				}
				if(bSh)
				{
					pbSecret = m_bCreate ? CreateShares() : CombineShares();
					if(pbSecret == null) return;
				}

				ResultKey = CerberusFactors.Derive(pbPin, m_pad.GridSize, seq, pbSecret);
				ResultFlags = (bPin ? "P" : "") + (bPat ? "M" : "") + (bSh ? "S" : "");
				ResultGrid = m_pad.GridSize;
				if(Settings.Current.RememberFactorConfig)
				{
					Settings.Current.LastFactorFlags = ResultFlags;
					Settings.Current.LastGridSize = ResultGrid;
					Settings.Save();
				}
				DialogResult = true;
			}
			finally
			{
				if(pbPin != null) MemUtil.ZeroByteArray(pbPin);
				if(pbSecret != null) MemUtil.ZeroByteArray(pbSecret);
			}
		}

		private byte[] CombineShares()
		{
			if(m_shares.Count == 0) { Dialog.Info(this, "Shamir", L.T("Chargez les fichiers de parts Shamir.")); return null; }
			try { return Shamir.Combine(m_shares); }
			catch(Exception ex) { Dialog.Error(this, "Shamir", ex.Message); return null; }
		}

		private byte[] CreateShares()
		{
			int k, n;
			if(!int.TryParse(m_tbK.Text, out k) || !int.TryParse(m_tbN.Text, out n) || k < 2 || n < k || n > 16)
			{ Dialog.Info(this, "Shamir", L.T("Valeurs invalides : 2 ≤ K ≤ N ≤ 16.")); return null; }
			string dir = m_tbDir.Text;
			if(string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { Dialog.Info(this, "Shamir", L.T("Choisissez le dossier où écrire les parts.")); return null; }
			string strBase = Path.GetFileNameWithoutExtension(m_strDb ?? "base");
			if(string.IsNullOrEmpty(strBase)) strBase = "base";

			byte[] secret = CryptoRandom.Instance.GetRandomBytes(32);
			ShamirShare[] v = Shamir.Split(secret, k, n);
			List<string> written = new List<string>();
			try
			{
				for(int i = 0; i < n; ++i)
				{
					string f = Path.Combine(dir, strBase + ".part" + (i + 1) + "of" + n + ".kpcshare");
					if(File.Exists(f)) throw new IOException(L.T("Le fichier existe déjà : ") + f);
					File.WriteAllText(f, v[i].Serialize(), new UTF8Encoding(false));
					written.Add(f);
				}
			}
			catch(Exception ex)
			{
				foreach(string f in written) { try { File.Delete(f); } catch(Exception) { } }
				MemUtil.ZeroByteArray(secret);
				Dialog.Error(this, "Shamir", ex.Message);
				return null;
			}
			Dialog.Info(this, L.T("Parts Shamir créées"), n + L.T(" parts écrites dans :") + "\n" + dir + "\n" + "\n" + k +
				L.T(" d'entre elles seront exigées à chaque ouverture. Déplacez-les vers des supports SÉPARÉS (clés USB, coffre…)."));
			return secret;
		}
	}
}
