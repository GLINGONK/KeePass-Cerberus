using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Cerberus.App.Services;

using KeePass.Cerberus;
using KeePassLib;
using KeePassLib.Cryptography;
using KeePassLib.Keys;
using KeePassLib.Utility;

namespace Cerberus.App.Views
{
	/// <summary>Shared layout for the key dialogs.</summary>
	public abstract class KeyWindowBase : CerbWindow
	{
		protected readonly StackPanel Body;
		protected readonly DockPanel Bar;
		protected readonly Grid Overlay;
		private readonly TextBlock m_overlayText;

		protected KeyWindowBase(string strTitle, string strSubtitle, string strIcon)
		{
			IsDialog = true;
			Title = "KeePass Cerberus — " + strTitle;
			SizeToContent = SizeToContent.Height;
			Width = 520;
			ResizeMode = ResizeMode.NoResize;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;

			Grid host = new Grid();
			StackPanel root = new StackPanel();

			// Header
			Border head = new Border { Padding = new Thickness(26, 22, 26, 18), BorderThickness = new Thickness(0, 0, 0, 1) };
			head.SetResourceReference(Border.BackgroundProperty, "Panel");
			head.SetResourceReference(Border.BorderBrushProperty, "Border");
			DockPanel hd = new DockPanel();
			Border ic = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 16, 0) };
			ic.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
			var p = MakeIcon(strIcon, 22);
			p.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
			ic.Child = p;
			hd.Children.Add(ic);
			StackPanel t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			t.Children.Add(new TextBlock { Text = strTitle, FontSize = 20, FontWeight = FontWeights.SemiBold });
			TextBlock sub = new TextBlock { Text = strSubtitle, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = strSubtitle };
			sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			t.Children.Add(sub);
			hd.Children.Add(t);
			head.Child = hd;
			root.Children.Add(head);

			Body = new StackPanel { Margin = new Thickness(26, 8, 26, 0) };
			root.Children.Add(Body);

			Bar = new DockPanel { Margin = new Thickness(26, 22, 26, 22), LastChildFill = false };
			Bar.Children.Add(CaptureCheckBox());
			root.Children.Add(Bar);

			host.Children.Add(root);

