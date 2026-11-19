/*
  KeePass Cerberus - modern dark / light theme for every window.
  Licensed under the GNU GPL v2 or later (same as KeePass).
*/

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

using KeePass.UI;

namespace KeePass.Cerberus
{
	internal static class CerberusTheme
	{
		public const string CfgDark = "Cerberus.DarkTheme";

		private static bool? g_obDark = null;
		public static bool Dark
		{
			get
			{
				if(!g_obDark.HasValue)
					g_obDark = Program.Config.CustomConfig.GetBool(CfgDark, true);
				return g_obDark.Value;
			}
		}

		public static void SetDarkForNextStart(bool bDark)
		{
			Program.Config.CustomConfig.SetBool(CfgDark, bDark);
		}

		// Palette
		public static Color Back { get { return Dark ? Color.FromArgb(24, 26, 33) : Color.FromArgb(245, 247, 250); } }
		public static Color Panel { get { return Dark ? Color.FromArgb(31, 34, 43) : Color.White; } }
		public static Color Surface { get { return Dark ? Color.FromArgb(40, 44, 56) : Color.White; } }
		public static Color Border { get { return Dark ? Color.FromArgb(58, 63, 80) : Color.FromArgb(214, 219, 228); } }
		public static Color Text { get { return Dark ? Color.FromArgb(230, 233, 240) : Color.FromArgb(28, 32, 40); } }
		public static Color Muted { get { return Dark ? Color.FromArgb(150, 157, 178) : Color.FromArgb(105, 112, 128); } }
		public static Color Accent { get { return Color.FromArgb(64, 140, 255); } }
		public static Color Selection { get { return Dark ? Color.FromArgb(45, 74, 125) : Color.FromArgb(214, 230, 255); } }
		public static Color Hover { get { return Dark ? Color.FromArgb(48, 53, 68) : Color.FromArgb(232, 238, 248); } }

		public static Font UiFont(float fSize, FontStyle fs)
		{
			try { return new Font("Segoe UI", fSize, fs); }
			catch(Exception) { return new Font(FontFamily.GenericSansSerif, fSize, fs); }
		}

