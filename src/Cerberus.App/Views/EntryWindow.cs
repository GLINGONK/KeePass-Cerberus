using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

using Cerberus.App.Services;

using KeePassLib;
using KeePassLib.Security;

namespace Cerberus.App.Views
{
	public sealed class EntryWindow : CerbWindow
	{
		private sealed class FieldItem
		{
			public string Name { get; set; }
			public string Value { get; set; }
			public bool Protected { get; set; }
		}

		private readonly PwEntry m_pe;
		private readonly bool m_bNew;
		private readonly TextBox m_title, m_user, m_url, m_tags, m_notes, m_expiry, m_otp, m_pwPlain;
		private readonly PasswordBox m_pw;
		private readonly ToggleButton m_tgPw;
		private readonly ComboBox m_cat;
		private readonly CheckBox m_cbExpires;
		private readonly ProgressBar m_quality;
		private readonly TextBlock m_qualityText;
		private readonly ObservableCollection<FieldItem> m_fields = new ObservableCollection<FieldItem>();
		private readonly Dictionary<string, ProtectedBinary> m_bins = new Dictionary<string, ProtectedBinary>();
		private readonly ListBox m_lbFields, m_lbBins;

		private sealed class AssocItem
		{
			public string Window { get; set; }
			public string Sequence { get; set; }
		}
		private readonly ObservableCollection<AssocItem> m_assocs = new ObservableCollection<AssocItem>();
		private CheckBox m_atEnabled, m_atCustom;
		private TextBox m_atSeq;

