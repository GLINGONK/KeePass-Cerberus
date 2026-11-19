using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;

using Microsoft.Win32;

using Cerberus.App.Services;

using KeePassLib;
using KeePassLib.Security;

namespace Cerberus.App.Views
{
	public sealed class MainWindow : CerbWindow
	{
		private sealed class EntryVm
		{
			public PwEntry Entry;
			public string Title { get; set; }
			public string Subtitle { get; set; }
			public string Icon { get; set; }
			public string Color { get; set; }
		}

		// layout
		private readonly Grid m_main;
		private readonly TextBlock m_dbName, m_status, m_clipInfo;
		private readonly TextBox m_search;
		private readonly ListBox m_side, m_list;
		private readonly ScrollViewer m_details;
		private readonly ColumnDefinition m_detailsCol;
		private readonly Grid m_welcome, m_busy;
		private readonly StackPanel m_welcomeBody;
		private readonly TextBlock m_busyText;
		private readonly Button m_btnLock;
		private readonly List<Button> m_dbOnly = new List<Button>();

		// state
		private string m_lockedPath;
		private readonly DispatcherTimer m_idle, m_totpTimer;
		private Action m_totpTick;
		private bool m_bClosing;

		public MainWindow()
		{
			Title = "KeePass Cerberus";
			Width = Settings.Current.WindowWidth; Height = Settings.Current.WindowHeight;
			MinWidth = 860; MinHeight = 520;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;
			if(Settings.Current.WindowMaximized) WindowState = WindowState.Maximized;

			Grid root = new Grid();
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition());
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			// ===== Toolbar =====
			Border tb = new Border { Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(0, 0, 0, 1) };
			tb.SetResourceReference(Border.BackgroundProperty, "Panel");
			tb.SetResourceReference(Border.BorderBrushProperty, "Border");
			DockPanel bar = new DockPanel();
			StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
			Border logo = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 10, 0) };
			logo.SetResourceReference(Border.BackgroundProperty, "Accent");
			Path lp = MakeIcon("IcShield", 16); lp.Stroke = Brushes.White;
			logo.Child = lp;
			brand.Children.Add(logo);
			StackPanel bt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			bt.Children.Add(new TextBlock { Text = "KeePass Cerberus", FontWeight = FontWeights.SemiBold, FontSize = 14 });
			m_dbName = new TextBlock { FontSize = 11, MaxWidth = 220 };
			m_dbName.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			bt.Children.Add(m_dbName);
			brand.Children.Add(bt);
			bar.Children.Add(brand);

			StackPanel right = new StackPanel { Orientation = Orientation.Horizontal };
			DockPanel.SetDock(right, Dock.Right);
			right.Children.Add(TbButton("IcDice", L.T("Générateur de mots de passe"), delegate { new GeneratorWindow(false) { Owner = this }.ShowDialog(); }, false));
			right.Children.Add(TbButton("IcSave", L.T("Enregistrer (Ctrl+S)"), async delegate { await SaveAsync(); }, true));
			m_btnLock = TbButton("IcLock", L.T("Verrouiller (Ctrl+L)"), async delegate { await LockAsync(); }, true);
			right.Children.Add(m_btnLock);
			Button more = TbButton("IcMore", "Menu", null, false);
			more.Click += delegate { BuildMenu().IsOpen = true; };
			right.Children.Add(more);
			bar.Children.Add(right);

			StackPanel left = new StackPanel { Orientation = Orientation.Horizontal };
			DockPanel.SetDock(left, Dock.Left);
			Button add = new Button { Content = IconText("IcAdd", L.T("Nouvelle entrée")), Style = (Style)FindResource("AccentButton"), Margin = new Thickness(0, 0, 10, 0), ToolTip = "Ctrl+N" };
			add.Click += delegate { NewEntry(); };
			m_dbOnly.Add(add);
			left.Children.Add(add);
			left.Children.Add(TbButton("IcUser", L.T("Copier le nom d'utilisateur (Ctrl+B)"), delegate { CopyField(PwDefs.UserNameField); }, true));
			left.Children.Add(TbButton("IcKey", L.T("Copier le mot de passe (Ctrl+C)"), delegate { CopyField(PwDefs.PasswordField); }, true));
			left.Children.Add(TbButton("IcGlobe", L.T("Ouvrir l'URL (Ctrl+U)"), delegate { OpenUrl(); }, true));
			left.Children.Add(TbButton("IcType", L.T("Saisie automatique dans la fenêtre précédente (Ctrl+V)") + "\n" + L.T("Raccourci global : Ctrl+Alt+A"), async delegate { await AutoTypeSelectedAsync(); }, true));
			bar.Children.Add(left);

			Grid sg = new Grid { Margin = new Thickness(12, 0, 12, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Stretch };
			m_search = new TextBox { Padding = new Thickness(34, 7, 9, 7), ToolTip = L.T("Rechercher (Ctrl+F)") };
			Path sic = MakeIcon("IcSearch", 15); sic.HorizontalAlignment = HorizontalAlignment.Left; sic.Margin = new Thickness(11, 0, 0, 0);
			sic.SetResourceReference(Shape.StrokeProperty, "Muted"); sic.IsHitTestVisible = false;
			TextBlock ph = new TextBlock { Text = L.T("Rechercher…"), Margin = new Thickness(36, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
			ph.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			m_search.TextChanged += delegate { ph.Visibility = m_search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; RefreshList(); };
			sg.Children.Add(m_search); sg.Children.Add(sic); sg.Children.Add(ph);
			bar.Children.Add(sg);
			tb.Child = bar;
			root.Children.Add(tb);

			// ===== Main area =====
			m_main = new Grid();
			m_main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
			m_main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
			m_detailsCol = new ColumnDefinition { Width = new GridLength(380), MinWidth = 0 };
			m_main.ColumnDefinitions.Add(m_detailsCol);
			Grid.SetRow(m_main, 1);

			Border side = new Border { BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(10, 12, 10, 12) };
			side.SetResourceReference(Border.BackgroundProperty, "Panel");
			side.SetResourceReference(Border.BorderBrushProperty, "Border");
			m_side = new ListBox();
			m_side.ItemTemplate = SideTemplate();
			m_side.SelectionChanged += delegate { RefreshList(); };
			side.Child = m_side;
			m_main.Children.Add(side);

			m_list = new ListBox { Padding = new Thickness(10, 10, 10, 10) };
			VirtualizingPanel.SetIsVirtualizing(m_list, true);
			m_list.ItemTemplate = EntryTemplate();
			m_list.SelectionChanged += delegate { ShowDetails(); };
			m_list.MouseDoubleClick += (s, e) => { if(m_list.SelectedItem != null) EditEntry(); };
			m_list.ContextMenuOpening += (s, e) => { if(Selected == null) { e.Handled = true; return; } m_list.ContextMenu = EntryMenu(); };
			m_list.ContextMenu = new ContextMenu();
			Grid.SetColumn(m_list, 1);
			m_main.Children.Add(m_list);

			Border det = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
			det.SetResourceReference(Border.BackgroundProperty, "Panel");
			det.SetResourceReference(Border.BorderBrushProperty, "Border");
			m_details = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(22, 20, 22, 20) };
			det.Child = m_details;
			Grid.SetColumn(det, 2);
			m_main.Children.Add(det);
			root.Children.Add(m_main);

			// ===== Welcome / locked screen =====
			m_welcome = new Grid();
			m_welcome.SetResourceReference(Panel.BackgroundProperty, "Bg");
			Grid.SetRow(m_welcome, 1);
			m_welcomeBody = new StackPanel { Width = 440, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
			m_welcome.Children.Add(new ScrollViewer { Content = m_welcomeBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
			root.Children.Add(m_welcome);

			// ===== Status bar =====
			Border st = new Border { Padding = new Thickness(14, 6, 14, 6), BorderThickness = new Thickness(0, 1, 0, 0) };
			st.SetResourceReference(Border.BackgroundProperty, "Panel");
			st.SetResourceReference(Border.BorderBrushProperty, "Border");
			DockPanel sd = new DockPanel();
			CheckBox cap = CaptureCheckBox();
			cap.Margin = new Thickness(0);
			DockPanel.SetDock(cap, Dock.Right);
			sd.Children.Add(cap);
			m_clipInfo = new TextBlock { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
			m_clipInfo.SetResourceReference(TextBlock.ForegroundProperty, "Warning");
			DockPanel.SetDock(m_clipInfo, Dock.Right);
			sd.Children.Add(m_clipInfo);
			m_status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
			m_status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			sd.Children.Add(m_status);
			st.Child = sd;
			Grid.SetRow(st, 2);
			root.Children.Add(st);

			// ===== Busy overlay =====
			m_busy = new Grid { Visibility = Visibility.Collapsed };
			m_busy.SetResourceReference(Panel.BackgroundProperty, "Overlay");
			Grid.SetRowSpan(m_busy, 3);
			StackPanel bsp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
			bsp.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 240, Margin = new Thickness(0, 0, 0, 12) });
			m_busyText = new TextBlock { FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center };
			bsp.Children.Add(m_busyText);
			m_busy.Children.Add(bsp);
			root.Children.Add(m_busy);

			Content = root;

			SafeClipboard.Countdown += n => Dispatcher.Invoke(() => m_clipInfo.Text = n > 0 ? L.T("Presse-papiers effacé dans ") + n + " s" : "");
			Session.Changed += () => Dispatcher.Invoke(UpdateUi);

			m_idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
			m_idle.Tick += async delegate
			{
				int min = Settings.Current.AutoLockMinutes;
				if(min > 0 && Session.IsOpen && Native.IdleMilliseconds() >= (uint)min * 60000U) await LockAsync();
			};
			m_idle.Start();
			m_totpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			m_totpTimer.Tick += delegate { m_totpTick?.Invoke(); };
			m_totpTimer.Start();

			SourceInitialized += delegate { RegisterGlobalHotKey(); };
			SystemEvents.SessionSwitch += OnSessionSwitch;
			SystemEvents.PowerModeChanged += OnPowerMode;
			StateChanged += async delegate { if(WindowState == WindowState.Minimized && Settings.Current.LockOnMinimize && Session.IsOpen && !m_bTyping) await LockAsync(); };
			Closing += OnClosing;
			Closed += delegate
			{
				SystemEvents.SessionSwitch -= OnSessionSwitch;
				SystemEvents.PowerModeChanged -= OnPowerMode;
			};
			PreviewKeyDown += OnKeys;

			UpdateUi();
		}

		// ================= helpers =================

		private Button TbButton(string icon, string tip, RoutedEventHandler h, bool bNeedsDb)
		{
			Button b = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon(icon, 17), ToolTip = tip, Margin = new Thickness(2, 0, 2, 0) };
			if(h != null) b.Click += h;
			if(bNeedsDb) m_dbOnly.Add(b);
			return b;
		}

		private static Brush BrushOf(string hex)
		{
			return (Brush)new BrushConverter().ConvertFromString(hex);
		}

		private DataTemplate SideTemplate()
		{
			var dp = new FrameworkElementFactory(typeof(DockPanel));
			var path = new FrameworkElementFactory(typeof(Path));
			path.SetResourceReference(StyleProperty, "Icon");
			path.SetBinding(Path.DataProperty, new System.Windows.Data.Binding("Icon") { Converter = new ResKey() });
			path.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Color") { Converter = new HexBrush() });
			path.SetValue(MarginProperty, new Thickness(0, 0, 11, 0));
			path.SetBinding(MarginProperty, new System.Windows.Data.Binding("Depth") { Converter = new DepthMargin() });
			var count = new FrameworkElementFactory(typeof(TextBlock));
			count.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Count"));
			count.SetValue(DockPanel.DockProperty, Dock.Right);
			count.SetValue(TextBlock.FontSizeProperty, 11.5);
			count.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			count.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
			var name = new FrameworkElementFactory(typeof(TextBlock));
			name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
			name.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
			dp.AppendChild(path); dp.AppendChild(count); dp.AppendChild(name);
			return new DataTemplate { VisualTree = dp };
		}

		private DataTemplate EntryTemplate()
		{
			var g = new FrameworkElementFactory(typeof(DockPanel));
			var circle = new FrameworkElementFactory(typeof(Border));
			circle.SetValue(WidthProperty, 36.0); circle.SetValue(HeightProperty, 36.0);
			circle.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
			circle.SetValue(MarginProperty, new Thickness(0, 0, 12, 0));
			circle.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Color") { Converter = new HexBrush(), ConverterParameter = "soft" });
			var ic = new FrameworkElementFactory(typeof(Path));
			ic.SetResourceReference(StyleProperty, "Icon");
			ic.SetBinding(Path.DataProperty, new System.Windows.Data.Binding("Icon") { Converter = new ResKey() });
			ic.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Color") { Converter = new HexBrush() });
			circle.AppendChild(ic);
			var sp = new FrameworkElementFactory(typeof(StackPanel));
			sp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
			var t1 = new FrameworkElementFactory(typeof(TextBlock));
			t1.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
			t1.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
			var t2 = new FrameworkElementFactory(typeof(TextBlock));
			t2.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Subtitle"));
			t2.SetValue(TextBlock.FontSizeProperty, 12.0);
			t2.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			sp.AppendChild(t1); sp.AppendChild(t2);
			g.AppendChild(circle); g.AppendChild(sp);
			return new DataTemplate { VisualTree = g };
		}

		private EntryVm Selected { get { return m_list.SelectedItem as EntryVm; } }

		private void Status(string s) { m_status.Text = s; }

		private void Busy(string s)
		{
			m_busyText.Text = s ?? "";
			m_busy.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
		}

		// ================= state / UI =================

		private void UpdateUi()
		{
			bool open = Session.IsOpen;
			m_main.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
			m_welcome.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
			m_search.IsEnabled = open;
			foreach(Button b in m_dbOnly) b.IsEnabled = open;
			m_dbName.Text = open ? (string.IsNullOrEmpty(Session.Db.Name) ? System.IO.Path.GetFileName(Session.Path) : Session.Db.Name) +
				(Session.Db.Modified ? " •" : "") : (m_lockedPath != null ? L.T("Verrouillée") : L.T("Aucune base ouverte"));
			m_detailsCol.Width = Settings.Current.ShowDetailsPane ? new GridLength(380) : new GridLength(0);
			if(open) { RefreshSide(); }
			else { BuildWelcome(); m_list.ItemsSource = null; m_details.Content = null; m_totpTick = null; }
		}

		private void BuildWelcome()
		{
			m_welcomeBody.Children.Clear();
			Border logo = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center };
			logo.SetResourceReference(Border.BackgroundProperty, "Accent");
			Path p = MakeIcon(m_lockedPath != null ? "IcLock" : "IcShield", 34); p.Stroke = Brushes.White;
			logo.Child = p;
			m_welcomeBody.Children.Add(logo);
			m_welcomeBody.Children.Add(new TextBlock { Text = m_lockedPath != null ? L.T("Base verrouillée") : "KeePass Cerberus", FontSize = 26, FontWeight = FontWeights.SemiBold,
				HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 4) });
			TextBlock sub = new TextBlock { Text = m_lockedPath ?? L.T("Gestionnaire de mots de passe local, compatible KeePass."), HorizontalAlignment = HorizontalAlignment.Center,
				TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.None };
			sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			m_welcomeBody.Children.Add(sub);

			StackPanel btns = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
			if(m_lockedPath != null)
			{
				Button un = new Button { Content = IconText("IcLock", L.T("Déverrouiller")), Style = (Style)FindResource("AccentButton"), MinHeight = 42, Margin = new Thickness(0, 0, 0, 8) };
				un.Click += async delegate { await OpenAsync(m_lockedPath); };
				btns.Children.Add(un);
			}
			Button open = new Button { Content = IconText("IcOpen", L.T("Ouvrir une base…")), MinHeight = 42, Margin = new Thickness(0, 0, 0, 8) };
			if(m_lockedPath == null) open.Style = (Style)FindResource("AccentButton");
			open.Click += async delegate { await OpenDialogAsync(); };
			Button create = new Button { Content = IconText("IcNewDb", L.T("Créer une nouvelle base…")), MinHeight = 42 };
			create.Click += async delegate { await NewDatabaseAsync(); };
			btns.Children.Add(open); btns.Children.Add(create);
			m_welcomeBody.Children.Add(btns);

			var recent = Settings.Current.RecentDatabases.Where(File.Exists).Take(6).ToList();
			if(recent.Count > 0)
			{
				m_welcomeBody.Children.Add(new TextBlock { Text = L.T("RÉCENTS"), Style = (Style)FindResource("Section"), Margin = new Thickness(0, 28, 0, 6) });
				foreach(string r in recent)
				{
					Button b = new Button { Style = (Style)FindResource("GhostButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1) };
					StackPanel sp = new StackPanel();
					sp.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(r), FontWeight = FontWeights.SemiBold });
					TextBlock d = new TextBlock { Text = System.IO.Path.GetDirectoryName(r), FontSize = 11.5 };
					d.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
					sp.Children.Add(d);
					b.Content = sp;
					string path = r;
					b.Click += async delegate { await OpenAsync(path); };
					m_welcomeBody.Children.Add(b);
				}
			}
		}

		private void RefreshSide()
		{
			object prev = m_side.SelectedItem;
			string prevName = (prev as Categories.Cat)?.Name;
			PwGroup prevGroup = (prev as Categories.Cat)?.Group;

			var all = Session.AllEntries(false).ToList();
			List<Categories.Cat> items = new List<Categories.Cat>();
			items.Add(new Categories.Cat { Name = L.T("Toutes les entrées"), Icon = "IcList", Color = "#4C8DFF", Count = all.Count, IsAll = true });
			foreach(string n in Categories.Names)
				items.Add(new Categories.Cat { Name = n, Icon = Categories.Icons[n], Color = Categories.Colors[n],
					Count = all.Count(e => string.Equals(Categories.Of(e), n, StringComparison.OrdinalIgnoreCase)) });
			items.Add(new Categories.Cat { Name = L.T("Non classées"), Icon = "IcQuestion", Color = "#8C92A5", Count = all.Count(e => Categories.Of(e) == null), IsNone = true });

			if(Settings.Current.ShowFolders)
			{
				PwGroup bin = Session.RecycleBin;
				AddFolders(items, Session.Db.RootGroup, 0, bin);
			}
			PwGroup pgBin = Session.RecycleBin;
			if(pgBin != null)
				items.Add(new Categories.Cat { Name = L.T("Corbeille"), Icon = "IcTrash", Color = "#8C92A5", Count = (int)pgBin.GetEntriesCount(true), IsBin = true, Group = pgBin });

			m_side.ItemsSource = items;
			Categories.Cat sel = items.FirstOrDefault(c => prevGroup != null ? c.Group == prevGroup : c.Name == prevName) ?? items[0];
			m_side.SelectedItem = sel;
			RefreshList();
		}

		private void AddFolders(List<Categories.Cat> items, PwGroup pg, int depth, PwGroup bin)
		{
			foreach(PwGroup sub in pg.Groups)
			{
				if(sub == bin) continue;
				items.Add(new Categories.Cat { Name = sub.Name, Icon = "IcFolder", Color = "#99A1B5", Count = (int)sub.GetEntriesCount(true),
					IsFolder = true, Group = sub, Depth = depth });
				AddFolders(items, sub, depth + 1, bin);
			}
		}

		private void RefreshList()
		{
			if(!Session.IsOpen) return;
			Categories.Cat c = m_side.SelectedItem as Categories.Cat;
			IEnumerable<PwEntry> src;
			if(c == null || c.IsAll) src = Session.AllEntries(false);
			else if(c.IsBin) src = c.Group.GetEntries(true);
			else if(c.IsFolder) src = c.Group.GetEntries(true).Where(e => Session.RecycleBin == null || !e.IsContainedIn(Session.RecycleBin));
			else if(c.IsNone) src = Session.AllEntries(false).Where(e => Categories.Of(e) == null);
			else src = Session.AllEntries(false).Where(e => string.Equals(Categories.Of(e), c.Name, StringComparison.OrdinalIgnoreCase));

			string q = m_search.Text.Trim();
			if(q.Length > 0)
			{
				PwGroup bin = Session.RecycleBin;
				src = Session.AllEntries(false).Where(e => Matches(e, q));
			}

			PwEntry prev = Selected?.Entry;
			var vms = src.Select(ToVm).OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
			m_list.ItemsSource = vms;
			EntryVm keep = vms.FirstOrDefault(v => v.Entry == prev);
			if(keep != null) m_list.SelectedItem = keep;
			else if(vms.Count > 0) m_list.SelectedIndex = 0;
			else ShowDetails();
			Status(vms.Count + L.T(" entrée(s)") + (q.Length > 0 ? L.T(" correspondant à « ") + q + " »" : ""));
		}

		private static bool Matches(PwEntry e, string q)
		{
			foreach(var kv in e.Strings)
			{
				if(kv.Value.IsProtected) continue;
				if(kv.Value.ReadString().IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) return true;
			}
			return e.Tags.Any(t => t.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
		}

		private static EntryVm ToVm(PwEntry e)
		{
			string cat = Categories.Of(e);
			string user = e.Strings.ReadSafe(PwDefs.UserNameField), url = e.Strings.ReadSafe(PwDefs.UrlField);
			string host = url;
			try { if(url.Length > 0) host = new Uri(url.Contains("://") ? url : "https://" + url).Host; } catch(Exception) { }
			string sub = user.Length > 0 && host.Length > 0 ? user + "  ·  " + host : (user.Length > 0 ? user : host);
			string title = e.Strings.ReadSafe(PwDefs.TitleField);
			return new EntryVm { Entry = e, Title = title.Length > 0 ? title : L.T("(sans titre)"), Subtitle = sub,
				Icon = cat != null ? Categories.Icons[cat] : "IcKey", Color = cat != null ? Categories.Colors[cat] : "#8C92A5" };
		}

		// ================= details =================

		private void ShowDetails()
		{
			m_totpTick = null;
			EntryVm vm = Selected;
			if(vm == null)
			{
				TextBlock e = new TextBlock { Text = L.T("Sélectionnez une entrée"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 0) };
				e.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
				m_details.Content = e;
				return;
			}
			PwEntry pe = vm.Entry;
			StackPanel sp = new StackPanel();

			DockPanel head = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
			Border ic = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 14, 0), Background = new HexBrush().Soft(vm.Color) };
			Path p = MakeIcon(vm.Icon, 22); p.Stroke = BrushOf(vm.Color);
			ic.Child = p;
			head.Children.Add(ic);
			StackPanel ht = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			ht.Children.Add(new TextBlock { Text = vm.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None });
			string cat = Categories.Of(pe);
			TextBlock ct = new TextBlock { Text = cat != null ? L.T(cat) : L.T("Non classée"), FontSize = 12 };
			ct.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			ht.Children.Add(ct);
			head.Children.Add(ht);
			sp.Children.Add(head);

			StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
			Button edit = new Button { Content = IconText("IcEdit", L.T("Modifier")), Margin = new Thickness(0, 0, 8, 0) };
			edit.Click += delegate { EditEntry(); };
			Button del = new Button { Content = IconText("IcTrash", L.T("Supprimer")), Style = (Style)FindResource("DangerButton") };
			del.Click += async delegate { await DeleteEntryAsync(); };
			Button qr = new Button { Content = IconText("IcQr", L.T("QR code")), Margin = new Thickness(0, 0, 8, 0), ToolTip = L.T("Afficher identifiant / mot de passe en QR code pour un téléphone") };
			qr.Click += delegate { ShowQr(); };
			actions.Children.Add(edit); actions.Children.Add(qr); actions.Children.Add(del);
			sp.Children.Add(actions);

			AddRow(sp, L.T("Nom d'utilisateur"), pe.Strings.Get(PwDefs.UserNameField), false);
			AddRow(sp, L.T("Mot de passe"), pe.Strings.Get(PwDefs.PasswordField), true);
			string url = pe.Strings.ReadSafe(PwDefs.UrlField);
			if(url.Length > 0)
			{
				FrameworkElement row = AddRow(sp, "URL", pe.Strings.Get(PwDefs.UrlField), false);
			}

			Totp.Params tp = Totp.FromEntry(pe);
			if(tp != null)
			{
				sp.Children.Add(new TextBlock { Text = L.T("Code TOTP"), Style = (Style)FindResource("FieldLabel") });
				DockPanel d = new DockPanel();
				Button cp = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcCopy"), ToolTip = L.T("Copier le code") };
				DockPanel.SetDock(cp, Dock.Right);
				ProgressBar pb = new ProgressBar { Width = 60, Maximum = tp.Period, Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
				DockPanel.SetDock(pb, Dock.Right);
				TextBlock code = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 22, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
				code.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
				d.Children.Add(cp); d.Children.Add(pb); d.Children.Add(code);
				sp.Children.Add(d);
				string cur = "";
				m_totpTick = delegate
				{
					try { int left; cur = Totp.Code(tp, DateTime.UtcNow, out left); code.Text = cur.Substring(0, cur.Length / 2) + " " + cur.Substring(cur.Length / 2); pb.Value = left; }
					catch(Exception) { code.Text = L.T("secret invalide"); }
				};
				m_totpTick();
				cp.Click += delegate { SafeClipboard.Copy(cur); Status(L.T("Code TOTP copié.")); };
			}

			foreach(var kv in pe.Strings)
			{
				if(PwDefs.IsStandardField(kv.Key) || kv.Key == "otp" || kv.Key.StartsWith("TimeOtp-")) continue;
				AddRow(sp, kv.Key, kv.Value, kv.Value.IsProtected);
			}

			string notes = pe.Strings.ReadSafe(PwDefs.NotesField);
			if(notes.Length > 0)
			{
				sp.Children.Add(new TextBlock { Text = L.T("Notes"), Style = (Style)FindResource("FieldLabel") });
				Border nb = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12) };
				nb.Child = new TextBox { Text = notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), MinHeight = 0 };
				sp.Children.Add(nb);
			}

			var tags = pe.Tags.Where(t => !Categories.Names.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
			if(tags.Count > 0)
			{
				sp.Children.Add(new TextBlock { Text = "Tags", Style = (Style)FindResource("FieldLabel") });
				WrapPanel wp = new WrapPanel();
				foreach(string t in tags)
				{
					Border chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6) };
					chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
					chip.Child = new TextBlock { Text = t, FontSize = 12 };
					wp.Children.Add(chip);
				}
				sp.Children.Add(wp);
			}

			if(pe.Binaries.UCount > 0)
			{
				sp.Children.Add(new TextBlock { Text = L.T("Pièces jointes"), Style = (Style)FindResource("FieldLabel") });
				foreach(var kv in pe.Binaries) sp.Children.Add(new TextBlock { Text = "📎 " + kv.Key });
			}

			TextBlock meta = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 18, 0, 0), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
				Text = L.T("Créée le ") + pe.CreationTime.ToLocalTime().ToString("g") + "\n" + L.T("Modifiée le ") + pe.LastModificationTime.ToLocalTime().ToString("g") +
					(pe.Expires ? "\n" + L.T("Expire le ") + pe.ExpiryTime.ToLocalTime().ToString("g") + (pe.ExpiryTime < DateTime.UtcNow ? L.T(" (expirée)") : "") : "") +
					(pe.History.UCount > 0 ? "\n" + pe.History.UCount + L.T(" version(s) dans l'historique") : "") };
			meta.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			sp.Children.Add(meta);

			m_details.Content = sp;
		}

		private FrameworkElement AddRow(Panel sp, string label, ProtectedString ps, bool bSecret)
		{
			if(ps == null || ps.IsEmpty) return null;
			sp.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("FieldLabel") });
			Border b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 4, 4, 4), BorderThickness = new Thickness(1) };
			b.SetResourceReference(Border.BackgroundProperty, "Surface");
			b.SetResourceReference(Border.BorderBrushProperty, "Border");
			DockPanel d = new DockPanel();
			Button cp = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcCopy"), ToolTip = L.T("Copier") };
			DockPanel.SetDock(cp, Dock.Right);
			d.Children.Add(cp);
			TextBlock val = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
			if(bSecret) val.FontFamily = new FontFamily("Consolas");
			if(bSecret)
			{
				ToggleButton eye = new ToggleButton { Style = (Style)FindResource("ToggleIcon"), Content = MakeIcon("IcEye"), ToolTip = L.T("Afficher / masquer") };
				DockPanel.SetDock(eye, Dock.Right);
				d.Children.Add(eye);
				val.Text = "••••••••••••";
				eye.Checked += delegate { val.Text = ps.ReadString(); };
				eye.Unchecked += delegate { val.Text = "••••••••••••"; };
			}
			else val.Text = ps.ReadString();
			if(label == "URL")
			{
				Button go = new Button { Style = (Style)FindResource("IconButton"), Content = MakeIcon("IcGlobe"), ToolTip = L.T("Ouvrir") };
				DockPanel.SetDock(go, Dock.Right);
				d.Children.Insert(0, go);
				go.Click += delegate { OpenUrl(); };
			}
			d.Children.Add(val);
			cp.Click += delegate { SafeClipboard.Copy(ps.ReadString()); Status(label + L.T(" copié.")); };
			b.Child = d;
			sp.Children.Add(b);
			return b;
		}

		// ================= actions =================

		private void ShowQr()
		{
			if(Selected == null) return;
			new QrWindow(Selected.Entry) { Owner = this }.ShowDialog();
		}

		private void CopyField(string strField)
		{
			if(Selected == null) return;
			SafeClipboard.Copy(Selected.Entry.Strings.ReadSafe(strField));
			Status((strField == PwDefs.PasswordField ? L.T("Mot de passe") : L.T("Nom d'utilisateur")) + L.T(" copié."));
		}

		private void OpenUrl()
		{
			if(Selected == null) return;
			string url = Selected.Entry.Strings.ReadSafe(PwDefs.UrlField).Trim();
			if(url.Length == 0) return;
			if(!url.Contains("://")) url = "https://" + url;
			Uri u;
			if(!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https"))
			{ Dialog.Info(this, "URL", L.T("Seules les adresses http(s) peuvent être ouvertes depuis KeePass Cerberus.")); return; }
			try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
			catch(Exception ex) { Dialog.Error(this, "URL", ex.Message); }
		}

		private PwGroup TargetGroup()
		{
			Categories.Cat c = m_side.SelectedItem as Categories.Cat;
			if(c != null && c.IsFolder && c.Group != null) return c.Group;
			return Session.Db.RootGroup;
		}

		private async void NewEntry()
		{
			if(!Session.IsOpen) return;
			PwEntry pe = new PwEntry(true, true);
			Categories.Cat c = m_side.SelectedItem as Categories.Cat;
			if(c != null && !c.IsAll && !c.IsNone && !c.IsFolder && !c.IsBin) pe.AddTag(c.Name);
			EntryWindow w = new EntryWindow(pe, true) { Owner = this };
			if(w.ShowDialog() != true) return;
			TargetGroup().AddEntry(pe, true);
			await ChangedAsync();
			RefreshSide();
			SelectEntry(pe);
		}

		private async void EditEntry()
		{
			if(Selected == null) return;
			PwEntry pe = Selected.Entry;
			EntryWindow w = new EntryWindow(pe, false) { Owner = this };
			if(w.ShowDialog() != true) return;
			await ChangedAsync();
			RefreshSide();
			SelectEntry(pe);
		}

		private async void DuplicateEntry()
		{
			if(Selected == null) return;
			PwEntry src = Selected.Entry;
			PwEntry pe = src.Duplicate();
			pe.Strings.Set(PwDefs.TitleField, new ProtectedString(Session.Db.MemoryProtection.ProtectTitle, src.Strings.ReadSafe(PwDefs.TitleField) + L.T(" (copie)")));
			src.ParentGroup.AddEntry(pe, true);
			await ChangedAsync();
			RefreshSide();
			SelectEntry(pe);
		}

		private async Task DeleteEntryAsync()
		{
			if(Selected == null) return;
			PwEntry pe = Selected.Entry;
			PwGroup bin = Session.RecycleBin;
			bool bPermanent = !Session.Db.RecycleBinEnabled || (bin != null && pe.IsContainedIn(bin));
			string title = pe.Strings.ReadSafe(PwDefs.TitleField);
			if(!Dialog.Confirm(this, bPermanent ? L.T("Supprimer définitivement") : L.T("Mettre à la corbeille"),
				"« " + title + " » " + (bPermanent ? L.T("sera supprimée définitivement.") : L.T("sera déplacée dans la corbeille.")), L.T("Supprimer"), true)) return;
			Session.DeleteEntry(pe);
			await ChangedAsync();
			RefreshSide();
		}

		private void SelectEntry(PwEntry pe)
		{
			if(m_list.ItemsSource is IEnumerable<EntryVm> vms)
			{
				EntryVm v = vms.FirstOrDefault(x => x.Entry == pe);
				if(v == null) { m_side.SelectedIndex = 0; v = (m_list.ItemsSource as IEnumerable<EntryVm>)?.FirstOrDefault(x => x.Entry == pe); }
				if(v != null) { m_list.SelectedItem = v; m_list.ScrollIntoView(v); }
			}
		}

		private async void SetCategory(string strCat)
		{
			if(Selected == null) return;
			PwEntry pe = Selected.Entry;
			pe.CreateBackup(Session.Db);
			if(!Categories.Set(pe, strCat)) return;
			pe.Touch(true, false);
			pe.MaintainBackups(Session.Db);
			await ChangedAsync();
			RefreshSide();
		}

		private async void AutoClassify()
		{
			if(!Session.IsOpen) return;
			int n = 0;
			foreach(PwEntry pe in Session.AllEntries(false).ToList())
			{
				if(Categories.Of(pe) != null) continue;
				string c = Categories.Guess(pe);
				if(c == null) continue;
				pe.CreateBackup(Session.Db);
				Categories.Set(pe, c);
				pe.Touch(true, false);
				pe.MaintainBackups(Session.Db);
				++n;
			}
			if(n > 0) { await ChangedAsync(); RefreshSide(); }
			Dialog.Info(this, L.T("Classement automatique"), n + L.T(" entrée(s) classée(s). Les catégories sont des tags KeePass ; ajustez-les avec un clic droit → Catégorie."));
		}

		private ContextMenu EntryMenu()
		{
			ContextMenu m = new ContextMenu();
			m.Items.Add(Mi(L.T("Copier le nom d'utilisateur"), "IcUser", "Ctrl+B", () => CopyField(PwDefs.UserNameField)));
			m.Items.Add(Mi(L.T("Copier le mot de passe"), "IcKey", "Ctrl+C", () => CopyField(PwDefs.PasswordField)));
			m.Items.Add(Mi(L.T("Ouvrir l'URL"), "IcGlobe", "Ctrl+U", OpenUrl));
			m.Items.Add(Mi(L.T("Saisie automatique"), "IcType", "Ctrl+V", async () => await AutoTypeSelectedAsync()));
			m.Items.Add(Mi(L.T("QR code (téléphone)…"), "IcQr", null, ShowQr));
			m.Items.Add(new Separator());
			m.Items.Add(Mi(L.T("Modifier…"), "IcEdit", L.T("Entrée"), EditEntry));
			m.Items.Add(Mi(L.T("Dupliquer"), "IcCopy", null, DuplicateEntry));
			MenuItem cat = Mi(L.T("Catégorie"), "IcPieces", null, null);
			foreach(string n in Categories.Names) { string c = n; MenuItem x = Mi(L.T(n), Categories.Icons[n], null, () => SetCategory(c)); x.IsChecked = Categories.Of(Selected.Entry) == n; cat.Items.Add(x); }
			cat.Items.Add(new Separator());
			cat.Items.Add(Mi(L.T("Aucune"), null, null, () => SetCategory(null)));
			m.Items.Add(cat);
			m.Items.Add(new Separator());
			m.Items.Add(Mi(L.T("Supprimer"), "IcTrash", L.T("Suppr"), async () => await DeleteEntryAsync()));
			return m;
		}

		private MenuItem Mi(string header, string icon, string gesture, Action a)
		{
			MenuItem mi = new MenuItem { Header = header, InputGestureText = gesture ?? "" };
			if(icon != null) mi.Icon = MakeIcon(icon, 15);
			if(a != null) mi.Click += delegate { a(); };
			return mi;
		}

		private ContextMenu BuildMenu()
		{
			bool open = Session.IsOpen;
			ContextMenu m = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Mouse };
			m.Items.Add(Mi(L.T("Nouvelle base…"), "IcNewDb", null, async () => await NewDatabaseAsync()));
			m.Items.Add(Mi(L.T("Ouvrir une base…"), "IcOpen", "Ctrl+O", async () => await OpenDialogAsync()));
			MenuItem save = Mi(L.T("Enregistrer"), "IcSave", "Ctrl+S", async () => await SaveAsync()); save.IsEnabled = open; m.Items.Add(save);
			MenuItem saveAs = Mi(L.T("Enregistrer sous…"), "IcSave", null, async () => await SaveAsAsync()); saveAs.IsEnabled = open; m.Items.Add(saveAs);
			m.Items.Add(new Separator());
			MenuItem ck = Mi(L.T("Changer la clé maître…"), "IcKey", null, async () => await ChangeKeyAsync()); ck.IsEnabled = open; m.Items.Add(ck);
			MenuItem ds = Mi(L.T("Paramètres de la base…"), "IcShield", null, async () => await DbSettingsAsync()); ds.IsEnabled = open; m.Items.Add(ds);
			MenuItem ac = Mi(L.T("Classer automatiquement"), "IcPieces", null, AutoClassify); ac.IsEnabled = open; m.Items.Add(ac);
			m.Items.Add(new Separator());
			MenuItem f = Mi(L.T("Afficher les dossiers"), "IcFolder", null, () => { Settings.Current.ShowFolders = !Settings.Current.ShowFolders; Settings.Save(); UpdateUi(); });
			f.IsChecked = Settings.Current.ShowFolders; m.Items.Add(f);
			MenuItem dp = Mi(L.T("Panneau de détails"), "IcList", null, () => { Settings.Current.ShowDetailsPane = !Settings.Current.ShowDetailsPane; Settings.Save(); UpdateUi(); });
			dp.IsChecked = Settings.Current.ShowDetailsPane; m.Items.Add(dp);
			m.Items.Add(Mi(L.T("Options…"), "IcSettings", null, () => new OptionsWindow { Owner = this }.ShowDialog()));
			m.Items.Add(Mi(L.T("Interface classique (KeePass)…"), "IcPieces", null, async () => await ClassicAsync()));
			m.Items.Add(new Separator());
			m.Items.Add(Mi(L.T("À propos"), "IcQuestion", null, () => Dialog.Info(this, "KeePass Cerberus",
				L.T("Version") + " " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " — " + L.T("basé sur KeePass 2.62 (Dominik Reichl, GPL v2+).") + "\n" + L.T("Cascade de chiffrement, Argon2id jusqu'à 4 Gio, facteurs PIN / motif / Shamir, protection anti-capture.") + "\n" + L.T("Format KDBX 4 : les bases KeePass existantes s'ouvrent sans conversion."))));
			m.Items.Add(Mi(L.T("Quitter"), null, null, Close));
			return m;
		}

		// ================= database lifecycle =================

		public async void StartWith(string strArg)
		{
			string p = (strArg != null && File.Exists(strArg)) ? strArg :
				(Settings.Current.LastDatabase != null && File.Exists(Settings.Current.LastDatabase) ? Settings.Current.LastDatabase : null);
			if(p != null) { m_lockedPath = p; UpdateUi(); await OpenAsync(p); }
		}

		private async Task OpenDialogAsync()
		{
			if(!await CloseCurrentAsync()) return;
			var ofd = new OpenFileDialog { Title = L.T("Ouvrir une base"), Filter = L.T("Bases KeePass (*.kdbx)|*.kdbx|Tous les fichiers (*.*)|*.*") };
			if(ofd.ShowDialog(this) != true) return;
			await OpenAsync(ofd.FileName);
		}

		private async Task OpenAsync(string strPath)
		{
			if(Session.IsOpen && !await CloseCurrentAsync()) return;
			UnlockWindow w = new UnlockWindow(strPath) { Owner = this };
			if(w.ShowDialog() != true || w.Result == null) { m_lockedPath = File.Exists(strPath) ? strPath : null; UpdateUi(); return; }
			m_lockedPath = null;
			m_search.Text = "";
			Session.Attach(w.Result, strPath);
			Status(L.T("Base ouverte : ") + Session.CipherName(Session.Db.DataCipherUuid) + " · " + Session.DescribeKdf(Session.Db.KdfParameters));
			await Task.Yield();
		}

		private async Task NewDatabaseAsync()
		{
			if(!await CloseCurrentAsync()) return;
			var sfd = new SaveFileDialog { Title = L.T("Créer une base"), Filter = L.T("Base KeePass (*.kdbx)|*.kdbx"), FileName = "MesMotsDePasse.kdbx" };
			if(sfd.ShowDialog(this) != true) return;
			CreateKeyWindow w = new CreateKeyWindow(sfd.FileName, true) { Owner = this };
			if(w.ShowDialog() != true) return;

			PwDatabase db = new PwDatabase();
			db.New(KeePassLib.Serialization.IOConnectionInfo.FromPath(sfd.FileName), w.ResultKey);
			db.Name = System.IO.Path.GetFileNameWithoutExtension(sfd.FileName);
			db.RootGroup.Name = db.Name;
			Session.ApplyProfile(db, w.ResultProfile ?? Session.Profiles[2]);
			Busy(L.T("Création et chiffrement de la base…"));
			try { await Task.Run(() => db.Save(new KeePassLib.Interfaces.NullStatusLogger())); }
			catch(Exception ex) { Busy(null); Dialog.Error(this, L.T("Création impossible"), ex.Message); return; }
			Busy(null);
			Session.Attach(db, sfd.FileName);
			Status(L.T("Base créée : ") + Session.CipherName(db.DataCipherUuid) + " · " + Session.DescribeKdf(db.KdfParameters));
		}

		private async Task<bool> SaveAsync()
		{
			if(!Session.IsOpen) return true;
			Busy(L.T("Enregistrement et chiffrement…"));
			try { await Task.Run(() => Session.Save()); Status(L.T("Enregistré.")); return true; }
			catch(Exception ex) { Dialog.Error(this, L.T("Enregistrement impossible"), ex.Message); return false; }
			finally { Busy(null); Session.RaiseChanged(); }
		}

		private async Task SaveAsAsync()
		{
			if(!Session.IsOpen) return;
			var sfd = new SaveFileDialog { Title = L.T("Enregistrer sous"), Filter = L.T("Base KeePass (*.kdbx)|*.kdbx"), FileName = System.IO.Path.GetFileName(Session.Path) };
			if(sfd.ShowDialog(this) != true) return;
			Busy(L.T("Enregistrement…"));
			try
			{
				var ioc = KeePassLib.Serialization.IOConnectionInfo.FromPath(sfd.FileName);
				await Task.Run(() => Session.Db.SaveAs(ioc, true, new KeePassLib.Interfaces.NullStatusLogger()));
				PwDatabase db = Session.Db;
				var ka = Settings.Current.GetKeyAssoc(Session.Path);
				if(ka != null) Settings.Current.SetKeyAssoc(new KeyAssoc { DatabasePath = sfd.FileName, KeyFilePath = ka.KeyFilePath, CerberusFactors = ka.CerberusFactors,
					FactorFlags = ka.FactorFlags, GridSize = ka.GridSize, WindowsAccount = ka.WindowsAccount });
				Session.SetPath(sfd.FileName);
				Settings.Current.AddRecent(sfd.FileName);
				Settings.Save();
				Status(L.T("Enregistré sous ") + sfd.FileName);
			}
			catch(Exception ex) { Dialog.Error(this, L.T("Enregistrement impossible"), ex.Message); }
			finally { Busy(null); Session.RaiseChanged(); }
		}

		private async Task ChangedAsync()
		{
			Session.Db.Modified = true;
			if(Settings.Current.AutoSave) await SaveAsync();
			else Session.RaiseChanged();
		}

		private async Task ChangeKeyAsync()
		{
			if(!Session.IsOpen) return;
			CreateKeyWindow w = new CreateKeyWindow(Session.Path, false) { Owner = this };
			if(w.ShowDialog() != true) return;
			Session.Db.MasterKey = w.ResultKey;
			Session.Db.MasterKeyChanged = DateTime.UtcNow;
			await ChangedAsync();
			if(!Settings.Current.AutoSave) Dialog.Info(this, L.T("Clé maître"), L.T("La nouvelle clé sera appliquée au prochain enregistrement."));
		}

		private async Task DbSettingsAsync()
		{
			if(!Session.IsOpen) return;
			DbSettingsWindow w = new DbSettingsWindow(Session.Db) { Owner = this };
			if(w.ShowDialog() != true) return;
			await ChangedAsync();
			RefreshSide();
			Status(L.T("Paramètres : ") + Session.CipherName(Session.Db.DataCipherUuid) + " · " + Session.DescribeKdf(Session.Db.KdfParameters));
		}

		/// <summary>Close the current database, asking to save if needed.</summary>
		private async Task<bool> CloseCurrentAsync()
		{
			if(!Session.IsOpen) return true;
			if(Session.Db.Modified)
			{
				if(Settings.Current.AutoSave) { if(!await SaveAsync()) return false; }
				else
				{
					bool save = Dialog.Show(this, L.T("Modifications non enregistrées"), L.T("Enregistrer les modifications avant de fermer la base ?"), L.T("Enregistrer"), L.T("Ne pas enregistrer"));
					if(save && !await SaveAsync()) return false;
				}
			}
			Session.Close();
			return true;
		}

		private async Task LockAsync()
		{
			if(!Session.IsOpen) return;
			string p = Session.Path;
			if(Session.Db.Modified && !await SaveAsync())
			{
				Status(L.T("Verrouillage annulé : l'enregistrement a échoué."));
				return;
			}
			foreach(Window w in OwnedWindows.Cast<Window>().ToList()) { try { w.Close(); } catch(Exception) { } }
			SafeClipboard.ClearIfOurs();
			Session.Close();
			m_lockedPath = p;
			UpdateUi();
			Status(L.T("Base verrouillée."));
		}

		private async Task ClassicAsync()
		{
			string exe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "classic", "KeePassCerberus.exe");
			if(!File.Exists(exe)) { Dialog.Error(this, L.T("Interface classique"), L.T("Introuvable : ") + exe); return; }
			string p = Session.Path ?? m_lockedPath;
			if(Session.IsOpen)
			{
				if(!Dialog.Confirm(this, L.T("Interface classique"), L.T("La base va être verrouillée ici puis ouverte dans l'interface classique de KeePass (fonctions avancées : import/export, déclencheurs, plugins…)."), L.T("Ouvrir"))) return;
				await LockAsync();
				if(Session.IsOpen) return;
			}
			try { Process.Start(new ProcessStartInfo(exe, p != null ? "\"" + p + "\"" : "") { UseShellExecute = false }); }
			catch(Exception ex) { Dialog.Error(this, L.T("Interface classique"), ex.Message); }
		}

		// ================= auto-type =================

		[System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
		[System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
		private const int HotKeyId = 0xC3B0;
		private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
		private bool m_bTyping;

		private void RegisterGlobalHotKey()
		{
			IntPtr h = Native.Handle(this);
			var src = System.Windows.Interop.HwndSource.FromHwnd(h);
			src.AddHook(WndProc);
			if(!RegisterHotKey(h, HotKeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x41 /* A */))
				Status(L.T("Raccourci global Ctrl+Alt+A indisponible (déjà utilisé par une autre application)."));
			Closed += delegate { UnregisterHotKey(h, HotKeyId); };
		}

		private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			const int WM_HOTKEY = 0x0312;
			if(msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
			{
				handled = true;
				IntPtr target = AutoType.GetForegroundWindow();
				Dispatcher.BeginInvoke(new Action(async () => await AutoTypeGlobalAsync(target)));
			}
			return IntPtr.Zero;
		}

		/// <summary>Ctrl+Alt+A from any window: pick the entry matching the active window.</summary>
		private async Task AutoTypeGlobalAsync(IntPtr target)
		{
			if(m_bTyping) return;
			if(target == IntPtr.Zero || AutoType.IsOwnWindow(target)) { Status(L.T("Saisie automatique : placez-vous d'abord dans la fenêtre cible.")); return; }
			string title = AutoType.WindowTitle(target);
			if(!Session.IsOpen)
			{
				Activate();
				if(m_lockedPath == null) { Dialog.Info(this, L.T("Saisie automatique"), L.T("Ouvrez d'abord une base.")); return; }
				await OpenAsync(m_lockedPath);
				if(!Session.IsOpen) return;
			}
			bool bExplicit;
			List<PwEntry> l = AutoType.FindForWindow(title, out bExplicit);
			PwEntry pe;
			if(bExplicit && l.Count == 1) pe = l[0]; // user-defined association: type directly
			else
			{
				// Guessed from the title/URL (spoofable by a web page), several matches
				// or none: always ask. Guesses are listed first, then all other entries.
				var rest = Session.AllEntries(false).Where(AutoType.IsEnabled).Where(x => !l.Contains(x));
				List<PwEntry> all = l.Concat(rest).ToList();
				Activate();
				pe = PickEntry(all, title, l.Count);
				if(pe == null) return;
			}
			await TypeIntoAsync(pe, target);
		}

		private async Task AutoTypeSelectedAsync()
		{
			if(Selected == null || m_bTyping) return;
			PwEntry pe = Selected.Entry;
			if(!AutoType.IsEnabled(pe)) { Dialog.Info(this, L.T("Saisie automatique"), L.T("La saisie automatique est désactivée pour cette entrée (onglet « Saisie auto »).")); return; }
			// Like KeePass: minimise, then type into the window that becomes active.
			m_bTyping = true;
			WindowState = WindowState.Minimized;
			await Task.Delay(350);
			IntPtr target = AutoType.GetForegroundWindow();
			if(target == IntPtr.Zero || AutoType.IsOwnWindow(target))
			{
				WindowState = WindowState.Normal;
				Status(L.T("Saisie automatique : aucune fenêtre cible trouvée."));
				m_bTyping = false;
				return;
			}
			await TypeIntoAsync(pe, target);
		}

		private async Task TypeIntoAsync(PwEntry pe, IntPtr target)
		{
			string seq = AutoType.SequenceFor(pe, AutoType.WindowTitle(target));
			m_bTyping = true;
			try
			{
				await Task.Run(() => AutoType.Perform(pe, seq, target));
				Status(L.T("Saisie automatique effectuée : ") + pe.Strings.ReadSafe(PwDefs.TitleField));
			}
			catch(Exception ex) { Status(L.T("Saisie automatique échouée : ") + ex.Message); }
			finally { m_bTyping = false; }
		}

		private PwEntry PickEntry(List<PwEntry> l, string title, int nSuggested)
		{
			CerbWindow w = new CerbWindow { Title = L.T("Saisie automatique"), Width = 480, Height = 460, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
			DockPanel d = new DockPanel { Margin = new Thickness(22, 18, 22, 18) };
			StackPanel head = new StackPanel();
			head.Children.Add(new TextBlock { Text = L.T("Choisir l'entrée à saisir"), FontSize = 17, FontWeight = FontWeights.SemiBold });
			TextBlock sub = new TextBlock { Text = L.T("Fenêtre : ") + title + (nSuggested > 0 ? "\n" + L.T("Suggestions d'après le titre en tête de liste : vérifiez que vous êtes sur le bon site.") : ""), Margin = new Thickness(0, 2, 0, 10), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None };
			sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
			head.Children.Add(sub);
			TextBox filter = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = L.T("Filtrer") };
			head.Children.Add(filter);
			DockPanel.SetDock(head, Dock.Top);
			d.Children.Add(head);
			StackPanel bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
			Button cancel = new Button { Content = L.T("Annuler"), MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
			Button ok = new Button { Content = L.T("Saisir"), MinWidth = 110, IsDefault = true, Style = (Style)FindResource("AccentButton") };
			bar.Children.Add(cancel); bar.Children.Add(ok);
			DockPanel.SetDock(bar, Dock.Bottom);
			d.Children.Add(bar);
			ListBox lb = new ListBox { ItemTemplate = EntryTemplate() };
			d.Children.Add(lb);
			var vms = l.Take(nSuggested).Select(ToVm).OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase)
				.Concat(l.Skip(nSuggested).Select(ToVm).OrderBy(v => v.Title, StringComparer.CurrentCultureIgnoreCase)).ToList();
			Action apply = delegate
			{
				string q = filter.Text.Trim();
				var f = q.Length == 0 ? vms : vms.Where(v => v.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
					(v.Subtitle ?? "").IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
				lb.ItemsSource = f;
				if(f.Count > 0) lb.SelectedIndex = 0;
			};
			filter.TextChanged += delegate { apply(); };
			filter.PreviewKeyDown += (s, e) => { if(e.Key == Key.Down) { lb.Focus(); e.Handled = true; } };
			apply();
			PwEntry result = null;
			ok.Click += delegate { if(lb.SelectedItem is EntryVm v) { result = v.Entry; w.Close(); } };
			cancel.Click += delegate { w.Close(); };
			lb.MouseDoubleClick += delegate { if(lb.SelectedItem is EntryVm v) { result = v.Entry; w.Close(); } };
			w.Content = d;
			w.Loaded += delegate { filter.Focus(); };
			w.ShowDialog();
			return result;
		}

		// ================= system events =================

		private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
		{
			if(e.Reason == SessionSwitchReason.SessionLock && Settings.Current.LockOnSessionLock)
				Dispatcher.BeginInvoke(new Action(async () => await LockAsync()));
		}

		private void OnPowerMode(object sender, PowerModeChangedEventArgs e)
		{
			if(e.Mode == PowerModes.Suspend && Settings.Current.LockOnSessionLock)
				Dispatcher.BeginInvoke(new Action(async () => await LockAsync()));
		}

		private async void OnClosing(object sender, CancelEventArgs e)
		{
			if(m_bClosing) return;
			Settings.Current.WindowMaximized = WindowState == WindowState.Maximized;
			if(WindowState == WindowState.Normal) { Settings.Current.WindowWidth = Width; Settings.Current.WindowHeight = Height; }
			if(Session.IsOpen && Session.Db.Modified)
			{
				e.Cancel = true;
				if(await CloseCurrentAsync()) { m_bClosing = true; Close(); }
				return;
			}
			Session.Close();
			Settings.Save();
		}

		private async void OnKeys(object sender, KeyEventArgs e)
		{
			bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
			if(ctrl && e.Key == Key.O) { e.Handled = true; await OpenDialogAsync(); return; }
			if(!Session.IsOpen) return;
			if(ctrl && e.Key == Key.F) { m_search.Focus(); m_search.SelectAll(); e.Handled = true; }
			else if(ctrl && e.Key == Key.S) { e.Handled = true; await SaveAsync(); }
			else if(ctrl && e.Key == Key.L) { e.Handled = true; await LockAsync(); }
			else if(ctrl && e.Key == Key.N) { e.Handled = true; NewEntry(); }
			else if(ctrl && e.Key == Key.B) { e.Handled = true; CopyField(PwDefs.UserNameField); }
			else if(ctrl && e.Key == Key.U) { e.Handled = true; OpenUrl(); }
			else if(ctrl && e.Key == Key.V && !(Keyboard.FocusedElement is TextBox)) { e.Handled = true; await AutoTypeSelectedAsync(); }
			else if(ctrl && e.Key == Key.C && !(Keyboard.FocusedElement is TextBox)) { e.Handled = true; CopyField(PwDefs.PasswordField); }
			else if(e.Key == Key.Delete && m_list.IsKeyboardFocusWithin) { e.Handled = true; await DeleteEntryAsync(); }
			else if(e.Key == Key.Enter && m_list.IsKeyboardFocusWithin) { e.Handled = true; EditEntry(); }
			else if(e.Key == Key.Escape && m_search.IsKeyboardFocusWithin) { m_search.Text = ""; m_list.Focus(); }
		}
	}

	// ===== converters =====
	internal sealed class ResKey : System.Windows.Data.IValueConverter
	{
		public object Convert(object v, Type t, object p, CultureInfo c) { return v is string s ? Application.Current.TryFindResource(s) : null; }
		public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
	}

	internal sealed class HexBrush : System.Windows.Data.IValueConverter
	{
		public Brush Soft(string hex)
		{
			Color c = (Color)ColorConverter.ConvertFromString(hex);
			return new SolidColorBrush(Color.FromArgb(App.IsDark ? (byte)46 : (byte)34, c.R, c.G, c.B));
		}
		public object Convert(object v, Type t, object p, CultureInfo c)
		{
			if(!(v is string s)) return null;
			if((p as string) == "soft") return Soft(s);
			return new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
		}
		public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
	}

	internal sealed class DepthMargin : System.Windows.Data.IValueConverter
	{
		public object Convert(object v, Type t, object p, CultureInfo c) { int d = v is int i ? i : 0; return new Thickness(d * 14, 0, 11, 0); }
		public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
	}
}
