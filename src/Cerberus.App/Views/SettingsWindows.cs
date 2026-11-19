using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

using Cerberus.App.Services;

using KeePass.Cerberus;
using KeePassLib;
using KeePassLib.Cryptography.Cipher;
using KeePassLib.Cryptography.KeyDerivation;

namespace Cerberus.App.Views
{
	public sealed class DbSettingsWindow : CerbWindow
	{
		private readonly PwDatabase m_db;
		private readonly TextBox m_name, m_desc, m_mem, m_it, m_par, m_hist;
		private readonly ComboBox[] m_layers = new ComboBox[4];
		private readonly ComboBox m_kdf;
		private readonly CheckBox m_bin;
		private readonly TextBlock m_summary;

		private static readonly string[] LayerNames = { "—", "AES-256", "ChaCha20", "Serpent-256", "Twofish-256", "Camellia-256" };

		public DbSettingsWindow(PwDatabase db)
		{
			m_db = db;
			IsDialog = true;
			Title = "Paramètres de la base";
			Width = 620; SizeToContent = SizeToContent.Height;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;

			StackPanel root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
			root.Children.Add(new TextBlock { Text = "Paramètres de la base", Style = (Style)FindResource("Title") });
			TabControl tabs = new TabControl { Margin = new Thickness(0, 14, 0, 0) };
			root.Children.Add(tabs);

			// General
			StackPanel g = new StackPanel();
			g.Children.Add(Lbl("Nom de la base"));
			m_name = new TextBox { Text = db.Name };
			g.Children.Add(m_name);
			g.Children.Add(Lbl("Description"));
			m_desc = new TextBox { Text = db.Description, Style = (Style)FindResource("MultilineBox"), Height = 80 };
			g.Children.Add(m_desc);
			m_bin = new CheckBox { Content = "Utiliser la corbeille", IsChecked = db.RecycleBinEnabled, Margin = new Thickness(0, 14, 0, 0) };
			g.Children.Add(m_bin);
			g.Children.Add(Lbl("Nombre maximal de versions dans l'historique de chaque entrée"));
			m_hist = new TextBox { Text = db.HistoryMaxItems.ToString(), Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
			g.Children.Add(m_hist);
			tabs.Items.Add(new TabItem { Header = "Général", Content = g });

			// Security
			StackPanel s = new StackPanel();
			s.Children.Add(new TextBlock { Text = "PROFILS CERBERUS", Style = (Style)FindResource("Section"), Margin = new Thickness(0, 0, 0, 6) });
			WrapPanel wp = new WrapPanel();
			foreach(var p in Session.Profiles)
			{
				Button b = new Button { Content = p.Name, Margin = new Thickness(0, 0, 8, 8), ToolTip = p.Description };
				var prof = p;
				b.Click += delegate { ApplyProfileToUi(prof); };
				wp.Children.Add(b);
			}
			s.Children.Add(wp);

			s.Children.Add(new TextBlock { Text = "CHIFFREMENT (CASCADE DE 1 À 4 COUCHES)", Style = (Style)FindResource("Section") });
			UniformGrid ug = new UniformGrid { Columns = 4 };
			for(int i = 0; i < 4; ++i)
			{
				m_layers[i] = new ComboBox { Margin = new Thickness(0, 0, i < 3 ? 8 : 0, 0) };
				foreach(string n in LayerNames) m_layers[i].Items.Add(n);
				m_layers[i].SelectionChanged += delegate { UpdateSummary(); };
				ug.Children.Add(m_layers[i]);
			}
			s.Children.Add(ug);
			s.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0),
				Text = "Couche 1 = la plus interne. « AES-256 » seul reste lisible par le KeePass d'origine ; toute autre combinaison n'est lisible que par KeePass Cerberus." });

