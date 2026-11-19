/*
  KeePass Cerberus - pattern lock grid (3x3 to 8x8).
  Licensed under the GNU GPL v2 or later (same as KeePass).
*/

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeePass.Cerberus
{
	public sealed class PatternControl : Control
	{
		private int m_n = 3;
		private readonly List<int> m_lSeq = new List<int>();
		private bool m_bDrawing = false;
		private Point m_ptMouse;

		public event EventHandler PatternChanged;

		public PatternControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
				ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			this.BackColor = Color.FromArgb(30, 32, 40);
			this.Size = new Size(220, 220);
			this.Cursor = Cursors.Hand;
		}

		public int GridSize
		{
			get { return m_n; }
			set
			{
				int n = Math.Max(3, Math.Min(8, value));
				if(n == m_n) return;
				m_n = n;
				Clear();
			}
		}

		public int[] Sequence { get { return m_lSeq.ToArray(); } }

		private bool m_bHideTrace = false;
		/// <summary>Do not draw lines nor highlight selected points.</summary>
		public bool HideTrace
		{
			get { return m_bHideTrace; }
			set { m_bHideTrace = value; Invalidate(); }
		}

		public void Clear()
		{
			m_lSeq.Clear();
			m_bDrawing = false;
			Invalidate();
			if(PatternChanged != null) PatternChanged(this, EventArgs.Empty);
		}

		private float Cell { get { return Math.Min(this.ClientSize.Width, this.ClientSize.Height) / (float)m_n; } }

		private PointF Center(int i)
		{
			float c = this.Cell;
			return new PointF((i % m_n) * c + c / 2f, (i / m_n) * c + c / 2f);
		}

		private int HitTest(Point pt)
		{
			float c = this.Cell, r = c * 0.32f;
			for(int i = 0; i < m_n * m_n; ++i)
			{
				PointF p = Center(i);
				float dx = pt.X - p.X, dy = pt.Y - p.Y;
				if((dx * dx + dy * dy) <= (r * r)) return i;
			}
			return -1;
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if(e.Button != MouseButtons.Left) return;
			m_lSeq.Clear();
			m_bDrawing = true;
			m_ptMouse = e.Location;
			AddAt(e.Location);
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if(!m_bDrawing) return;
			m_ptMouse = e.Location;
			AddAt(e.Location);
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if(!m_bDrawing) return;
			m_bDrawing = false;
			Invalidate();
			if(PatternChanged != null) PatternChanged(this, EventArgs.Empty);
		}

		private void AddAt(Point pt)
		{
			int i = HitTest(pt);
			if((i >= 0) && !m_lSeq.Contains(i)) m_lSeq.Add(i);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.Clear(this.BackColor);

			float c = this.Cell, r = Math.Max(3f, c * 0.12f);
			Color clrAccent = Color.FromArgb(90, 160, 255);

			if(!m_bHideTrace)
			using(Pen pen = new Pen(Color.FromArgb(200, clrAccent), Math.Max(2f, c * 0.06f)))
			{
				pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
				pen.LineJoin = LineJoin.Round;
				for(int k = 1; k < m_lSeq.Count; ++k)
					g.DrawLine(pen, Center(m_lSeq[k - 1]), Center(m_lSeq[k]));
				if(m_bDrawing && (m_lSeq.Count > 0))
					g.DrawLine(pen, Center(m_lSeq[m_lSeq.Count - 1]), m_ptMouse);
			}

			using(SolidBrush brIdle = new SolidBrush(Color.FromArgb(110, 115, 130)))
			using(SolidBrush brOn = new SolidBrush(clrAccent))
			{
				for(int i = 0; i < m_n * m_n; ++i)
				{
					PointF p = Center(i);
					bool bOn = !m_bHideTrace && m_lSeq.Contains(i);
					float rr = (bOn ? r * 1.4f : r);
					g.FillEllipse(bOn ? brOn : brIdle, p.X - rr, p.Y - rr, rr * 2f, rr * 2f);
				}
			}
		}
	}
}
