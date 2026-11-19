/*
  KeePass Cerberus - harmonised modern look for every KeePass dialog:
  flat banner, flat tab strip, flat check boxes / radio buttons / groups.
  Licensed under the GNU GPL v2 or later (same as KeePass).
*/

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

using KeePass.UI;

namespace KeePass.Cerberus
{
	internal static class CerberusModern
	{
		public static void Install()
		{
			BannerFactory.CustomGenerator = CreateBanner;
		}

		/// <summary>Flat header replacing KeePass' gradient banners.</summary>
		private static Image CreateBanner(BfBannerInfo bi)
		{
			int w = Math.Max(1, bi.Width), h = Math.Max(1, bi.Height);
			Bitmap bmp = new Bitmap(w, h);
			using(Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;

				using(SolidBrush br = new SolidBrush(CerberusTheme.Panel))
					g.FillRectangle(br, 0, 0, w, h);
				int hBar = Math.Max(2, h / 20);
				using(SolidBrush br = new SolidBrush(CerberusTheme.Accent))
					g.FillRectangle(br, 0, h - hBar, w, hBar);

				int x = (int)(h * 0.22);
				if(bi.Icon != null)
				{
					int s = (int)(h * 0.5);
					Rectangle rCircle = new Rectangle(x, (h - hBar - (int)(s * 1.5)) / 2,
						(int)(s * 1.5), (int)(s * 1.5));
					using(SolidBrush br = new SolidBrush(Color.FromArgb(CerberusTheme.Dark ? 45 : 30, CerberusTheme.Accent)))
						g.FillEllipse(br, rCircle);
					g.DrawImage(bi.Icon, new Rectangle(rCircle.X + (rCircle.Width - s) / 2,
						rCircle.Y + (rCircle.Height - s) / 2, s, s));
					x = rCircle.Right + (int)(h * 0.2);
				}

				float fTitle = Math.Max(9f, h * 0.20f);
				float fLine = Math.Max(7.5f, h * 0.12f);
				using(Font f1 = new Font("Segoe UI Semibold", fTitle, FontStyle.Regular, GraphicsUnit.Pixel))
				using(Font f2 = new Font("Segoe UI", fLine * 1.25f, FontStyle.Regular, GraphicsUnit.Pixel))
				{
					int yTitle = (int)(h * 0.18);
					TextRenderer.DrawText(g, bi.TitleText ?? string.Empty, f1,
						new Rectangle(x, yTitle, w - x - 8, (int)(fTitle * 1.5)), CerberusTheme.Text,
						TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
					TextRenderer.DrawText(g, bi.InfoText ?? string.Empty, f2,
						new Rectangle(x, yTitle + (int)(fTitle * 1.45), w - x - 8, (int)(fLine * 2.2)),
						CerberusTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
				}
			}
			return bmp;
		}

		/// <summary>Modernise one control (called for every control of every dialog).</summary>
		public static void Apply(Control c)
		{
			TabControl tc = (c as TabControl);
			if(tc != null) { FlatTabs(tc); return; }

			CheckBox cb = (c as CheckBox);
			if((cb != null) && (cb.Appearance == Appearance.Normal))
			{
				cb.FlatStyle = FlatStyle.Flat;
				cb.FlatAppearance.BorderColor = CerberusTheme.Muted;
				cb.FlatAppearance.CheckedBackColor = CerberusTheme.Surface;
				cb.FlatAppearance.MouseOverBackColor = CerberusTheme.Hover;
				return;
			}
			RadioButton rb = (c as RadioButton);
			if((rb != null) && (rb.Appearance == Appearance.Normal))
			{
				rb.FlatStyle = FlatStyle.Flat;
				rb.FlatAppearance.BorderColor = CerberusTheme.Muted;
				rb.FlatAppearance.MouseOverBackColor = CerberusTheme.Hover;
				return;
			}
			GroupBox gb = (c as GroupBox);
			if(gb != null)
			{
				gb.FlatStyle = FlatStyle.Flat;
				gb.ForeColor = CerberusTheme.Accent;
				foreach(Control ch in gb.Controls)
				{
					if(ch.ForeColor == CerberusTheme.Accent) ch.ForeColor = CerberusTheme.Text;
				}
				return;
			}
		}

		// ---- Flat tab strip --------------------------------------------

		private sealed class TabStrip : Control
		{
			private readonly TabControl m_tc;
			private int m_iHover = -1;

			public TabStrip(TabControl tc)
			{
				m_tc = tc;
				SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
					ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
				this.Font = CerberusTheme.UiFont(9.5f, FontStyle.Regular);
				this.Cursor = Cursors.Hand;
				tc.SelectedIndexChanged += delegate { Invalidate(); };
				tc.ControlAdded += delegate { Invalidate(); };
				tc.ControlRemoved += delegate { Invalidate(); };
				tc.VisibleChanged += delegate { this.Visible = tc.Visible; };
				tc.EnabledChanged += delegate { Invalidate(); };
			}

			private Rectangle[] Layout(Graphics g)
			{
				Rectangle[] v = new Rectangle[m_tc.TabPages.Count];
				int x = 2;
				for(int i = 0; i < v.Length; ++i)
				{
					Size sz = TextRenderer.MeasureText(g, m_tc.TabPages[i].Text, this.Font);
					int w = sz.Width + 22;
					v[i] = new Rectangle(x, 0, w, this.Height);
					x += w + 2;
				}
				return v;
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				Graphics g = e.Graphics;
				g.Clear(CerberusTheme.Back);
				Rectangle[] v = Layout(g);
				using(Pen pBase = new Pen(CerberusTheme.Border))
					g.DrawLine(pBase, 0, this.Height - 1, this.Width, this.Height - 1);
				for(int i = 0; i < v.Length; ++i)
				{
					bool bSel = (i == m_tc.SelectedIndex);
					if((i == m_iHover) && !bSel)
						using(SolidBrush br = new SolidBrush(CerberusTheme.Hover)) g.FillRectangle(br, v[i]);
					Color clr = (bSel ? CerberusTheme.Text : CerberusTheme.Muted);
					if(!m_tc.Enabled) clr = CerberusTheme.Border;
					TextRenderer.DrawText(g, m_tc.TabPages[i].Text,
						bSel ? new Font(this.Font, FontStyle.Bold) : this.Font, v[i], clr,
						TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
					if(bSel)
						using(SolidBrush br = new SolidBrush(CerberusTheme.Accent))
							g.FillRectangle(br, v[i].X + 4, this.Height - 3, v[i].Width - 8, 3);
				}
			}

			private int HitTest(Point pt)
			{
				using(Graphics g = CreateGraphics())
				{
					Rectangle[] v = Layout(g);
					for(int i = 0; i < v.Length; ++i) { if(v[i].Contains(pt)) return i; }
				}
				return -1;
			}

			protected override void OnMouseMove(MouseEventArgs e)
			{
				base.OnMouseMove(e);
				int i = HitTest(e.Location);
				if(i != m_iHover) { m_iHover = i; Invalidate(); }
			}

			protected override void OnMouseLeave(EventArgs e)
			{
				base.OnMouseLeave(e);
				m_iHover = -1; Invalidate();
			}

			protected override void OnMouseDown(MouseEventArgs e)
			{
				base.OnMouseDown(e);
				if(!m_tc.Enabled) return;
				int i = HitTest(e.Location);
				if(i >= 0) m_tc.SelectedIndex = i;
			}
		}

		private static void FlatTabs(TabControl tc)
		{
			if((tc.Parent == null) || (tc.Tag as string == "cerb-tabs")) return;
			if(tc.Alignment != TabAlignment.Top) return;
			tc.Tag = "cerb-tabs";

			Action a = delegate
			{
				try
				{
					int yOld = tc.DisplayRectangle.Top;
					tc.Appearance = TabAppearance.Buttons;
					tc.SizeMode = TabSizeMode.Fixed;
					tc.ItemSize = new Size(0, 1);
					int yNew = tc.DisplayRectangle.Top;
					int hStrip = Math.Max(24, yOld - yNew + 6);

					TabStrip ts = new TabStrip(tc);
					ts.Bounds = new Rectangle(tc.Left, tc.Top, tc.Width, hStrip);
					ts.Anchor = (tc.Anchor & ~AnchorStyles.Bottom) | AnchorStyles.Top;
					if((tc.Dock == DockStyle.Fill) || (tc.Dock == DockStyle.Top))
					{
						ts.Dock = DockStyle.Top;
						tc.Parent.Controls.Add(ts);
						ts.BringToFront();
						tc.BringToFront(); // fill below the strip
					}
					else
					{
						tc.Parent.Controls.Add(ts);
						ts.BringToFront();
						tc.Top += hStrip - 2;
						tc.Height -= hStrip - 2;
					}
					foreach(TabPage tp in tc.TabPages) tp.BackColor = CerberusTheme.Back;
				}
				catch(Exception) { }
			};
			if(tc.IsHandleCreated) a();
			else tc.HandleCreated += delegate { a(); };
		}
	}
}