		public EntryWindow(PwEntry pe, bool bNew)
		{
			m_pe = pe; m_bNew = bNew;
			IsDialog = true;
			Title = bNew ? "Nouvelle entrée" : "Modifier l'entrée";
			Width = 640; Height = 720; MinWidth = 520; MinHeight = 520;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ShowInTaskbar = false;

			DockPanel root = new DockPanel();

			// Header
			Border head = new Border { Padding = new Thickness(26, 18, 26, 14) };
			head.SetResourceReference(Border.BackgroundProperty, "Panel");
			StackPanel hs = new StackPanel();
			hs.Children.Add(new TextBlock { Text = Title, Style = (Style)FindResource("Title") });
			head.Child = hs;
			DockPanel.SetDock(head, Dock.Top);
			root.Children.Add(head);

			// Buttons
			DockPanel bar = new DockPanel { Margin = new Thickness(26, 12, 26, 18), LastChildFill = false };
			Button ok = new Button { Content = "Enregistrer", Style = (Style)FindResource("AccentButton"), MinWidth = 120, IsDefault = true };
			Button cancel = new Button { Content = "Annuler", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
			DockPanel.SetDock(ok, Dock.Right); DockPanel.SetDock(cancel, Dock.Right);
			bar.Children.Add(CaptureCheckBox());
			bar.Children.Add(ok); bar.Children.Add(cancel);
			DockPanel.SetDock(bar, Dock.Bottom);
			root.Children.Add(bar);
			ok.Click += delegate { if(SaveEntry()) DialogResult = true; };

			TabControl tabs = new TabControl { Margin = new Thickness(26, 8, 26, 0) };
			root.Children.Add(tabs);

			// ---- General
			StackPanel gen = new StackPanel();
			m_title = Field(gen, "Titre");
			m_user = Field(gen, "Nom d'utilisateur");
			gen.Children.Add(Label("Mot de passe"));
			Grid pg = new Grid();
			pg.ColumnDefinitions.Add(new ColumnDefinition());
			pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			m_pw = new PasswordBox();
			m_pwPlain = new TextBox { Visibility = Visibility.Collapsed, FontFamily = new FontFamily("Consolas") };
			m_tgPw = new ToggleButton { Style = (Style)FindResource("ToggleIcon"), Content = MakeIcon("IcEye"), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Afficher / masquer", Focusable = false };
			Button genPw = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcDice"), ToolTip = "Générer" };
			Grid.SetColumn(m_tgPw, 1); Grid.SetColumn(genPw, 2);
			pg.Children.Add(m_pw); pg.Children.Add(m_pwPlain); pg.Children.Add(m_tgPw); pg.Children.Add(genPw);
			gen.Children.Add(pg);
			m_tgPw.Checked += delegate { m_pwPlain.Text = m_pw.Password; m_pwPlain.Visibility = Visibility.Visible; m_pw.Visibility = Visibility.Collapsed; };
			m_tgPw.Unchecked += delegate { m_pw.Password = m_pwPlain.Text; m_pwPlain.Text = ""; m_pwPlain.Visibility = Visibility.Collapsed; m_pw.Visibility = Visibility.Visible; };
			genPw.Click += delegate
			{
				GeneratorWindow gw = new GeneratorWindow(true) { Owner = this };
				if(gw.ShowDialog() == true && gw.Result != null)
				{
					m_tgPw.IsChecked = true;
					m_pwPlain.Text = new string(gw.Result);
					Array.Clear(gw.Result, 0, gw.Result.Length);
				}
			};
			Grid q = new Grid { Margin = new Thickness(0, 8, 0, 0) };
			q.ColumnDefinitions.Add(new ColumnDefinition());
			q.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			m_quality = new ProgressBar { Maximum = 128, VerticalAlignment = VerticalAlignment.Center };
			m_qualityText = new TextBlock { Margin = new Thickness(12, 0, 0, 0), Width = 130 };
			m_qualityText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			Grid.SetColumn(m_qualityText, 1);
			q.Children.Add(m_quality); q.Children.Add(m_qualityText);
			gen.Children.Add(q);
			m_pw.PasswordChanged += delegate { UpdateQuality(); };
			m_pwPlain.TextChanged += delegate { UpdateQuality(); };

			m_url = Field(gen, "URL");
			Grid two = new Grid();
			two.ColumnDefinitions.Add(new ColumnDefinition());
			two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
			two.ColumnDefinitions.Add(new ColumnDefinition());
			StackPanel c1 = new StackPanel(), c2 = new StackPanel();
			c1.Children.Add(Label("Catégorie"));
			m_cat = new ComboBox();
			m_cat.Items.Add("(aucune)");
			foreach(string n in Categories.Names) m_cat.Items.Add(n);
			c1.Children.Add(m_cat);
			m_tags = Field(c2, "Tags (séparés par des virgules)");
			Grid.SetColumn(c2, 2);
			two.Children.Add(c1); two.Children.Add(c2);
			gen.Children.Add(two);

			gen.Children.Add(Label("Notes"));
			m_notes = new TextBox { Style = (Style)FindResource("MultilineBox"), Height = 110 };
			gen.Children.Add(m_notes);

			StackPanel ex = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
			m_cbExpires = new CheckBox { Content = "Expire le", VerticalAlignment = VerticalAlignment.Center };
			m_expiry = new TextBox { Width = 160, Margin = new Thickness(10, 0, 0, 0), ToolTip = "jj/mm/aaaa hh:mm" };
			ex.Children.Add(m_cbExpires); ex.Children.Add(m_expiry);
			gen.Children.Add(ex);
			tabs.Items.Add(new TabItem { Header = "Général", Content = Scroll(gen) });

			// ---- Custom fields
			DockPanel fd = new DockPanel();
			StackPanel ft = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
			Button addF = new Button { Content = IconText("IcAdd", "Ajouter un champ") };
			ft.Children.Add(addF);
			DockPanel.SetDock(ft, Dock.Top);
			fd.Children.Add(ft);
			m_lbFields = new ListBox { ItemsSource = m_fields };
			m_lbFields.ItemTemplate = FieldTemplate();
			fd.Children.Add(m_lbFields);
			addF.Click += delegate { m_fields.Add(new FieldItem { Name = "Champ " + (m_fields.Count + 1), Value = "", Protected = false }); };
			tabs.Items.Add(new TabItem { Header = "Champs", Content = fd });

			// ---- TOTP
			StackPanel tp = new StackPanel();
			tp.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Text = "Secret Base32 ou URI otpauth:// (compatible KeePass et KeePassXC). Laissez vide pour désactiver." });
			m_otp = Field(tp, "Secret TOTP");
			tabs.Items.Add(new TabItem { Header = "TOTP", Content = tp });

			// ---- Auto-type (KeePass syntax)
			StackPanel atp = new StackPanel();
			m_atEnabled = new CheckBox { Content = "Activer la saisie automatique pour cette entrée" };
			atp.Children.Add(m_atEnabled);
			m_atCustom = new CheckBox { Content = "Séquence personnalisée (sinon : celle du dossier, ou {USERNAME}{TAB}{PASSWORD}{ENTER})", Margin = new Thickness(0, 10, 0, 4) };
			atp.Children.Add(m_atCustom);
			m_atSeq = new TextBox { FontFamily = new FontFamily("Consolas") };
			atp.Children.Add(m_atSeq);
			m_atCustom.Checked += delegate { m_atSeq.IsEnabled = true; };
			m_atCustom.Unchecked += delegate { m_atSeq.IsEnabled = false; };
			atp.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0),
				Text = "Codes : {USERNAME} {PASSWORD} {TITLE} {URL} {NOTES} {S:Champ} {TOTP} · {TAB} {ENTER} {SPACE} {BS} {DEL} {ESC} {UP} {DOWN} {LEFT} {RIGHT} {HOME} {END} {F1}… · {TAB 3} = répéter · {DELAY 500} = pause (ms) · {DELAY=50} = délai entre touches · {CLEARFIELD} · + Maj, ^ Ctrl, % Alt · {+} {^} {%} {{} {}} = caractères." });

			atp.Children.Add(new TextBlock { Text = "ASSOCIATIONS DE FENÊTRES", Style = (Style)FindResource("Section") });
			atp.Children.Add(new TextBlock { Style = (Style)FindResource("Hint"),
				Text = "Pour Ctrl+Alt+A : titre de fenêtre avec * comme joker (ex. *Banque*Connexion*) ou //expression régulière//. Séquence vide = séquence de l'entrée." });
			Button addAssoc = new Button { Content = IconText("IcAdd", "Ajouter une association"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 6) };
			atp.Children.Add(addAssoc);
			ListBox lbA = new ListBox { ItemsSource = m_assocs, ItemTemplate = AssocTemplate(), MinHeight = 120 };
			atp.Children.Add(lbA);
			addAssoc.Click += delegate { m_assocs.Add(new AssocItem { Window = "*" + m_title.Text + "*", Sequence = "" }); };
			tabs.Items.Add(new TabItem { Header = "Saisie auto", Content = Scroll(atp) });

			// ---- Attachments
			DockPanel ad = new DockPanel();
			StackPanel at = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
			Button addA = new Button { Content = IconText("IcAttach", "Joindre un fichier"), Margin = new Thickness(0, 0, 8, 0) };
			Button saveA = new Button { Content = IconText("IcSave", "Enregistrer sous…"), Margin = new Thickness(0, 0, 8, 0) };
			Button delA = new Button { Content = IconText("IcTrash", "Retirer"), Style = (Style)FindResource("DangerButton") };
			at.Children.Add(addA); at.Children.Add(saveA); at.Children.Add(delA);
			DockPanel.SetDock(at, Dock.Top);
			ad.Children.Add(at);
			m_lbBins = new ListBox();
			ad.Children.Add(m_lbBins);
			addA.Click += delegate { AddAttachment(); };
			saveA.Click += delegate { SaveAttachment(); };
			delA.Click += delegate { if(m_lbBins.SelectedItem is string s) { m_bins.Remove(s); RefreshBins(); } };
			tabs.Items.Add(new TabItem { Header = "Pièces jointes", Content = ad });

			// ---- History
			if(!bNew && pe.History.UCount > 0)
			{
				ListBox lh = new ListBox();
				foreach(PwEntry h in pe.History.Reverse())
					lh.Items.Add(h.LastModificationTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + "  ·  " + h.Strings.ReadSafe(PwDefs.TitleField));
				tabs.Items.Add(new TabItem { Header = "Historique (" + pe.History.UCount + ")", Content = lh });
			}

			Content = root;
			LoadEntry();
			Loaded += delegate { m_title.Focus(); m_title.SelectAll(); };
			Closed += delegate { m_pwPlain.Text = ""; m_pw.Clear(); };
		}

		private static ScrollViewer Scroll(UIElement e) { return new ScrollViewer { Content = e, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 8, 0) }; }

		private TextBlock Label(string s) { return new TextBlock { Text = s, Style = (Style)FindResource("FieldLabel") }; }

		private TextBox Field(Panel p, string label)
		{
			p.Children.Add(Label(label));
			TextBox tb = new TextBox();
			p.Children.Add(tb);
			return tb;
		}

		private DataTemplate FieldTemplate()
		{
			// Name | Value | protected | delete — built with FrameworkElementFactory
			var grid = new FrameworkElementFactory(typeof(Grid));
			var c0 = new FrameworkElementFactory(typeof(ColumnDefinition)); c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(160));
			var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
			var c2 = new FrameworkElementFactory(typeof(ColumnDefinition)); c2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
			var c3 = new FrameworkElementFactory(typeof(ColumnDefinition)); c3.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
			grid.AppendChild(c0); grid.AppendChild(c1); grid.AppendChild(c2); grid.AppendChild(c3);

			var name = new FrameworkElementFactory(typeof(TextBox));
			name.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Name") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
			name.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
			var val = new FrameworkElementFactory(typeof(TextBox));
			val.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Value") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
			val.SetValue(Grid.ColumnProperty, 1);
			var prot = new FrameworkElementFactory(typeof(CheckBox));
			prot.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding("Protected"));
			prot.SetValue(ContentControl.ContentProperty, "Secret");
			prot.SetValue(Grid.ColumnProperty, 2);
			prot.SetValue(MarginProperty, new Thickness(10, 0, 6, 0));
			prot.SetValue(ToolTipProperty, "Masqué, protégé en mémoire, exclu de la recherche");
			var del = new FrameworkElementFactory(typeof(Button));
			del.SetValue(Grid.ColumnProperty, 3);
			del.SetValue(ContentControl.ContentProperty, "✕");
			del.SetResourceReference(StyleProperty, "IconButton");
			del.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
			{
				if((s as FrameworkElement)?.DataContext is FieldItem f) m_fields.Remove(f);
			}));
			grid.AppendChild(name); grid.AppendChild(val); grid.AppendChild(prot); grid.AppendChild(del);
			return new DataTemplate { VisualTree = grid };
		}

		private DataTemplate AssocTemplate()
		{
			var grid = new FrameworkElementFactory(typeof(Grid));
			var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
			var c1 = new FrameworkElementFactory(typeof(ColumnDefinition)); c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(200));
			var c2 = new FrameworkElementFactory(typeof(ColumnDefinition)); c2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
			grid.AppendChild(c0); grid.AppendChild(c1); grid.AppendChild(c2);
			var win = new FrameworkElementFactory(typeof(TextBox));
			win.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Window") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
			win.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
			win.SetValue(ToolTipProperty, "Titre de fenêtre (* = joker)");
			var seq = new FrameworkElementFactory(typeof(TextBox));
			seq.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Sequence") { UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
			seq.SetValue(Grid.ColumnProperty, 1);
			seq.SetValue(ToolTipProperty, "Séquence (vide = séquence de l'entrée)");
			var del = new FrameworkElementFactory(typeof(Button));
			del.SetValue(Grid.ColumnProperty, 2);
			del.SetValue(ContentControl.ContentProperty, "✕");
			del.SetResourceReference(StyleProperty, "IconButton");
			del.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
			{
				if((s as FrameworkElement)?.DataContext is AssocItem a) m_assocs.Remove(a);
			}));
			grid.AppendChild(win); grid.AppendChild(seq); grid.AppendChild(del);
			return new DataTemplate { VisualTree = grid };
		}

		private void UpdateQuality()
		{
			char[] v = (m_tgPw.IsChecked == true) ? m_pwPlain.Text.ToCharArray() : Secure.Chars(m_pw.SecurePassword);
			uint bits = PasswordGen.Estimate(v);
			Array.Clear(v, 0, v.Length);
			m_quality.Value = Math.Min(bits, 128);
			m_quality.SetResourceReference(ForegroundProperty, bits < 50 ? "Danger" : bits < 80 ? "Warning" : "Success");
			m_qualityText.Text = bits + " bits · " + PasswordGen.Label(bits);
		}

		private void LoadEntry()
		{
			m_title.Text = m_pe.Strings.ReadSafe(PwDefs.TitleField);
			m_user.Text = m_pe.Strings.ReadSafe(PwDefs.UserNameField);
			m_pw.Password = m_pe.Strings.ReadSafe(PwDefs.PasswordField);
			m_url.Text = m_pe.Strings.ReadSafe(PwDefs.UrlField);
			m_notes.Text = m_pe.Strings.ReadSafe(PwDefs.NotesField);
			string cat = Categories.Of(m_pe);
			m_cat.SelectedIndex = (cat == null) ? 0 : Array.IndexOf(Categories.Names, cat) + 1;
			m_tags.Text = string.Join(", ", m_pe.Tags.Where(t => !Categories.Names.Contains(t, StringComparer.OrdinalIgnoreCase)));
			m_cbExpires.IsChecked = m_pe.Expires;
			m_expiry.Text = (m_pe.Expires ? m_pe.ExpiryTime : DateTime.UtcNow.AddYears(1)).ToLocalTime().ToString("dd/MM/yyyy HH:mm");

			string otp = m_pe.Strings.ReadSafe("otp");
			if(otp.Length == 0) otp = m_pe.Strings.ReadSafe("TimeOtp-Secret-Base32");
			m_otp.Text = otp;

			foreach(var kv in m_pe.Strings)
			{
				if(PwDefs.IsStandardField(kv.Key) || kv.Key == "otp" || kv.Key.StartsWith("TimeOtp-")) continue;
				m_fields.Add(new FieldItem { Name = kv.Key, Value = kv.Value.ReadString(), Protected = kv.Value.IsProtected });
			}
			foreach(var kv in m_pe.Binaries) m_bins[kv.Key] = kv.Value;

			m_atEnabled.IsChecked = m_pe.AutoType.Enabled;
			bool bCustom = !string.IsNullOrEmpty(m_pe.AutoType.DefaultSequence);
			m_atCustom.IsChecked = bCustom;
			m_atSeq.Text = bCustom ? m_pe.AutoType.DefaultSequence : KeePassLib.PwDefs.DefaultAutoTypeSequence;
			m_atSeq.IsEnabled = bCustom;
			foreach(var a in m_pe.AutoType.Associations) m_assocs.Add(new AssocItem { Window = a.WindowName, Sequence = a.Sequence });
			RefreshBins();
			UpdateQuality();
		}

		private void RefreshBins()
		{
			m_lbBins.Items.Clear();
			foreach(string k in m_bins.Keys.OrderBy(k => k)) m_lbBins.Items.Add(k);
		}

		private void AddAttachment()
		{
			var ofd = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Joindre des fichiers" };
			if(ofd.ShowDialog(this) != true) return;
			foreach(string f in ofd.FileNames)
			{
				try
				{
					byte[] pb = File.ReadAllBytes(f);
					m_bins[Path.GetFileName(f)] = new ProtectedBinary(true, pb);
					KeePassLib.Utility.MemUtil.ZeroByteArray(pb);
				}
				catch(Exception ex) { Dialog.Error(this, "Pièce jointe", ex.Message); }
			}
			RefreshBins();
		}

		private void SaveAttachment()
		{
			if(!(m_lbBins.SelectedItem is string s)) return;
			var sfd = new Microsoft.Win32.SaveFileDialog { FileName = s };
			if(sfd.ShowDialog(this) != true) return;
			try
			{
				byte[] pb = m_bins[s].ReadData();
				File.WriteAllBytes(sfd.FileName, pb);
				KeePassLib.Utility.MemUtil.ZeroByteArray(pb);
			}
			catch(Exception ex) { Dialog.Error(this, "Pièce jointe", ex.Message); }
		}

		private bool SaveEntry()
		{
			PwDatabase db = Session.Db;
			if(db == null) return false;

			DateTime dtExp = DateTime.UtcNow;
			if(m_cbExpires.IsChecked == true)
			{
				if(!DateTime.TryParseExact(m_expiry.Text.Trim(), new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" },
					CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dtExp))
				{ Dialog.Info(this, "Expiration", "Date invalide (format jj/mm/aaaa hh:mm)."); return false; }
				dtExp = dtExp.ToUniversalTime();
			}

			HashSet<string> names = new HashSet<string>();
			foreach(FieldItem f in m_fields)
			{
				string n = (f.Name ?? "").Trim();
				if(n.Length == 0 || PwDefs.IsStandardField(n) || !names.Add(n))
				{ Dialog.Info(this, "Champs", "Nom de champ vide, réservé ou en double : « " + n + " »."); return false; }
			}

			if(!m_bNew) m_pe.CreateBackup(db);

			var mp = db.MemoryProtection;
			m_pe.Strings.Set(PwDefs.TitleField, new ProtectedString(mp.ProtectTitle, m_title.Text));
			m_pe.Strings.Set(PwDefs.UserNameField, new ProtectedString(mp.ProtectUserName, m_user.Text));
			if(m_tgPw.IsChecked == true) m_pw.Password = m_pwPlain.Text;
			m_pe.Strings.Set(PwDefs.PasswordField, Secure.ToProtected(m_pw.SecurePassword));
			m_pe.Strings.Set(PwDefs.UrlField, new ProtectedString(mp.ProtectUrl, m_url.Text));
			m_pe.Strings.Set(PwDefs.NotesField, new ProtectedString(mp.ProtectNotes, m_notes.Text));

			// custom fields: replace all non-standard (except TOTP handled below)
			foreach(string k in m_pe.Strings.GetKeys().ToList())
				if(!PwDefs.IsStandardField(k) && k != "otp" && !k.StartsWith("TimeOtp-")) m_pe.Strings.Remove(k);
			foreach(FieldItem f in m_fields) m_pe.Strings.Set(f.Name.Trim(), new ProtectedString(f.Protected, f.Value ?? ""));

			string otp = m_otp.Text.Trim();
			m_pe.Strings.Remove("otp");
			m_pe.Strings.Remove("TimeOtp-Secret-Base32");
			if(otp.Length > 0)
			{
				if(otp.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase)) m_pe.Strings.Set("otp", new ProtectedString(true, otp));
				else m_pe.Strings.Set("TimeOtp-Secret-Base32", new ProtectedString(true, otp.Replace(" ", "")));
			}

			var tags = m_tags.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
			m_pe.Tags.Clear();
			foreach(string t in tags) m_pe.AddTag(t);
			Categories.Set(m_pe, m_cat.SelectedIndex > 0 ? (string)m_cat.SelectedItem : null);

			m_pe.AutoType.Enabled = m_atEnabled.IsChecked == true;
			m_pe.AutoType.DefaultSequence = (m_atCustom.IsChecked == true) ? m_atSeq.Text.Trim() : string.Empty;
			m_pe.AutoType.Clear();
			foreach(AssocItem a in m_assocs)
			{
				string w = (a.Window ?? "").Trim();
				if(w.Length > 0) m_pe.AutoType.Add(new KeePassLib.Collections.AutoTypeAssociation(w, (a.Sequence ?? "").Trim()));
			}

			m_pe.Expires = m_cbExpires.IsChecked == true;
			if(m_pe.Expires) m_pe.ExpiryTime = dtExp;

			m_pe.Binaries.Clear();
			foreach(var kv in m_bins) m_pe.Binaries.Set(kv.Key, kv.Value);

			m_pe.Touch(true, false);
			if(!m_bNew) m_pe.MaintainBackups(db);
			return true;
		}
	}
}