			s.Children.Add(new TextBlock { Text = "DÉRIVATION DE CLÉ", Style = (Style)FindResource("Section") });
			m_kdf = new ComboBox();
			m_kdf.Items.Add("Argon2id (recommandé)"); m_kdf.Items.Add("Argon2d"); m_kdf.Items.Add("AES-KDF (ancien)");
			s.Children.Add(m_kdf);
			UniformGrid kg = new UniformGrid { Columns = 3, Margin = new Thickness(0, 6, 0, 0) };
			StackPanel k1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }, k2 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }, k3 = new StackPanel();
			k1.Children.Add(Lbl("Mémoire (Mio)")); m_mem = new TextBox(); k1.Children.Add(m_mem);
			k2.Children.Add(Lbl("Itérations")); m_it = new TextBox(); k2.Children.Add(m_it);
			k3.Children.Add(Lbl("Parallélisme")); m_par = new TextBox(); k3.Children.Add(m_par);
			kg.Children.Add(k1); kg.Children.Add(k2); kg.Children.Add(k3);
			s.Children.Add(kg);
			m_kdf.SelectionChanged += delegate { bool a2 = m_kdf.SelectedIndex < 2; m_mem.IsEnabled = m_par.IsEnabled = a2; UpdateSummary(); };
			m_mem.TextChanged += delegate { UpdateSummary(); };
			m_it.TextChanged += delegate { UpdateSummary(); };
			m_par.TextChanged += delegate { UpdateSummary(); };

			Border sum = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12) };
			m_summary = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None };
			sum.Child = m_summary;
			s.Children.Add(sum);
			tabs.Items.Add(new TabItem { Header = "Sécurité", Content = s });

			DockPanel bar = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
			Button ok = new Button { Content = "Enregistrer", Style = (Style)FindResource("AccentButton"), MinWidth = 120, IsDefault = true };
			Button cancel = new Button { Content = "Annuler", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
			DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
			bar.Children.Add(ok); bar.Children.Add(cancel);
			root.Children.Add(bar);
			ok.Click += delegate { if(Apply()) DialogResult = true; };
			Content = root;

			LoadFromDb();
		}

		private TextBlock Lbl(string s) { return new TextBlock { Text = s, Style = (Style)FindResource("FieldLabel") }; }

		private void LoadFromDb()
		{
			CascadeLayer[] v;
			if(m_db.DataCipherUuid.Equals(StandardAesEngine.AesUuid)) v = new[] { CascadeLayer.Aes };
			else if(m_db.DataCipherUuid.Equals(new ChaCha20Engine().CipherUuid)) v = new[] { CascadeLayer.ChaCha20 };
			else
			{
				CascadeCipherEngine c = CascadeCipherEngine.FromUuid(m_db.DataCipherUuid);
				v = (c != null) ? c.Layers : new[] { CascadeLayer.Aes };
			}
			for(int i = 0; i < 4; ++i) m_layers[i].SelectedIndex = (i < v.Length) ? (int)v[i] : 0;

			KdfParameters p = m_db.KdfParameters;
			KdfEngine k = KdfPool.Get(p.KdfUuid);
			Argon2Kdf a = k as Argon2Kdf;
			if(a != null)
			{
				m_kdf.SelectedIndex = (a.Uuid.Equals(new Argon2Kdf(Argon2Type.ID).Uuid)) ? 0 : 1;
				m_mem.Text = (p.GetUInt64(Argon2Kdf.ParamMemory, 64UL << 20) >> 20).ToString();
				m_it.Text = p.GetUInt64(Argon2Kdf.ParamIterations, 2).ToString();
				m_par.Text = p.GetUInt32(Argon2Kdf.ParamParallelism, 2).ToString();
			}
			else
			{
				m_kdf.SelectedIndex = 2;
				m_it.Text = p.GetUInt64(AesKdf.ParamRounds, 60000).ToString();
				m_mem.Text = "0"; m_par.Text = "1";
			}
			UpdateSummary();
		}

		private void ApplyProfileToUi(Session.Profile p)
		{
			CascadeLayer[] v = p.Layers ?? new[] { CascadeLayer.Aes };
			for(int i = 0; i < 4; ++i) m_layers[i].SelectedIndex = (i < v.Length) ? (int)v[i] : 0;
			m_kdf.SelectedIndex = 0;
			m_mem.Text = p.MemoryMiB.ToString(); m_it.Text = p.Iterations.ToString(); m_par.Text = p.Parallelism.ToString();
		}

		private CascadeLayer[] Layers()
		{
			return m_layers.Where(c => c.SelectedIndex > 0).Select(c => (CascadeLayer)c.SelectedIndex).ToArray();
		}

		private void UpdateSummary()
		{
			if(m_summary == null) return;
			CascadeLayer[] v = Layers();
			string strC = v.Length == 0 ? "aucun chiffrement choisi" : string.Join(" → ", v.Select(CascadeCipherEngine.LayerName));
			bool bCompat = v.Length == 1 && (v[0] == CascadeLayer.Aes) && m_kdf.SelectedIndex != 2 ? true : (v.Length == 1 && v[0] == CascadeLayer.Aes);
			ulong mem; ulong.TryParse(m_mem.Text, out mem);
			m_summary.Text = "Chiffrement : " + strC + "\nDérivation : " + (m_kdf.SelectedItem ?? "") +
				(m_kdf.SelectedIndex < 2 ? " · " + mem + " Mio · " + m_it.Text + " passes · " + m_par.Text + " threads" : " · " + m_it.Text + " tours") +
				"\n" + (bCompat ? "Compatible avec le KeePass d'origine." : "Lisible uniquement par KeePass Cerberus.") +
				(mem >= 2048 ? "\nAttention : " + (mem / 1024) + " Gio de RAM seront nécessaires à chaque ouverture." : "");
		}

		private bool Apply()
		{
			CascadeLayer[] v = Layers();
			if(v.Length == 0) { Dialog.Info(this, "Chiffrement", "Choisissez au moins une couche de chiffrement."); return false; }
			if(v.Distinct().Count() != v.Length) { Dialog.Info(this, "Chiffrement", "Un même algorithme ne peut pas apparaître deux fois."); return false; }
			ulong mem, it; uint par; int hist;
			if(!ulong.TryParse(m_it.Text, out it) || it < 1) { Dialog.Info(this, "Dérivation", "Nombre d'itérations invalide."); return false; }
			if(!int.TryParse(m_hist.Text, out hist)) hist = 10;

			PwUuid puCipher;
			if(v.Length == 1 && v[0] == CascadeLayer.Aes) puCipher = StandardAesEngine.AesUuid;
			else if(v.Length == 1 && v[0] == CascadeLayer.ChaCha20) puCipher = new ChaCha20Engine().CipherUuid;
			else puCipher = new CascadeCipherEngine(v).CipherUuid;

			KdfParameters kp;
			if(m_kdf.SelectedIndex == 2)
			{
				kp = new AesKdf().GetDefaultParameters();
				kp.SetUInt64(AesKdf.ParamRounds, it);
			}
			else
			{
				if(!ulong.TryParse(m_mem.Text, out mem) || mem < 8 || mem > 4096) { Dialog.Info(this, "Dérivation", "Mémoire entre 8 et 4096 Mio."); return false; }
				if(!uint.TryParse(m_par.Text, out par) || par < 1 || par > 16) { Dialog.Info(this, "Dérivation", "Parallélisme entre 1 et 16."); return false; }
				if(mem >= 2048 && !Dialog.Confirm(this, "Mémoire élevée", mem / 1024 + " Gio de RAM libre seront nécessaires à chaque ouverture et enregistrement. Continuer ?")) return false;
				kp = new Argon2Kdf(m_kdf.SelectedIndex == 0 ? Argon2Type.ID : Argon2Type.D).GetDefaultParameters();
				kp.SetUInt64(Argon2Kdf.ParamMemory, mem << 20);
				kp.SetUInt64(Argon2Kdf.ParamIterations, it);
				kp.SetUInt32(Argon2Kdf.ParamParallelism, par);
			}

			m_db.Name = m_name.Text; m_db.NameChanged = DateTime.UtcNow;
			m_db.Description = m_desc.Text; m_db.DescriptionChanged = DateTime.UtcNow;
			m_db.RecycleBinEnabled = m_bin.IsChecked == true;
			m_db.HistoryMaxItems = hist;
			m_db.DataCipherUuid = puCipher;
			m_db.KdfParameters = kp;
			return true;
		}
	}

	public sealed class OptionsWindow : CerbWindow
	{
		public OptionsWindow()
		{
			IsDialog = true;
			Title = "Options";
			Width = 560; SizeToContent = SizeToContent.Height;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;
			Settings st = Settings.Current;

			StackPanel root = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
			root.Children.Add(new TextBlock { Text = "Options", Style = (Style)FindResource("Title") });

			root.Children.Add(new TextBlock { Text = "APPARENCE", Style = (Style)FindResource("Section") });
			StackPanel th = new StackPanel { Orientation = Orientation.Horizontal };
			RadioButton dark = new RadioButton { Content = "Sombre", IsChecked = App.IsDark, Margin = new Thickness(0, 0, 24, 0), GroupName = "t" };
			RadioButton light = new RadioButton { Content = "Clair", IsChecked = !App.IsDark, GroupName = "t" };
			dark.Checked += delegate { App.ApplyTheme("Dark"); Settings.Save(); };
			light.Checked += delegate { App.ApplyTheme("Light"); Settings.Save(); };
			th.Children.Add(dark); th.Children.Add(light);
			root.Children.Add(th);

			root.Children.Add(new TextBlock { Text = "SÉCURITÉ", Style = (Style)FindResource("Section") });
			root.Children.Add(CaptureCheckBox());
			CheckBox lockSess = new CheckBox { Content = "Verrouiller quand la session Windows est verrouillée ou mise en veille", IsChecked = st.LockOnSessionLock };
			CheckBox lockMin = new CheckBox { Content = "Verrouiller à la réduction de la fenêtre", IsChecked = st.LockOnMinimize };
			CheckBox remember = new CheckBox { Content = "Mémoriser les éléments de clé utilisés (jamais les secrets)", IsChecked = st.RememberKeySources };
			CheckBox rememberFactors = new CheckBox { Content = "Mémoriser la configuration des facteurs (taille de grille, facteurs cochés)", IsChecked = st.RememberFactorConfig };
			root.Children.Add(rememberFactors);
			CheckBox autosave = new CheckBox { Content = "Enregistrer automatiquement après chaque modification", IsChecked = st.AutoSave };
			root.Children.Add(lockSess); root.Children.Add(lockMin); root.Children.Add(remember); root.Children.Add(autosave);

			UniformGrid ug = new UniformGrid { Columns = 2, Margin = new Thickness(0, 6, 0, 0) };
			StackPanel a = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }, b = new StackPanel();
			a.Children.Add(new TextBlock { Text = "Verrouillage après inactivité (min, 0 = jamais)", Style = (Style)FindResource("FieldLabel") });
			TextBox tbLock = new TextBox { Text = st.AutoLockMinutes.ToString() };
			a.Children.Add(tbLock);
			b.Children.Add(new TextBlock { Text = "Effacement du presse-papiers (s, 0 = jamais)", Style = (Style)FindResource("FieldLabel") });
			TextBox tbClip = new TextBox { Text = st.ClipboardClearSeconds.ToString() };
			b.Children.Add(tbClip);
			ug.Children.Add(a); ug.Children.Add(b);
			root.Children.Add(ug);

			if(!Native.IsWindows10OrNewer)
			{
				TextBlock warn = new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 14, 0, 0),
					Text = "Votre version de Windows ne reçoit plus de mises à jour de sécurité de Microsoft. KeePass Cerberus fonctionne, mais le système reste exposé : une mise à niveau vers Windows 10 ou 11 est recommandée." };
				warn.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
				root.Children.Add(warn);
			}

			DockPanel bar = new DockPanel { Margin = new Thickness(0, 20, 0, 0), LastChildFill = false };
			Button ok = new Button { Content = "Enregistrer", Style = (Style)FindResource("AccentButton"), MinWidth = 120, IsDefault = true };
			Button cancel = new Button { Content = "Fermer", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
			DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
			bar.Children.Add(ok); bar.Children.Add(cancel);
			root.Children.Add(bar);
			ok.Click += delegate
			{
				int n;
				st.LockOnSessionLock = lockSess.IsChecked == true;
				st.LockOnMinimize = lockMin.IsChecked == true;
				st.RememberKeySources = remember.IsChecked == true;
				if(!st.RememberKeySources) st.KeySources.Clear();
				st.RememberFactorConfig = rememberFactors.IsChecked == true;
				if(!st.RememberFactorConfig) { st.LastFactorFlags = ""; st.LastGridSize = 3; }
				st.AutoSave = autosave.IsChecked == true;
				if(int.TryParse(tbLock.Text, out n) && n >= 0) st.AutoLockMinutes = n;
				if(int.TryParse(tbClip.Text, out n) && n >= 0) st.ClipboardClearSeconds = n;
				Settings.Save();
				DialogResult = true;
			};
			Content = root;
		}
	}
}