			// Busy overlay (KDF can take seconds)
			Overlay = new Grid { Visibility = Visibility.Collapsed };
			Overlay.SetResourceReference(Panel.BackgroundProperty, "Overlay");
			StackPanel ov = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
			ov.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 220, Margin = new Thickness(0, 0, 0, 12) });
			m_overlayText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 14 };
			ov.Children.Add(m_overlayText);
			Overlay.Children.Add(ov);
			host.Children.Add(Overlay);

			Content = host;
		}

		protected void Busy(string strText)
		{
			m_overlayText.Text = strText ?? "";
			Overlay.Visibility = strText == null ? Visibility.Collapsed : Visibility.Visible;
		}

		protected TextBlock Section(string s)
		{
			TextBlock tb = new TextBlock { Text = s.ToUpperInvariant(), Style = (Style)FindResource("Section") };
			Body.Children.Add(tb);
			return tb;
		}

		protected TextBlock Hint(string s, Thickness? m = null)
		{
			TextBlock tb = new TextBlock { Text = s, Style = (Style)FindResource("Hint"), Margin = m ?? new Thickness(28, 0, 0, 6) };
			Body.Children.Add(tb);
			return tb;
		}

		/// <summary>Password box with reveal toggle.</summary>
		protected PasswordBox PasswordRow(out ToggleButton tgReveal, out TextBox tbPlain)
		{
			Grid g = new Grid();
			g.ColumnDefinitions.Add(new ColumnDefinition());
			g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			PasswordBox pb = new PasswordBox { FontSize = 15, MinHeight = 40 };
			TextBox plain = new TextBox { FontSize = 15, MinHeight = 40, Visibility = Visibility.Collapsed, FontFamily = new FontFamily("Consolas") };
			ToggleButton tg = new ToggleButton { Style = (Style)FindResource("ToggleIcon"), Content = MakeIcon("IcEye", 18), Margin = new Thickness(6, 0, 0, 0),
				Width = 40, Height = 40, ToolTip = L.T("Afficher / masquer"), Focusable = false };
			tg.Checked += delegate { plain.Text = pb.Password; plain.Visibility = Visibility.Visible; pb.Visibility = Visibility.Collapsed; };
			tg.Unchecked += delegate { pb.Password = plain.Text; plain.Text = ""; plain.Visibility = Visibility.Collapsed; pb.Visibility = Visibility.Visible; };
			Grid.SetColumn(tg, 1);
			g.Children.Add(pb); g.Children.Add(plain); g.Children.Add(tg);
			Body.Children.Add(g);
			Closed += delegate { plain.Text = ""; pb.Clear(); }; // do not leave the secret in the visual tree
			tgReveal = tg; tbPlain = plain;
			return pb;
		}

		/// <summary>Current password from a reveal-able row (UTF-8, caller zeroes).</summary>
		protected static byte[] ReadPw(PasswordBox pb, ToggleButton tg, TextBox plain)
		{
			if(tg.IsChecked == true) pb.Password = plain.Text;
			return Secure.Utf8(pb.SecurePassword);
		}

		protected CheckBox Option(string strText, bool b)
		{
			CheckBox cb = new CheckBox { Content = strText, IsChecked = b, Margin = new Thickness(0, 8, 0, 2) };
			Body.Children.Add(cb);
			return cb;
		}

		protected TextBox FileRow(params Button[] buttons)
		{
			DockPanel d = new DockPanel { Margin = new Thickness(28, 4, 0, 2) };
			for(int i = buttons.Length - 1; i >= 0; --i)
			{
				buttons[i].Margin = new Thickness(6, 0, 0, 0);
				DockPanel.SetDock(buttons[i], Dock.Right);
				d.Children.Add(buttons[i]);
			}
			TextBox tb = new TextBox();
			d.Children.Add(tb);
			Body.Children.Add(d);
			return tb;
		}

		protected Button AddButtons(string strOk, out Button cancel)
		{
			Button ok = new Button { Content = strOk, Style = (Style)FindResource("AccentButton"), MinWidth = 130, IsDefault = true };
			cancel = new Button { Content = L.T("Annuler"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
			DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
			Bar.Children.Add(ok); Bar.Children.Add(cancel);
			return ok;
		}

		protected static string BrowseKeyFile(Window owner, string strCur)
		{
			var ofd = new Microsoft.Win32.OpenFileDialog { Title = L.T("Choisir le fichier clé"),
				Filter = L.T("Fichiers clés (*.keyx;*.key)|*.keyx;*.key|Tous les fichiers (*.*)|*.*"), CheckFileExists = true };
			try { if(!string.IsNullOrEmpty(strCur)) ofd.InitialDirectory = Path.GetDirectoryName(strCur); } catch(Exception) { }
			return ofd.ShowDialog(owner) == true ? ofd.FileName : null;
		}

		/// <summary>Composite key in KeePass order: password, key file, Cerberus factors, Windows account.</summary>
		protected static CompositeKey BuildKey(byte[] pbPw, string strKeyFile, byte[] pbFactors, bool bAccount, bool bNewKey)
		{
			CompositeKey ck = new CompositeKey();
			if(pbPw != null) ck.AddUserKey(new KcpPassword(pbPw, false));
			if(!string.IsNullOrEmpty(strKeyFile)) ck.AddUserKey(new KcpKeyFile(strKeyFile, bNewKey));
			if(pbFactors != null) ck.AddUserKey(CerberusFactors.ToUserKey(pbFactors));
			if(bAccount) ck.AddUserKey(new KcpUserAccount());
			return ck;
		}
	}

	/// <summary>Unlock an existing database.</summary>
	public sealed class UnlockWindow : KeyWindowBase
	{
		private readonly string m_strPath;
		private readonly PasswordBox m_pw;
		private readonly ToggleButton m_tgPw;
		private readonly TextBox m_pwPlain, m_tbKey;
		private readonly CheckBox m_cbPw, m_cbKey, m_cbFactors, m_cbAccount;
		private readonly TextBlock m_caps, m_error;
		private readonly KeyAssoc m_assoc;

		public PwDatabase Result { get; private set; }

		public UnlockWindow(string strPath)
			: base(L.T("Déverrouiller la base"), strPath, "IcLock")
		{
			m_strPath = strPath;
			m_assoc = Settings.Current.GetKeyAssoc(strPath);

			// Explicit, never ticked automatically; remembered per database.
			m_cbPw = Option(L.T("Mot de passe maître"), m_assoc == null || m_assoc.UsePassword);
			m_cbPw.FontWeight = FontWeights.SemiBold;
			m_cbPw.Margin = new Thickness(0, 14, 0, 6);
			m_pw = PasswordRow(out m_tgPw, out m_pwPlain);
			Action syncPw = delegate { bool b = m_cbPw.IsChecked == true; m_pw.IsEnabled = m_pwPlain.IsEnabled = m_tgPw.IsEnabled = b; };
			m_cbPw.Checked += delegate { syncPw(); };
			m_cbPw.Unchecked += delegate { syncPw(); };
			syncPw();
			m_caps = new TextBlock { Text = L.T("Verr. Maj est activé"), Margin = new Thickness(2, 6, 0, 0), Visibility = Visibility.Collapsed };
			m_caps.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
			Body.Children.Add(m_caps);

			Section(L.T("Autres éléments de la clé"));
			m_cbKey = Option(L.T("Fichier clé"), m_assoc != null && !string.IsNullOrEmpty(m_assoc.KeyFilePath));
			Button browse = new Button { Content = L.T("Parcourir…") };
			m_tbKey = FileRow(browse);
			m_tbKey.Text = m_assoc?.KeyFilePath ?? "";
			browse.Click += delegate { string s = BrowseKeyFile(this, m_tbKey.Text); if(s != null) m_tbKey.Text = s; };
			m_cbFactors = Option(L.T("Facteurs Cerberus (PIN / motif / Shamir)"), m_assoc != null && m_assoc.CerberusFactors);
			m_cbAccount = Option(L.T("Compte utilisateur Windows"), m_assoc != null && m_assoc.WindowsAccount);

			m_error = new TextBlock { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
			m_error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
			Body.Children.Add(m_error);

			Button ok = AddButtons(L.T("Déverrouiller"), out _);
			ok.Click += OnUnlock;

			PreviewKeyDown += delegate { UpdateCaps(); };
			Loaded += delegate { if(m_cbPw.IsChecked == true) m_pw.Focus(); UpdateCaps(); };
		}

		private void UpdateCaps()
		{
			m_caps.Visibility = Keyboard.IsKeyToggled(Key.CapsLock) ? Visibility.Visible : Visibility.Collapsed;
		}

		private async void OnUnlock(object sender, RoutedEventArgs e)
		{
			m_error.Visibility = Visibility.Collapsed;
			byte[] pbPw = ReadPw(m_pw, m_tgPw, m_pwPlain);
			byte[] pbFactors = null;
			string strKey = (m_cbKey.IsChecked == true) ? m_tbKey.Text.Trim() : "";
			try
			{
				if(m_cbKey.IsChecked == true && strKey.Length == 0) { ShowError(L.T("Choisissez le fichier clé, ou décochez cette option.")); return; }
				if(strKey.Length > 0 && !File.Exists(strKey)) { ShowError(L.T("Fichier clé introuvable : ") + strKey); return; }

				string flags = null; int grid = 3;
				if(m_cbFactors.IsChecked == true)
				{
					FactorsWindow fw = new FactorsWindow(false, m_strPath, m_assoc) { Owner = this };
					if(fw.ShowDialog() != true) return;
					pbFactors = fw.ResultKey; flags = fw.ResultFlags; grid = fw.ResultGrid;
				}

				bool bUsePw = m_cbPw.IsChecked == true;
				bool bAccount = m_cbAccount.IsChecked == true;
				bool bOther = strKey.Length > 0 || pbFactors != null || bAccount;
				if(!bUsePw && !bOther) { ShowError(L.T("Choisissez au moins un élément de clé.")); return; }

				// Candidate keys. KeePass distinguishes "empty password" from "no password":
				// with the box ticked and an empty field, try both (old bases may use either).
				var candidates = new System.Collections.Generic.List<Tuple<CompositeKey, bool>>();
				try
				{
					if(bUsePw) candidates.Add(Tuple.Create(BuildKey(pbPw, strKey, pbFactors, bAccount, false), true));
					if(!bUsePw || (pbPw.Length == 0 && bOther))
						candidates.Add(Tuple.Create(BuildKey(null, strKey, pbFactors, bAccount, false), false));
				}
				catch(Exception ex) { ShowError(ex.Message); return; }

				Busy(L.T("Déchiffrement en cours…"));
				IsEnabled = false;
				Exception exLast = null;
				foreach(var cand in candidates)
				{
					try
					{
						CompositeKey ck = cand.Item1;
						PwDatabase db = await Task.Run(() => Session.OpenFile(m_strPath, ck));
						Result = db;
						Settings.Current.SetKeyAssoc(new KeyAssoc { DatabasePath = m_strPath, KeyFilePath = strKey.Length > 0 ? strKey : null,
							CerberusFactors = pbFactors != null, FactorFlags = flags, GridSize = grid, WindowsAccount = bAccount,
							UsePassword = cand.Item2 });
						Settings.Save();
						DialogResult = true;
						return;
					}
					catch(Exception ex)
					{
						exLast = ex;
						if(!(ex is KeePassLib.Keys.InvalidCompositeKeyException)) break;
						if(candidates.Count > 1) Busy(L.T("Seconde tentative (sans mot de passe vide)…"));
					}
				}
				try { throw exLast; }
				catch(Exception ex)
				{
					Busy(null);
					IsEnabled = true;
					bool bKey = ex is KeePassLib.Keys.InvalidCompositeKeyException;
					ShowError(bKey ? L.T("Clé incorrecte : vérifiez le mot de passe et les autres éléments.") : ex.Message);
					m_pw.Focus();
					m_pw.SelectAll();
				}
			}
			finally
			{
				MemUtil.ZeroByteArray(pbPw);
				if(pbFactors != null) MemUtil.ZeroByteArray(pbFactors);
			}
		}

		private void ShowError(string s)
		{
			m_error.Text = s;
			m_error.Visibility = Visibility.Visible;
		}
	}

	/// <summary>Create a new master key (new database or key change).</summary>
	internal sealed class CreateKeyWindow : KeyWindowBase
	{
		private readonly string m_strPath;
		private readonly PasswordBox m_pw, m_pw2;
		private readonly ToggleButton m_tgPw, m_tg2;
		private readonly TextBox m_pwPlain, m_plain2, m_tbKey;
		private readonly CheckBox m_cbPw, m_cbKey, m_cbFactors, m_cbAccount;
		private readonly ProgressBar m_quality;
		private readonly TextBlock m_qualityText;
		private readonly ComboBox m_profile;

		public CompositeKey ResultKey { get; private set; }
		public Session.Profile ResultProfile { get; private set; }

		public CreateKeyWindow(string strPath, bool bNewDatabase)
			: base(bNewDatabase ? L.T("Protéger la nouvelle base") : L.T("Changer la clé maître"), strPath, "IcShield")
		{
			m_strPath = strPath;

			Hint(L.T("Choisissez un ou plusieurs éléments : tous seront exigés à chaque ouverture."), new Thickness(0, 12, 0, 0));

			m_cbPw = Option(L.T("Mot de passe maître"), true);
			m_cbPw.FontWeight = FontWeights.SemiBold;
			m_pw = PasswordRow(out m_tgPw, out m_pwPlain);
			Body.Children.Add(new TextBlock { Text = L.T("Confirmation"), Style = (Style)FindResource("FieldLabel") });
			m_pw2 = PasswordRow(out m_tg2, out m_plain2);
			m_tg2.Visibility = Visibility.Hidden;
			m_tgPw.Checked += delegate { m_pw2.IsEnabled = false; };
			m_tgPw.Unchecked += delegate { m_pw2.IsEnabled = true; };

			Grid q = new Grid { Margin = new Thickness(0, 10, 0, 0) };
			q.ColumnDefinitions.Add(new ColumnDefinition());
			q.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			m_quality = new ProgressBar { Maximum = 128, VerticalAlignment = VerticalAlignment.Center };
			m_qualityText = new TextBlock { Margin = new Thickness(12, 0, 0, 0), Width = 130 };
			m_qualityText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			Grid.SetColumn(m_qualityText, 1);
			q.Children.Add(m_quality); q.Children.Add(m_qualityText);
			Body.Children.Add(q);
			Button gen = new Button { Content = IconText("IcDice", L.T("Générer un mot de passe")), Style = (Style)FindResource("GhostButton"),
				HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 6, 0, 0) };
			gen.Click += delegate
			{
				GeneratorWindow gw = new GeneratorWindow(true) { Owner = this };
				if(gw.ShowDialog() == true && gw.Result != null)
				{
					m_tgPw.IsChecked = true;
					m_pwPlain.Text = new string(gw.Result);
					Array.Clear(gw.Result, 0, gw.Result.Length);
				}
			};
			Body.Children.Add(gen);
			m_pw.PasswordChanged += delegate { UpdateQuality(); };
			m_pwPlain.TextChanged += delegate { UpdateQuality(); };
			m_cbPw.Checked += delegate { m_pw.IsEnabled = m_tgPw.IsEnabled = true; m_pw2.IsEnabled = m_tgPw.IsChecked != true; };
			m_cbPw.Unchecked += delegate { m_pw.IsEnabled = m_pw2.IsEnabled = m_tgPw.IsEnabled = false; };
			UpdateQuality();

			Section(L.T("Éléments supplémentaires"));
			m_cbKey = Option(L.T("Fichier clé"), false);
			Button create = new Button { Content = L.T("Créer…") }, browse = new Button { Content = L.T("Parcourir…") };
			m_tbKey = FileRow(create, browse);
			browse.Click += delegate { string s = BrowseKeyFile(this, m_tbKey.Text); if(s != null) m_tbKey.Text = s; };
			create.Click += delegate { CreateKeyFile(); };
			Hint(L.T("Gardez une copie de sauvegarde du fichier clé : sans lui, la base est irrécupérable."));
			m_cbFactors = Option(L.T("Facteurs Cerberus (PIN / motif / Shamir)"), false);
			Hint(L.T("Configurés à l'étape suivante."));
			m_cbAccount = Option(L.T("Compte utilisateur Windows"), false);
			Hint(L.T("Lie la base à ce compte Windows : une réinstallation de Windows la rend illisible."));

			if(bNewDatabase)
			{
				Section(L.T("Profil de sécurité"));
				m_profile = new ComboBox();
				foreach(var p in Session.Profiles) m_profile.Items.Add(p);
				m_profile.SelectedIndex = 2; // Renforcé
				Body.Children.Add(m_profile);
				TextBlock desc = Hint("", new Thickness(2, 6, 0, 0));
				m_profile.SelectionChanged += delegate { desc.Text = ((Session.Profile)m_profile.SelectedItem).Description; };
				desc.Text = Session.Profiles[2].Description;
			}

			Button ok = AddButtons(bNewDatabase ? L.T("Créer la base") : L.T("Enregistrer"), out _);
			ok.Click += OnOK;
			Loaded += delegate { m_pw.Focus(); };
		}

		private void UpdateQuality()
		{
			char[] v = (m_tgPw.IsChecked == true) ? m_pwPlain.Text.ToCharArray() : Secure.Chars(m_pw.SecurePassword);
			uint bits = PasswordGen.Estimate(v);
			Array.Clear(v, 0, v.Length);
			m_quality.Value = Math.Min(bits, 128);
			string col = bits < 50 ? "Danger" : bits < 80 ? "Warning" : "Success";
			m_quality.SetResourceReference(ForegroundProperty, col);
			m_qualityText.Text = bits + L.T(" bits · ") + PasswordGen.Label(bits);
		}

		private void CreateKeyFile()
		{
			var sfd = new Microsoft.Win32.SaveFileDialog { Title = L.T("Créer un fichier clé"), Filter = L.T("Fichier clé (*.keyx)|*.keyx"),
				FileName = Path.GetFileNameWithoutExtension(m_strPath ?? "base") + ".keyx" };
			if(sfd.ShowDialog(this) != true) return;
			try
			{
				KcpKeyFile.Create(sfd.FileName, CryptoRandom.Instance.GetRandomBytes(64));
				m_tbKey.Text = sfd.FileName;
			}
			catch(Exception ex) { Dialog.Error(this, L.T("Fichier clé"), ex.Message); }
		}

		private void OnOK(object sender, RoutedEventArgs e)
		{
			bool bPw = m_cbPw.IsChecked == true;
			string strKey = (m_cbKey.IsChecked == true) ? m_tbKey.Text.Trim() : "";
			if(m_cbKey.IsChecked == true && strKey.Length == 0) { Dialog.Info(this, L.T("Fichier clé"), L.T("Choisissez ou créez un fichier clé, ou décochez cette option.")); return; }
			if(!bPw && strKey.Length == 0 && m_cbFactors.IsChecked != true && m_cbAccount.IsChecked != true)
			{ Dialog.Info(this, L.T("Clé maître"), L.T("Choisissez au moins un élément de clé.")); return; }

			byte[] pb = null, pbFactors = null;
			try
			{
				if(bPw)
				{
					pb = ReadPw(m_pw, m_tgPw, m_pwPlain);
					if(m_tgPw.IsChecked != true)
					{
						byte[] pb2 = Secure.Utf8(m_pw2.SecurePassword);
						bool eq = MemUtil.ArraysEqual(pb, pb2);
						MemUtil.ZeroByteArray(pb2);
						if(!eq) { Dialog.Info(this, L.T("Mot de passe"), L.T("Les deux mots de passe ne correspondent pas.")); return; }
					}
					if(pb.Length == 0 && !Dialog.Confirm(this, L.T("Mot de passe vide"),
						L.T("Le mot de passe maître est vide. Continuer quand même ?"), L.T("Continuer"), true)) return;
					uint bits = QualityEstimation.EstimatePasswordBits(pb);
					if(pb.Length > 0 && bits < 50 && !Dialog.Confirm(this, L.T("Mot de passe faible"),
						L.T("Ce mot de passe est faible (") + bits + L.T(" bits). Il peut être deviné. Continuer quand même ?"), L.T("Continuer"), true)) return;
				}

				string flags = null; int grid = 3;
				if(m_cbFactors.IsChecked == true)
				{
					FactorsWindow fw = new FactorsWindow(true, m_strPath, null) { Owner = this };
					if(fw.ShowDialog() != true) return;
					pbFactors = fw.ResultKey; flags = fw.ResultFlags; grid = fw.ResultGrid;
				}

				ResultKey = BuildKey(pb, strKey, pbFactors, m_cbAccount.IsChecked == true, true);
				ResultProfile = (m_profile != null) ? (Session.Profile)m_profile.SelectedItem : null;
				Settings.Current.SetKeyAssoc(new KeyAssoc { DatabasePath = m_strPath, KeyFilePath = strKey.Length > 0 ? strKey : null,
					CerberusFactors = pbFactors != null, FactorFlags = flags, GridSize = grid, WindowsAccount = m_cbAccount.IsChecked == true });
				Settings.Save();
				DialogResult = true;
			}
			catch(Exception ex) { Dialog.Error(this, L.T("Clé maître"), ex.Message); }
			finally
			{
				if(pb != null) MemUtil.ZeroByteArray(pb);
				if(pbFactors != null) MemUtil.ZeroByteArray(pbFactors);
			}
		}
	}
}
