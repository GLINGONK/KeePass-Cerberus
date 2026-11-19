/*
  KeePass Cerberus - single switch for the screen-capture protection.
  Licensed under the GNU GPL v2 or later (same as KeePass).
*/

using System;
using System.Windows.Forms;

using KeePass.UI;

namespace KeePass.Cerberus
{
	internal static class CerberusCapture
	{
		public static event EventHandler Changed;

		public static bool Enabled
		{
			get { return Program.Config.Security.PreventScreenCapture; }
		}

		/// <summary>Turn the protection on/off, applied immediately to every open window.</summary>
		public static void Set(bool bEnabled)
		{
			Program.Config.Security.PreventScreenCapture = bEnabled;
			foreach(Form f in Application.OpenForms)
			{
				try
				{
					if(f.IsHandleCreated) GlobalWindowManager.SetCaptureProtection(f.Handle, bEnabled);
				}
				catch(Exception) { }
			}
			try { KeePass.App.Configuration.AppConfigSerializer.Save(Program.Config); } catch(Exception) { } // keep the choice even after a crash
			EventHandler h = Changed;
			if(h != null) h(null, EventArgs.Empty);
		}

		public const string Label = "Anti-capture d'écran";

		public static CheckBox CreateCheckBox()
		{
			CheckBox cb = new CheckBox();
			cb.Text = Label;
			cb.AutoSize = true;
			cb.Checked = Enabled;
			bool bSync = false;
			cb.CheckedChanged += delegate { if(!bSync) Set(cb.Checked); };
			EventHandler h = delegate { bSync = true; cb.Checked = Enabled; bSync = false; };
			Changed += h;
			cb.Disposed += delegate { Changed -= h; };
			new ToolTip().SetToolTip(cb, "Coché : les fenêtres apparaissent en noir dans les captures et partages d'écran.");
			return cb;
		}
	}
}