		[DllImport("UxTheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

		[DllImport("DwmApi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hWnd, int iAttr, ref int iValue, int cbValue);

		[DllImport("User32.dll")]
		private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

		private const int LVM_GETHEADER = 0x101F;

		public static void Apply(Form f)
		{
			if(f == null) return;
			try
			{
				f.BackColor = Back;
				f.ForeColor = Text;
				ApplyRec(f);
				Guard(f);
				f.Shown += delegate { ApplyRec(f); FixListViewItems(f); };

				if(Dark)
				{
					if(f.IsHandleCreated) DarkTitleBar(f.Handle);
					else f.HandleCreated += delegate { DarkTitleBar(f.Handle); };
				}
			}
			catch(Exception) { }
		}

		// ---- Readability guard ----------------------------------------
		// KeePass recolours many controls after a form is loaded (input
		// validation, read-only fields, list items ...). In dark mode, any
		// light background or dark text set later is pulled back into the
		// palette; strongly coloured backgrounds (errors) keep a dark tint.

		private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>
			g_tGuarded = new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

		private static double Luma(Color c)
		{
			return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
		}

		private static Color Blend(Color a, Color b, double t)
		{
			return Color.FromArgb((int)(a.R * t + b.R * (1 - t)),
				(int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
		}

		private static bool IsInput(Control c)
		{
			return (c is TextBoxBase) || (c is ComboBox) || (c is ListBox) ||
				(c is NumericUpDown) || (c is ButtonBase && !(c is CheckBox) && !(c is RadioButton));
		}

		internal static Color DarkenBack(Color c, Control cOwner)
		{
			if(c.A == 0) return c; // transparent
			if(Luma(c) < 0.6) return c;
			Color b = ((cOwner is ListView || cOwner is TreeView) ? Panel :
				(IsInput(cOwner) ? Surface : Back));
			float s = c.GetSaturation();
			if(s > 0.25f) return Blend(c, b, 0.28); // keep a tint (error / warning)
			return b;
		}

		internal static Color LightenFore(Color c)
		{
			if(c.A == 0) return c;
			if(Luma(c) > 0.45) return c;
			float s = c.GetSaturation();
			if(s > 0.35f) return Blend(c, Color.White, 0.55); // coloured text, brightened
			return Text;
		}

		private static void Guard(Control c)
		{
			if(!Dark || (c == null)) return;
			if(c is PictureBox || c is ImageComboBoxEx || c is ProgressBar) return;
			object o;
			if(g_tGuarded.TryGetValue(c, out o)) return;
			g_tGuarded.Add(c, null);

			c.BackColorChanged += delegate
			{
				Color n = DarkenBack(c.BackColor, c);
				if(n != c.BackColor) c.BackColor = n;
			};
			c.ForeColorChanged += delegate
			{
				Color n = LightenFore(c.ForeColor);
				if(n != c.ForeColor) c.ForeColor = n;
			};
			c.ControlAdded += delegate(object sender, ControlEventArgs e)
			{
				ApplyOne(e.Control);
				ApplyRec(e.Control);
			};
			c.EnabledChanged += delegate { c.Invalidate(); };

			Color nb = DarkenBack(c.BackColor, c);
			if(nb != c.BackColor) c.BackColor = nb;
			Color nf = LightenFore(c.ForeColor);
			if(nf != c.ForeColor) c.ForeColor = nf;
		}

		public static void FixListViewItems(Control cRoot)
		{
			if(!Dark || (cRoot == null)) return;
			foreach(Control c in cRoot.Controls)
			{
				ListView lv = (c as ListView);
				if(lv != null)
				{
					foreach(ListViewItem lvi in lv.Items)
					{
						lvi.BackColor = DarkenBack(lvi.BackColor, lv);
						lvi.ForeColor = LightenFore(lvi.ForeColor);
					}
				}
				FixListViewItems(c);
			}
		}

		private static void DarkTitleBar(IntPtr h)
		{
			try
			{
				int v = 1;
				if(DwmSetWindowAttribute(h, 20, ref v, 4) != 0)
					DwmSetWindowAttribute(h, 19, ref v, 4); // older Windows 10 builds
			}
			catch(Exception) { }
		}

		private static void ExplorerTheme(Control c)
		{
			Action a = delegate
			{
				try
				{
					SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
					ListView lv = (c as ListView);
					if(lv != null)
					{
						IntPtr hHdr = SendMessage(lv.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
						if(hHdr != IntPtr.Zero)
							SetWindowTheme(hHdr, Dark ? "DarkMode_ItemsView" : "ItemsView", null);
					}
				}
				catch(Exception) { }
			};
			if(c.IsHandleCreated) a();
			else c.HandleCreated += delegate { a(); };
		}

		private static void ApplyRec(Control c)
		{
			foreach(Control ch in c.Controls)
			{
				ApplyOne(ch);
				if(!(ch is UserControl) || (ch.Controls.Count > 0)) ApplyRec(ch);
			}
		}

		public static void ApplyOne(Control c)
		{
			if(c is ImageComboBoxEx || c is PictureBox) return;
			Guard(c);
			CerberusModern.Apply(c);

			ToolStrip ts = (c as ToolStrip);
			if(ts != null)
			{
				ts.Renderer = new ToolStripProfessionalRenderer(new CerberusColorTable()) { RoundedEdges = false };
				ts.BackColor = Panel;
				ts.ForeColor = Text;
				foreach(ToolStripItem it in ts.Items) ApplyItem(it);
				return;
			}

			if(c is TextBoxBase)
			{
				c.BackColor = Surface; c.ForeColor = Text;
				TextBox tb = (c as TextBox);
				if(tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
				return;
			}
			if(c is ComboBox || c is NumericUpDown || c is ListBox)
			{
				c.BackColor = Surface; c.ForeColor = Text;
				return;
			}
			if(c is ListView || c is TreeView)
			{
				c.BackColor = Panel; c.ForeColor = Text;
				ListView lv = (c as ListView);
				if(lv != null) lv.BorderStyle = BorderStyle.None;
				TreeView tv = (c as TreeView);
				if(tv != null) { tv.BorderStyle = BorderStyle.None; tv.LineColor = Border; }
				ExplorerTheme(c);
				return;
			}

			ButtonBase bb = (c as ButtonBase);
			if((bb != null) && !(c is CheckBox) && !(c is RadioButton))
			{
				bb.FlatStyle = FlatStyle.Flat;
				bb.FlatAppearance.BorderColor = Border;
				bb.FlatAppearance.MouseOverBackColor = Hover;
				bb.FlatAppearance.MouseDownBackColor = Selection;
				bb.BackColor = Surface;
				bb.ForeColor = Text;
				return;
			}

			if(c is LinkLabel)
			{
				LinkLabel ll = (LinkLabel)c;
				ll.LinkColor = Accent; ll.ActiveLinkColor = Accent; ll.VisitedLinkColor = Accent;
				ll.BackColor = Color.Transparent;
				return;
			}

			if(c is TabPage) { c.BackColor = Back; c.ForeColor = Text; return; }
			if(c is TabControl) return; // native tabs cannot be recoloured
			if(c is ProgressBar) return;

			// Labels, check boxes, group boxes, panels, split containers ...
			// keep explicit (warning) colours set by KeePass
			if(c.ForeColor == SystemColors.ControlText || c.ForeColor == SystemColors.WindowText)
				c.ForeColor = Text;
			if(c is SplitContainer)
			{
				c.BackColor = Border;
				SplitContainer sc = (SplitContainer)c;
				sc.Panel1.BackColor = Back; sc.Panel2.BackColor = Back;
			}
			else if(!(c is Label) || (c.BackColor == SystemColors.Control))
				c.BackColor = (c is Label) ? Color.Transparent : Back;
		}

		private static void ApplyItem(ToolStripItem it)
		{
			it.ForeColor = Text;
			ToolStripDropDownItem dd = (it as ToolStripDropDownItem);
			if(dd != null)
			{
				dd.DropDown.BackColor = Panel;
				dd.DropDown.ForeColor = Text;
				dd.DropDown.Renderer = new ToolStripProfessionalRenderer(new CerberusColorTable()) { RoundedEdges = false };
				dd.DropDownOpening += delegate
				{
					foreach(ToolStripItem sub in dd.DropDownItems) ApplyItem(sub);
				};
			}
			ToolStripComboBox cb = (it as ToolStripComboBox);
			if(cb != null) { cb.BackColor = Surface; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat; }
			ToolStripTextBox tb = (it as ToolStripTextBox);
			if(tb != null) { tb.BackColor = Surface; tb.ForeColor = Text; }
		}

		public static void ApplyContextMenu(ContextMenuStrip cms)
		{
			if(cms == null) return;
			cms.Renderer = new ToolStripProfessionalRenderer(new CerberusColorTable()) { RoundedEdges = false };
			cms.BackColor = Panel;
			cms.ForeColor = Text;
			cms.Opening += delegate { foreach(ToolStripItem it in cms.Items) ApplyItem(it); };
		}
	}

	internal sealed class CerberusColorTable : ProfessionalColorTable
	{
		public CerberusColorTable() { this.UseSystemColors = false; }

		private static Color P { get { return CerberusTheme.Panel; } }
		private static Color S { get { return CerberusTheme.Selection; } }
		private static Color H { get { return CerberusTheme.Hover; } }
		private static Color B { get { return CerberusTheme.Border; } }

		public override Color ToolStripGradientBegin { get { return P; } }
		public override Color ToolStripGradientMiddle { get { return P; } }
		public override Color ToolStripGradientEnd { get { return P; } }
		public override Color ToolStripBorder { get { return P; } }
		public override Color ToolStripDropDownBackground { get { return P; } }
		public override Color ToolStripContentPanelGradientBegin { get { return P; } }
		public override Color ToolStripContentPanelGradientEnd { get { return P; } }
		public override Color MenuStripGradientBegin { get { return P; } }
		public override Color MenuStripGradientEnd { get { return P; } }
		public override Color MenuBorder { get { return B; } }
		public override Color MenuItemBorder { get { return S; } }
		public override Color MenuItemSelected { get { return H; } }
		public override Color MenuItemSelectedGradientBegin { get { return H; } }
		public override Color MenuItemSelectedGradientEnd { get { return H; } }
		public override Color MenuItemPressedGradientBegin { get { return S; } }
		public override Color MenuItemPressedGradientMiddle { get { return S; } }
		public override Color MenuItemPressedGradientEnd { get { return S; } }
		public override Color ImageMarginGradientBegin { get { return P; } }
		public override Color ImageMarginGradientMiddle { get { return P; } }
		public override Color ImageMarginGradientEnd { get { return P; } }
		public override Color ButtonSelectedHighlight { get { return H; } }
		public override Color ButtonSelectedGradientBegin { get { return H; } }
		public override Color ButtonSelectedGradientMiddle { get { return H; } }
		public override Color ButtonSelectedGradientEnd { get { return H; } }
		public override Color ButtonSelectedBorder { get { return S; } }
		public override Color ButtonPressedGradientBegin { get { return S; } }
		public override Color ButtonPressedGradientMiddle { get { return S; } }
		public override Color ButtonPressedGradientEnd { get { return S; } }
		public override Color ButtonCheckedGradientBegin { get { return S; } }
		public override Color ButtonCheckedGradientMiddle { get { return S; } }
		public override Color ButtonCheckedGradientEnd { get { return S; } }
		public override Color SeparatorDark { get { return B; } }
		public override Color SeparatorLight { get { return P; } }
		public override Color GripDark { get { return B; } }
		public override Color GripLight { get { return P; } }
		public override Color StatusStripGradientBegin { get { return P; } }
		public override Color StatusStripGradientEnd { get { return P; } }
		public override Color OverflowButtonGradientBegin { get { return P; } }
		public override Color OverflowButtonGradientMiddle { get { return P; } }
		public override Color OverflowButtonGradientEnd { get { return P; } }
		public override Color CheckBackground { get { return S; } }
		public override Color CheckSelectedBackground { get { return S; } }
		public override Color CheckPressedBackground { get { return S; } }
	}
}
