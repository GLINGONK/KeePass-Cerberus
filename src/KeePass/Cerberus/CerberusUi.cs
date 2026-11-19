/*
  KeePass Cerberus - makes the Cerberus factors visible in the
  master key creation / prompt dialogs.
  Licensed under the GNU GPL v2 or later (same as KeePass).
*/

using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeePass.Cerberus
{
	internal static class CerberusUi
	{
		/// <summary>
		/// Adds a "Cerberus factors" checkbox to a key dialog. Checking it
		/// enables the key-file/provider row (and expert mode if any) and
		/// selects the Cerberus key provider; unchecking clears it.
		/// </summary>
		public static void AttachFactorsOption(Form f, CheckBox cbKeyFile,
			ComboBox cmbKeyFile, CheckBox cbExpert, Control cNeighbor)
		{
			f.Load += delegate
			{
				CheckBox cb = new CheckBox();
				cb.Text = "Facteurs Cerberus";
				new ToolTip().SetToolTip(cb, "PIN, motif et/ou parts Shamir, en plus du mot de passe maître");
				cb.AutoSize = true;
				cb.Font = new Font(f.Font, FontStyle.Bold);
				cb.ForeColor = Color.FromArgb(0, 102, 204);
				cb.Location = new Point(cNeighbor.Right + (cNeighbor.Height * 2), cNeighbor.Top);
				f.Controls.Add(cb);
				cb.BringToFront();

				bool bSync = false;
				Func<bool> fIsSel = () => cbKeyFile.Checked &&
					(cmbKeyFile.Text == CerberusKeyProvider.ProviderName);

				cb.Checked = fIsSel();
				cb.CheckedChanged += delegate
				{
					if(bSync) return;
					bSync = true;
					try
					{
						if(cb.Checked)
						{
							if((cbExpert != null) && !cbExpert.Checked) cbExpert.Checked = true;
							cbKeyFile.Checked = true;
							int i = cmbKeyFile.FindStringExact(CerberusKeyProvider.ProviderName);
							if(i >= 0) cmbKeyFile.SelectedIndex = i;
						}
						else if(fIsSel())
						{
							cmbKeyFile.SelectedIndex = -1;
							cmbKeyFile.Text = string.Empty;
							cbKeyFile.Checked = false;
						}
					}
					finally { bSync = false; }
				};

				EventHandler eh = delegate
				{
					if(bSync) return;
					bSync = true;
					cb.Checked = fIsSel();
					bSync = false;
				};
				cmbKeyFile.SelectedIndexChanged += eh;
				cmbKeyFile.TextChanged += eh;
				cbKeyFile.CheckedChanged += eh;
			};
		}
	}
}
