using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cerberus.App.Controls
{
	/// <summary>Pattern lock grid (3x3 to 8x8).</summary>
	public sealed class PatternPad : FrameworkElement
	{
		private int m_n = 3;
		private readonly List<int> m_seq = new List<int>();
		private bool m_bDrawing;
		private Point m_ptMouse;

		public event EventHandler PatternChanged;

		public PatternPad()
		{
			Width = 230; Height = 230;
			Cursor = Cursors.Hand;
			Focusable = true;
		}

		public int GridSize
		{
			get { return m_n; }
			set { int n = Math.Max(3, Math.Min(8, value)); if(n != m_n) { m_n = n; Clear(); } }
		}

		private bool m_bHide;
		public bool HideTrace { get { return m_bHide; } set { m_bHide = value; InvalidateVisual(); } }

		public int[] Sequence { get { return m_seq.ToArray(); } }

		public void Clear()
		{
			m_seq.Clear(); m_bDrawing = false; InvalidateVisual();
			PatternChanged?.Invoke(this, EventArgs.Empty);
		}

		private double Cell { get { return Math.Min(ActualWidth, ActualHeight) / m_n; } }

		private Point Center(int i)
		{
			double c = Cell;
			return new Point((i % m_n) * c + c / 2, (i / m_n) * c + c / 2);
		}

		private int Hit(Point pt)
		{
			double r = Cell * 0.32;
			for(int i = 0; i < m_n * m_n; ++i)
			{
				Vector d = pt - Center(i);
				if(d.Length <= r) return i;
			}
			return -1;
		}

		protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
		{
			base.OnMouseLeftButtonDown(e);
			Focus();
			CaptureMouse();
			m_seq.Clear(); m_bDrawing = true;
			m_ptMouse = e.GetPosition(this);
			Add(m_ptMouse);
			InvalidateVisual();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if(!m_bDrawing) return;
			m_ptMouse = e.GetPosition(this);
			Add(m_ptMouse);
			InvalidateVisual();
		}

		protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
		{
			base.OnMouseLeftButtonUp(e);
			if(!m_bDrawing) return;
			m_bDrawing = false;
			ReleaseMouseCapture();
			InvalidateVisual();
			PatternChanged?.Invoke(this, EventArgs.Empty);
		}

		private void Add(Point pt)
		{
			int i = Hit(pt);
			if(i >= 0 && !m_seq.Contains(i)) m_seq.Add(i);
		}

		private Brush Res(string k, Color fb)
		{
			return (TryFindResource(k) as Brush) ?? new SolidColorBrush(fb);
		}

		protected override void OnRender(DrawingContext dc)
		{
			Brush bg = Res("Surface", Colors.Black), idle = Res("BorderStrong", Colors.Gray), acc = Res("Accent", Colors.DodgerBlue);
			dc.DrawRoundedRectangle(bg, new Pen(Res("Border", Colors.Gray), 1), new Rect(0, 0, ActualWidth, ActualHeight), 12, 12);
			double c = Cell, r = Math.Max(3, c * 0.09);

			if(!m_bHide && m_seq.Count > 0)
			{
				Pen pen = new Pen(acc, Math.Max(3, c * 0.06)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
				for(int k = 1; k < m_seq.Count; ++k) dc.DrawLine(pen, Center(m_seq[k - 1]), Center(m_seq[k]));
				if(m_bDrawing) dc.DrawLine(pen, Center(m_seq[m_seq.Count - 1]), m_ptMouse);
			}
			for(int i = 0; i < m_n * m_n; ++i)
			{
				bool on = !m_bHide && m_seq.Contains(i);
				dc.DrawEllipse(on ? acc : idle, null, Center(i), on ? r * 1.5 : r, on ? r * 1.5 : r);
			}
		}
	}
}
