/*
  KeePass Cerberus - main window: theme + categories sidebar.
  Licensed under the GNU GPL v2 or later (same as KeePass).

  Categories are plain KeePass tags (e.g. "Banque"), so they stay
  readable by any KDBX client and need no format change.
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using KeePass.Cerberus;
using KeePass.Util;

using KeePassLib;
using KeePassLib.Collections;

namespace KeePass.Forms
{
	public partial class MainForm
	{
		private sealed class CerbCategory
		{
			public readonly string Name;   // also the tag
			public readonly string Glyph;  // Segoe MDL2 Assets / Fluent icon
			public readonly Color Color;
			public int Count;
			public CerbCategory(string strName, string strGlyph, Color clr)
			{ Name = strName; Glyph = strGlyph; Color = clr; }
		}

		private const string CerbAll = "Toutes les entrées";
		private const string CerbNone = "Non classées";

		private readonly List<CerbCategory> m_lCerbCats = new List<CerbCategory> {
			new CerbCategory(CerbAll, "", Color.FromArgb(64, 140, 255)),
			new CerbCategory("Connexions", "", Color.FromArgb(80, 170, 255)),
			new CerbCategory("Banque", "", Color.FromArgb(46, 196, 140)),
			new CerbCategory("Cartes", "", Color.FromArgb(255, 170, 60)),
			new CerbCategory("Identités", "", Color.FromArgb(190, 120, 255)),
			new CerbCategory("Notes", "", Color.FromArgb(255, 210, 80)),
			new CerbCategory("Wi-Fi", "", Color.FromArgb(60, 200, 220)),
			new CerbCategory("Serveurs", "", Color.FromArgb(255, 110, 110)),
			new CerbCategory("Licences", "", Color.FromArgb(160, 200, 90)),
			new CerbCategory(CerbNone, "", Color.FromArgb(140, 146, 165))
		};

		private const string CfgShowFolders = "Cerberus.ShowFolders";
		private Label m_lblCerbFolders = null;
		private PwDatabase m_pdCerbLast = null;
		private ListBox m_lbCerbCats = null;
		private bool m_bCerbSyncSel = false;

		private readonly System.Text.StringBuilder m_sbCerbErrors = new System.Text.StringBuilder();

		private void CerbStep(string strName, Action a)
		{
			try { a(); }
			catch(Exception ex)
			{
				m_sbCerbErrors.AppendLine(strName + " : " + ex.GetType().Name + " — " + ex.Message);
			}
		}

		private void CerberusInitUi()
		{
			// The capture switch comes first: it must exist even if another step fails.
			CerbStep("Bascule anti-capture", CerberusCaptureSwitch);
			CerbStep("Menu Affichage", CerberusViewMenu);
			CerbStep("Catégories", BuildCategorySidebar);
			CerbStep("Menu Catégorie", BuildCategoryContextMenu);
			CerbStep("Barre d'outils", CerberusModernToolbar);
			CerbStep("Thème", delegate
			{
				CerberusTheme.Apply(this);
				CerberusTheme.ApplyContextMenu(m_ctxPwList);
				CerberusTheme.ApplyContextMenu(m_ctxGroupList);
				m_lvEntries.Font = CerberusTheme.UiFont(9.5f, FontStyle.Regular);
				m_tvGroups.Font = CerberusTheme.UiFont(9.5f, FontStyle.Regular);
				m_tvGroups.ItemHeight = Math.Max(m_tvGroups.ItemHeight, (int)(m_tvGroups.Font.Height * 1.6));
				m_tvGroups.ShowLines = false;
				m_tvGroups.FullRowSelect = true;
			});

			if(m_sbCerbErrors.Length > 0)
				KeePassLib.Utility.MessageService.ShowWarning("KeePass Cerberus — erreurs d'initialisation de l'interface :" +
					KeePassLib.Utility.MessageService.NewParagraph + m_sbCerbErrors.ToString());
		}

		/// <summary>Always-visible check box in the status bar.</summary>
		private void CerberusCaptureSwitch()
		{
			ToolStripButton tsb = new ToolStripButton();
			tsb.CheckOnClick = true;
			tsb.Checked = CerberusCapture.Enabled;
			tsb.DisplayStyle = ToolStripItemDisplayStyle.Text;
			tsb.Alignment = ToolStripItemAlignment.Right;
			Action upd = delegate
			{
				tsb.Text = (tsb.Checked ? "☑ " : "☐ ") + CerberusCapture.Label;
				tsb.ToolTipText = (tsb.Checked ? "Protection active : fenêtres noires dans les captures. Cliquer pour désactiver." :
					"Protection désactivée : captures d'écran possibles. Cliquer pour activer.");
			};
			upd();
			bool bSync = false;
			tsb.CheckedChanged += delegate
			{
				upd();
				if(bSync) return;
				CerberusCapture.Set(tsb.Checked);
				SetStatusEx(tsb.Checked ? "Protection anti-capture activée." :
					"Protection anti-capture DÉSACTIVÉE : les fenêtres sont visibles dans les captures.");
			};
			CerberusCapture.Changed += delegate
			{
				bSync = true; tsb.Checked = CerberusCapture.Enabled; bSync = false;
			};
			m_statusMain.Items.Add(tsb);
		}

		private void CerberusViewMenu()
		{
				ToolStripMenuItem tsmiTheme = new ToolStripMenuItem("Thème sombre (Cerberus)");
				tsmiTheme.Checked = CerberusTheme.Dark;
				tsmiTheme.Click += delegate
				{
					CerberusTheme.SetDarkForNextStart(!tsmiTheme.Checked);
					tsmiTheme.Checked = !tsmiTheme.Checked;
					KeePassLib.Utility.MessageService.ShowInfo(
						"Le changement de thème s'appliquera au prochain démarrage.");
				};
				ToolStripMenuItem tsmiCapture = new ToolStripMenuItem("Protection anti-capture d'écran");
				tsmiCapture.Checked = CerberusCapture.Enabled;
				tsmiCapture.Click += delegate { CerberusCapture.Set(!CerberusCapture.Enabled); };
				CerberusCapture.Changed += delegate { tsmiCapture.Checked = CerberusCapture.Enabled; };

				ToolStripMenuItem tsmiFolders = new ToolStripMenuItem("Afficher les dossiers");
				tsmiFolders.Checked = Program.Config.CustomConfig.GetBool(CfgShowFolders, false);
				tsmiFolders.Click += delegate
				{
					tsmiFolders.Checked = !tsmiFolders.Checked;
					Program.Config.CustomConfig.SetBool(CfgShowFolders, tsmiFolders.Checked);
					CerberusShowFolders(tsmiFolders.Checked);
				};

				m_menuView.DropDownItems.Insert(0, tsmiTheme);
				m_menuView.DropDownItems.Insert(1, tsmiCapture);
				m_menuView.DropDownItems.Insert(2, tsmiFolders);
				m_menuView.DropDownItems.Insert(3, new ToolStripSeparator());
		}

		internal static Bitmap CerbGlyph(string strGlyph, int cx, Color clr)
		{
			Bitmap bmp = new Bitmap(cx, cx);
			using(Graphics g = Graphics.FromImage(bmp))
			{
				g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
				g.Clear(Color.Transparent);
				using(Font f = new Font("Segoe MDL2 Assets", cx * 0.62f, GraphicsUnit.Pixel))
				using(StringFormat sf = new StringFormat { Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center })
				using(SolidBrush br = new SolidBrush(clr))
					g.DrawString(strGlyph, f, br, new RectangleF(0, 1, cx, cx), sf);
			}
			return bmp;
		}

		private void CerberusModernToolbar()
		{
			int cx = KeePass.UI.DpiUtil.ScaleIntY(20);
			Color clr = CerberusTheme.Text;
			Action<ToolStripItem, string> set = delegate(ToolStripItem it, string g)
			{
				if(it == null) return;
				it.Image = CerbGlyph(g, cx, clr);
				it.ImageScaling = ToolStripItemImageScaling.None;
				it.Padding = new Padding(4, 2, 4, 2);
			};
			set(m_tbNewDatabase, "");
			set(m_tbOpenDatabase, "");
			set(m_tbSaveDatabase, "");
			set(m_tbSaveAll, "");
			set(m_tbAddEntry, "");
			set(m_tbCopyUserName, "");
			set(m_tbCopyPassword, "");
			set(m_tbOpenUrl, "");
			set(m_tbCopyUrl, "");
			set(m_tbAutoType, "");
			set(m_tbFind, "");
			set(m_tbEntryViewsDropDown, "");
			set(m_tbLockWorkspace, "");
			set(m_tbCloseTab, "");
			m_toolMain.ImageScalingSize = new Size(cx, cx);
			m_toolMain.Padding = new Padding(6, 4, 6, 4);
			m_toolMain.GripStyle = ToolStripGripStyle.Hidden;
			m_tbQuickFind.Font = CerberusTheme.UiFont(10f, FontStyle.Regular);
			m_tbQuickFind.Size = new Size(KeePass.UI.DpiUtil.ScaleIntX(220), m_tbQuickFind.Height);
		}

		private void BuildCategorySidebar()
		{
			Control pnl = m_splitVertical.Panel1;

			m_lbCerbCats = new ListBox();
			m_lbCerbCats.DrawMode = DrawMode.OwnerDrawFixed;
			m_lbCerbCats.BorderStyle = BorderStyle.None;
			m_lbCerbCats.IntegralHeight = false;
			m_lbCerbCats.Font = CerberusTheme.UiFont(10f, FontStyle.Regular);
			m_lbCerbCats.ItemHeight = (int)(m_lbCerbCats.Font.Height * 1.9);
			m_lbCerbCats.Dock = DockStyle.Top;
			m_lbCerbCats.Height = m_lbCerbCats.ItemHeight * m_lCerbCats.Count + 4;
			foreach(CerbCategory c in m_lCerbCats) m_lbCerbCats.Items.Add(c);
			m_lbCerbCats.DrawItem += this.OnCerbCatDraw;
			m_lbCerbCats.SelectedIndexChanged += this.OnCerbCatSelected;

			LinkLabel llAuto = new LinkLabel();
			llAuto.Text = "Classer automatiquement…";
			llAuto.AutoSize = false;
			llAuto.Dock = DockStyle.Top;
			llAuto.Height = (int)(llAuto.Font.Height * 1.8);
			llAuto.TextAlign = ContentAlignment.MiddleLeft;
			llAuto.Padding = new Padding(10, 0, 0, 0);
			llAuto.LinkBehavior = LinkBehavior.HoverUnderline;
			llAuto.LinkClicked += delegate { CerberusAutoClassify(); };

			Label lblCat = NewSidebarHeader("CATÉGORIES");
			Label lblGrp = NewSidebarHeader("DOSSIERS");
			m_lblCerbFolders = lblGrp;

			// Dock order: the last added Top-docked control is placed highest.
			pnl.Controls.Add(lblGrp);
			pnl.Controls.Add(llAuto);
			pnl.Controls.Add(m_lbCerbCats);
			pnl.Controls.Add(lblCat);

			CerberusTheme.ApplyOne(m_lbCerbCats);
			m_lbCerbCats.BackColor = CerberusTheme.Panel;
			pnl.BackColor = CerberusTheme.Panel;

			CerberusShowFolders(Program.Config.CustomConfig.GetBool(CfgShowFolders, false));

			m_tvGroups.AfterSelect += delegate
			{
				if(m_bCerbSyncSel) return;
				m_bCerbSyncSel = true;
				m_lbCerbCats.ClearSelected();
				m_bCerbSyncSel = false;
			};
		}

		private void CerberusShowFolders(bool bShow)
		{
			m_tvGroups.Visible = bShow;
			if(m_lblCerbFolders != null) m_lblCerbFolders.Visible = bShow;
		}

		private static Label NewSidebarHeader(string strText)
		{
			Label l = new Label();
			l.Text = strText;
			l.AutoSize = false;
			l.Dock = DockStyle.Top;
			l.Font = CerberusTheme.UiFont(8f, FontStyle.Bold);
			l.ForeColor = CerberusTheme.Muted;
			l.Height = (int)(l.Font.Height * 2.4);
			l.TextAlign = ContentAlignment.BottomLeft;
			l.Padding = new Padding(10, 0, 0, 4);
			return l;
		}

		private void OnCerbCatDraw(object sender, DrawItemEventArgs e)
		{
			if((e.Index < 0) || (e.Index >= m_lCerbCats.Count)) return;
			CerbCategory c = m_lCerbCats[e.Index];
			Graphics g = e.Graphics;
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

			bool bSel = ((e.State & DrawItemState.Selected) != 0);
			using(SolidBrush br = new SolidBrush(CerberusTheme.Panel)) g.FillRectangle(br, e.Bounds);
			Rectangle r = Rectangle.Inflate(e.Bounds, -6, -2);
			if(bSel)
			{
				using(SolidBrush br = new SolidBrush(CerberusTheme.Selection)) g.FillRectangle(br, r);
				using(SolidBrush br = new SolidBrush(CerberusTheme.Accent))
					g.FillRectangle(br, r.X, r.Y + 4, 3, r.Height - 8);
			}

			int h = r.Height;
			Rectangle rIcon = new Rectangle(r.X + 10, r.Y + (h - 22) / 2, 22, 22);
			using(SolidBrush br = new SolidBrush(Color.FromArgb(CerberusTheme.Dark ? 55 : 35, c.Color)))
				g.FillEllipse(br, rIcon);
			using(Font fIcon = new Font("Segoe MDL2 Assets", 9f))
				TextRenderer.DrawText(g, c.Glyph, fIcon, rIcon, c.Color,
					TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

			Rectangle rText = new Rectangle(rIcon.Right + 10, r.Y, r.Width - rIcon.Right - 50, h);
			TextRenderer.DrawText(g, c.Name, e.Font, rText, CerberusTheme.Text,
				TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

			string strCount = c.Count.ToString();
			Rectangle rCount = new Rectangle(r.Right - 46, r.Y, 40, h);
			using(Font fSmall = CerberusTheme.UiFont(8.5f, FontStyle.Regular))
				TextRenderer.DrawText(g, strCount, fSmall, rCount, CerberusTheme.Muted,
					TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
		}

		private static bool CerbHasCategory(PwEntry pe, List<CerbCategory> l)
		{
			foreach(string t in pe.Tags)
			{
				for(int i = 1; i < l.Count - 1; ++i)
				{
					if(string.Equals(t, l[i].Name, StringComparison.OrdinalIgnoreCase)) return true;
				}
			}
			return false;
		}

		private void CerberusRefreshCategories()
		{
			if(m_lbCerbCats == null) return;
			foreach(CerbCategory c in m_lCerbCats) c.Count = 0;

			PwDatabase pd = m_docMgr.ActiveDatabase;
			if((pd != null) && pd.IsOpen)
			{
				PwGroup pgBin = (pd.RecycleBinEnabled ? pd.RootGroup.FindGroup(pd.RecycleBinUuid, true) : null);
				foreach(PwEntry pe in pd.RootGroup.GetEntries(true))
				{
					if((pgBin != null) && pe.IsContainedIn(pgBin)) continue;
					m_lCerbCats[0].Count++;
					bool bAny = false;
					for(int i = 1; i < m_lCerbCats.Count - 1; ++i)
					{
						foreach(string t in pe.Tags)
						{
							if(string.Equals(t, m_lCerbCats[i].Name, StringComparison.OrdinalIgnoreCase))
							{ m_lCerbCats[i].Count++; bAny = true; break; }
						}
					}
					if(!bAny) m_lCerbCats[m_lCerbCats.Count - 1].Count++;
				}
			}
			m_lbCerbCats.Invalidate();

			// Folders hidden: show every entry when a database becomes active
			if((pd != null) && pd.IsOpen && !m_tvGroups.Visible && !ReferenceEquals(pd, m_pdCerbLast))
			{
				m_pdCerbLast = pd;
				if(m_lbCerbCats.SelectedIndex == 0) OnCerbCatSelected(m_lbCerbCats, EventArgs.Empty);
				else m_lbCerbCats.SelectedIndex = 0;
			}
			else if((pd == null) || !pd.IsOpen) m_pdCerbLast = null;
		}

		private void OnCerbCatSelected(object sender, EventArgs e)
		{
			if(m_bCerbSyncSel) return;
			int iSel = m_lbCerbCats.SelectedIndex;
			if(iSel < 0) return;

			PwDatabase pd = m_docMgr.ActiveDatabase;
			if((pd == null) || !pd.IsOpen) { m_lbCerbCats.ClearSelected(); return; }

			CerbCategory c = m_lCerbCats[iSel];
			PwGroup pgBin = (pd.RecycleBinEnabled ? pd.RootGroup.FindGroup(pd.RecycleBinUuid, true) : null);
			PwGroup pgRes = SearchUtil.CreateResultsGroup(c.Name);
			foreach(PwEntry pe in pd.RootGroup.GetEntries(true))
			{
				if((pgBin != null) && pe.IsContainedIn(pgBin)) continue;
				bool bMatch;
				if(iSel == 0) bMatch = true;
				else if(iSel == m_lCerbCats.Count - 1) bMatch = !CerbHasCategory(pe, m_lCerbCats);
				else
				{
					bMatch = false;
					foreach(string t in pe.Tags)
					{
						if(string.Equals(t, c.Name, StringComparison.OrdinalIgnoreCase)) { bMatch = true; break; }
					}
				}
				if(bMatch) pgRes.AddEntry(pe, false, false);
			}
			ShowSearchResults(pgRes, null, pd.RootGroup, false);
		}

		private void BuildCategoryContextMenu()
		{
			ToolStripMenuItem tsmi = new ToolStripMenuItem("Catégorie");
			for(int i = 1; i < m_lCerbCats.Count - 1; ++i)
			{
				string strName = m_lCerbCats[i].Name;
				ToolStripMenuItem t = new ToolStripMenuItem(strName);
				t.Click += delegate { CerberusSetCategory(GetSelectedEntries(), strName); };
				tsmi.DropDownItems.Add(t);
			}
			tsmi.DropDownItems.Add(new ToolStripSeparator());
			ToolStripMenuItem tNone = new ToolStripMenuItem("Aucune");
			tNone.Click += delegate { CerberusSetCategory(GetSelectedEntries(), null); };
			tsmi.DropDownItems.Add(tNone);

			m_ctxPwList.Items.Insert(0, tsmi);
			m_ctxPwList.Items.Insert(1, new ToolStripSeparator());
			m_ctxPwList.Opening += delegate { tsmi.Enabled = (GetSelectedEntriesCount() > 0); };
		}

		private bool CerbApplyCategory(PwEntry pe, string strCat, PwDatabase pd, bool bBackup)
		{
			string strCur = null;
			foreach(string t in pe.Tags)
			{
				for(int i = 1; i < m_lCerbCats.Count - 1; ++i)
				{
					if(string.Equals(t, m_lCerbCats[i].Name, StringComparison.OrdinalIgnoreCase))
					{ strCur = m_lCerbCats[i].Name; break; }
				}
				if(strCur != null) break;
			}
			if(string.Equals(strCur, strCat, StringComparison.OrdinalIgnoreCase)) return false;

			if(bBackup) pe.CreateBackup(pd);
			for(int i = 1; i < m_lCerbCats.Count - 1; ++i) pe.RemoveTag(m_lCerbCats[i].Name);
			if(strCat != null) pe.AddTag(strCat);
			pe.Touch(true, false);
			return true;
		}

		private void CerberusSetCategory(PwEntry[] v, string strCat)
		{
			PwDatabase pd = m_docMgr.ActiveDatabase;
			if((v == null) || (v.Length == 0) || (pd == null) || !pd.IsOpen) return;

			bool bMod = false;
			foreach(PwEntry pe in v) bMod |= CerbApplyCategory(pe, strCat, pd, true);
			if(!bMod) return;

			int iSel = m_lbCerbCats.SelectedIndex;
			if(iSel >= 0) OnCerbCatSelected(m_lbCerbCats, EventArgs.Empty);
			else RefreshEntriesList();
			UpdateUIState(true);
		}

		/// <summary>Heuristic: guess a category for every unclassified entry.</summary>
		private void CerberusAutoClassify()
		{
			PwDatabase pd = m_docMgr.ActiveDatabase;
			if((pd == null) || !pd.IsOpen)
			{
				KeePassLib.Utility.MessageService.ShowInfo("Ouvrez d'abord une base de données.");
				return;
			}

			Regex rxCard = new Regex(@"(?:\d[ -]?){13,19}");
			Regex rxWifi = new Regex(@"wi-?fi|ssid|wpa|wlan|box internet|livebox|freebox|routeur", RegexOptions.IgnoreCase);
			Regex rxSrv = new Regex(@"ssh|ftp|sftp|rdp|vpn|serveur|server|nas|cpanel|mysql|postgres|database|admin\b|^\d{1,3}(\.\d{1,3}){3}", RegexOptions.IgnoreCase);
			Regex rxBank = new Regex(@"banque|bank|crédit|credit agricole|caisse d'épargne|bnp|société générale|lcl|boursorama|paypal|revolut|n26|iban|assurance", RegexOptions.IgnoreCase);
			Regex rxCardTxt = new Regex(@"carte|card|visa|mastercard|amex|cvv|cvc", RegexOptions.IgnoreCase);
			Regex rxId = new Regex(@"passeport|passport|identité|identity|sécu|permis|impots|impôts|ameli|caf\b|france ?connect", RegexOptions.IgnoreCase);
			Regex rxLic = new Regex(@"licen[cs]e|serial|clé produit|product key|activation", RegexOptions.IgnoreCase);

			PwGroup pgBin = (pd.RecycleBinEnabled ? pd.RootGroup.FindGroup(pd.RecycleBinUuid, true) : null);
			int n = 0;
			foreach(PwEntry pe in pd.RootGroup.GetEntries(true))
			{
				if((pgBin != null) && pe.IsContainedIn(pgBin)) continue;
				if(CerbHasCategory(pe, m_lCerbCats)) continue;

				string strTitle = pe.Strings.ReadSafe(PwDefs.TitleField);
				string strUrl = pe.Strings.ReadSafe(PwDefs.UrlField);
				string strUser = pe.Strings.ReadSafe(PwDefs.UserNameField);
				string strPw = pe.Strings.ReadSafe(PwDefs.PasswordField);
				string strNotes = pe.Strings.ReadSafe(PwDefs.NotesField);
				string strGroup = ((pe.ParentGroup != null) ? pe.ParentGroup.Name : string.Empty);
				string strAll = strTitle + " " + strUrl + " " + strGroup + " " + strNotes;
				foreach(KeyValuePair<string, KeePassLib.Security.ProtectedString> kvp in pe.Strings)
				{
					if(!PwDefs.IsStandardField(kvp.Key)) strAll += " " + kvp.Key;
				}

				string strCat;
				if(rxWifi.IsMatch(strAll)) strCat = "Wi-Fi";
				else if(rxCardTxt.IsMatch(strTitle + " " + strGroup) ||
					rxCard.IsMatch(strUser) || rxCard.IsMatch(strNotes)) strCat = "Cartes";
				else if(rxBank.IsMatch(strAll)) strCat = "Banque";
				else if(rxId.IsMatch(strAll)) strCat = "Identités";
				else if(rxLic.IsMatch(strAll)) strCat = "Licences";
				else if(rxSrv.IsMatch(strAll)) strCat = "Serveurs";
				else if((strPw.Length == 0) && (strUser.Length == 0) && (strNotes.Length > 0)) strCat = "Notes";
				else if((strUrl.Length > 0) || (strUser.Length > 0)) strCat = "Connexions";
				else continue;

				if(CerbApplyCategory(pe, strCat, pd, true)) ++n;
			}

			if(n > 0)
			{
				RefreshEntriesList();
				UpdateUIState(true);
			}
			KeePassLib.Utility.MessageService.ShowInfo(n.ToString() + " entrée(s) classée(s)." +
				KeePassLib.Utility.MessageService.NewParagraph +
				"Les catégories sont des tags : ajustez-les avec clic droit → Catégorie. " +
				"Pensez à enregistrer la base.");
		}
	}
}
